using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using DonateWeb.ViewModels.Wallet;

namespace DonateWeb.Controllers
{
    [Authorize]
    public class WalletController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IAuthService _authService;
        private readonly ILogger<WalletController> _logger;
        private readonly IConfiguration _configuration;

        public WalletController(
            AppDbContext context,
            IAuthService authService,
            ILogger<WalletController> logger,
            IConfiguration configuration)
        {
            _context = context;
            _authService = authService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Đảm bảo bảng WalletTransactions và WithdrawalRequests luôn tồn tại trong Database
        /// (Tránh lỗi SqlException khi chạy trực tiếp trên DB cũ chưa khởi động lại)
        /// </summary>
        private async Task EnsureWalletTablesExistAsync()
        {
            await _context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.WalletTransactions (
                        Id INT IDENTITY(1,1) NOT NULL,
                        UserId INT NOT NULL,
                        TransactionCode NVARCHAR(100) NOT NULL,
                        TransactionType NVARCHAR(30) NOT NULL,
                        Amount DECIMAL(18,2) NOT NULL,
                        BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
                        PaymentMethodName NVARCHAR(100) NULL,
                        [Status] INT NOT NULL DEFAULT 1,
                        Note NVARCHAR(500) NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_WalletTransactions PRIMARY KEY CLUSTERED (Id),
                        CONSTRAINT FK_WalletTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_TransactionCode ON dbo.WalletTransactions (TransactionCode);
                    CREATE NONCLUSTERED INDEX IX_WalletTransactions_UserId_CreatedAt ON dbo.WalletTransactions (UserId, CreatedAt);
                END

                IF OBJECT_ID('dbo.WithdrawalRequests', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.WithdrawalRequests (
                        Id INT IDENTITY(1,1) NOT NULL,
                        UserId INT NOT NULL,
                        StreamerProfileId INT NULL,
                        TransactionCode NVARCHAR(100) NOT NULL,
                        Amount DECIMAL(18,2) NOT NULL,
                        BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BankName NVARCHAR(100) NOT NULL,
                        BankAccountNumber NVARCHAR(50) NOT NULL,
                        BankAccountName NVARCHAR(100) NOT NULL,
                        Note NVARCHAR(500) NULL,
                        [Status] INT NOT NULL DEFAULT 0,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        ProcessedAt DATETIME2 NULL,
                        CONSTRAINT PK_WithdrawalRequests PRIMARY KEY CLUSTERED (Id),
                        CONSTRAINT FK_WithdrawalRequests_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
                        CONSTRAINT FK_WithdrawalRequests_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id)
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX IX_WithdrawalRequests_TransactionCode ON dbo.WithdrawalRequests (TransactionCode);
                    CREATE NONCLUSTERED INDEX IX_WithdrawalRequests_UserId_CreatedAt ON dbo.WithdrawalRequests (UserId, CreatedAt);
                END
            ");
        }

        /// <summary>
        /// Đồng bộ số dư của Streamer theo dữ liệu donate thực tế:
        /// Nếu chưa có dữ liệu donate (actualTotal == 0) thì số dư của Streamer được đặt về 0.
        /// Nếu đã có dữ liệu donate thì số dư = Tổng donate thành công - Tổng tiền đã yêu cầu rút.
        /// </summary>
        private async Task SyncStreamerBalanceIfNeededAsync(User user)
        {
            if (user.StreamerProfile == null) return;

            var actualTotalDonations = await _context.Donations
                .Where(d => d.StreamerProfileId == user.StreamerProfile.Id && d.Status == DonationStatus.Success)
                .SumAsync(d => (decimal?)d.Amount) ?? 0m;

            var totalWithdrawn = await _context.WithdrawalRequests
                .Where(wr => wr.UserId == user.Id && (wr.Status == 0 || wr.Status == 1))
                .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;

            var expectedBalance = actualTotalDonations == 0m
                ? 0m
                : Math.Max(0m, actualTotalDonations - totalWithdrawn);

            bool changed = false;
            if (user.StreamerProfile.TotalReceived != actualTotalDonations)
            {
                user.StreamerProfile.TotalReceived = actualTotalDonations;
                changed = true;
            }

            if (user.WalletBalance != expectedBalance)
            {
                user.WalletBalance = expectedBalance;
                changed = true;
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
                await RefreshUserClaimsCookieAsync(user);
            }
        }

        /// <summary>
        /// Điều hướng tự động theo Role của User:
        /// - Streamer -> Trang Rút tiền (/Wallet/Withdraw)
        /// - Viewer -> Trang Nạp tiền (/Wallet/Deposit)
        /// </summary>
        [HttpGet]
        public IActionResult Index()
        {
            if (User.IsInRole(UserRoles.Streamer))
            {
                return RedirectToAction(nameof(Withdraw));
            }
            return RedirectToAction(nameof(Deposit));
        }

        // =========================================================================
        // 1. CHỨC NĂNG NẠP TIỀN (DEPOSIT) DÀNH CHO ROLE VIEWER (VIETQR NAPAS247)
        // =========================================================================

        /// <summary>
        /// Lấy thông tin tài khoản ngân hàng thụ hưởng từ appsettings.json
        /// </summary>
        private (string BankId, string AccountNo, string AccountName, string BankName) GetBankConfig()
        {
            var bankId = _configuration["BankConfig:BankId"] ?? "MB";
            var accountNo = _configuration["BankConfig:AccountNo"] ?? "0987654321";
            var accountName = _configuration["BankConfig:AccountName"] ?? "NGUYEN ANH HIEU";
            var bankName = _configuration["BankConfig:BankName"] ?? "MB Bank (Ngân Hàng Quân Đội)";
            return (bankId, accountNo, accountName, bankName);
        }

        /// <summary>
        /// GET: /Wallet/Deposit
        /// Hiển thị form nạp tiền cho Viewer với các mốc nạp nhanh và số dư hiện tại
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Deposit()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            await EnsureWalletTablesExistAsync();

            var user = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // Nếu tài khoản là Streamer, điều hướng sang chức năng Rút tiền dành cho Streamer
            if (User.IsInRole(UserRoles.Streamer) || user.StreamerProfile != null)
            {
                return RedirectToAction(nameof(Withdraw));
            }

            var (bankId, accountNo, accountName, bankName) = GetBankConfig();

            var model = new DepositViewModel
            {
                Amount = 50000m,
                PaymentMethod = "Chuyển khoản ngân hàng (VietQR)",
                CurrentBalance = user.WalletBalance,
                Username = user.Username,
                FullName = user.FullName,
                BankId = bankId,
                AccountNo = accountNo,
                AccountName = accountName,
                BankName = bankName,
                RecentTransactions = await _context.WalletTransactions
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT")
                    .OrderByDescending(wt => wt.CreatedAt)
                    .Take(10)
                    .ToListAsync()
            };

            return View(model);
        }

        /// <summary>
        /// POST: /Wallet/CreateDeposit
        /// Nhận số tiền nạp, tạo bản ghi WalletTransaction Pending, sinh mã Memo duy nhất và URL VietQR Napas247 động
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDeposit(DepositRequestViewModel request)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Json(new { success = false, message = "Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại." });
            }

            await EnsureWalletTablesExistAsync();

            var user = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return Json(new { success = false, message = "Không tìm thấy thông tin tài khoản người dùng." });
            }

            if (User.IsInRole(UserRoles.Streamer) || user.StreamerProfile != null)
            {
                return Json(new { success = false, message = "Chức năng Nạp tiền chỉ dành cho tài khoản Người dùng (Viewer)." });
            }

            // Kiểm tra số tiền hợp lệ
            if (request == null || request.Amount < 10000)
            {
                return Json(new { success = false, message = "Số tiền nạp tối thiểu mỗi lần là 10.000 VNĐ." });
            }

            if (request.Amount > 500000000)
            {
                return Json(new { success = false, message = "Số tiền nạp tối đa mỗi giao dịch là 500.000.000 VNĐ." });
            }

            try
            {
                // 1. Tạo TransactionCode duy nhất cho giao dịch
                var txCode = $"DEP_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

                // 2. Tạo bản ghi WalletTransaction với Status = 0 (Pending)
                var walletTx = new WalletTransaction
                {
                    UserId = user.Id,
                    TransactionCode = txCode,
                    TransactionType = "DEPOSIT",
                    Amount = request.Amount,
                    BalanceBefore = user.WalletBalance,
                    BalanceAfter = user.WalletBalance, // Chưa cộng số dư khi ở trạng thái Chờ (Pending)
                    PaymentMethodName = "Chuyển khoản VietQR",
                    Status = 0, // 0 = Pending (Chờ chuyển khoản)
                    Note = "Chờ quét mã VietQR",
                    CreatedAt = DateTime.UtcNow
                };

                _context.WalletTransactions.Add(walletTx);
                await _context.SaveChangesAsync();

                // 3. Sinh mã nội dung chuyển khoản duy nhất (Memo)
                // Format chuẩn: "NAP " + TransactionId (ví dụ: "NAP 84920")
                var memo = $"NAP {walletTx.Id}";
                walletTx.Note = memo;
                await _context.SaveChangesAsync();

                // 4. Lấy thông tin tài khoản thụ hưởng từ appsettings.json
                var (bankId, accountNo, accountName, bankName) = GetBankConfig();

                // 5. URL encode đúng chuẩn RFC 3986 cho addInfo và accountName
                var encodedMemo = Uri.EscapeDataString(memo);
                var encodedAccountName = Uri.EscapeDataString(accountName);

                // Tạo URL chuẩn img.vietqr.io với template compact2 chuyên nghiệp
                var qrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={(long)walletTx.Amount}&addInfo={encodedMemo}&accountName={encodedAccountName}";

                // 6. Đóng gói dữ liệu trả về cho ViewModel
                var paymentModel = new DepositPaymentViewModel
                {
                    TransactionId = walletTx.Id,
                    TransactionCode = walletTx.TransactionCode,
                    Amount = walletTx.Amount,
                    Memo = memo,
                    BankId = bankId,
                    BankName = bankName,
                    AccountNo = accountNo,
                    AccountName = accountName,
                    QrUrl = qrUrl,
                    CreatedAt = walletTx.CreatedAt,
                    ExpireAt = walletTx.CreatedAt.AddMinutes(10),
                    ExpireSeconds = 600 // 10 phút đếm ngược
                };

                // Trả về JSON cho Client xử lý chuyển Bước 2 mượt mà
                return Json(new
                {
                    success = true,
                    data = paymentModel,
                    message = "Tạo mã QR nạp tiền thành công."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo mã QR nạp tiền cho UserId {UserId}", userId);
                return Json(new
                {
                    success = false,
                    message = "Đã xảy ra lỗi trong quá trình tạo mã QR nạp tiền. Vui lòng thử lại sau."
                });
            }
        }

        /// <summary>
        /// GET: /Wallet/CheckDepositStatus?transactionId=...
        /// Polling AJAX kiểm tra trạng thái giao dịch định kỳ (mỗi 3 giây)
        /// Trả về JSON { isPaid: true/false }
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckDepositStatus(int transactionId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized(new { success = false, isPaid = false, message = "Chưa đăng nhập." });
            }

            var transaction = await _context.WalletTransactions
                .Include(wt => wt.User)
                .FirstOrDefaultAsync(wt => wt.Id == transactionId && wt.UserId == userId);

            if (transaction == null)
            {
                return NotFound(new { success = false, isPaid = false, message = "Không tìm thấy giao dịch nạp tiền." });
            }

            // Kiểm tra xem đơn đã quá hạn 10 phút chưa
            var isExpired = false;
            if (transaction.Status == 0 && DateTime.UtcNow > transaction.CreatedAt.AddMinutes(10))
            {
                isExpired = true;
                transaction.Status = 2; // Failed / Expired
                if (string.IsNullOrEmpty(transaction.Note) || !transaction.Note.Contains("[Hết hạn]"))
                {
                    transaction.Note = (transaction.Note ?? "") + " [Hết hạn thanh toán 10 phút]";
                }
                await _context.SaveChangesAsync();
            }

            var isPaid = transaction.Status == 1;

            return Json(new
            {
                success = true,
                isPaid = isPaid,
                status = transaction.Status,
                isExpired = isExpired,
                amount = transaction.Amount,
                transactionCode = transaction.TransactionCode,
                newBalance = isPaid ? transaction.User?.WalletBalance : null,
                message = isPaid
                    ? "Giao dịch đã được thanh toán thành công!"
                    : (isExpired ? "Giao dịch đã hết hạn thanh toán (quá 10 phút)." : "Đang chờ thanh toán...")
            });
        }

        /// <summary>
        /// POST: /Wallet/SimulateSuccess?transactionId=...
        /// Action hỗ trợ TEST / DEMO: Giả lập thanh toán thành công cho đơn đang Pending
        /// Kích hoạt cộng tiền vào ví và cập nhật Cookie claims để kiểm tra hiệu ứng Polling
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SimulateSuccess(int transactionId)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
            }

            var tx = await _context.WalletTransactions
                .Include(wt => wt.User)
                .FirstOrDefaultAsync(wt => wt.Id == transactionId && wt.UserId == userId);

            if (tx == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy giao dịch." });
            }

            if (tx.Status == 1)
            {
                return Json(new { success = true, isPaid = true, message = "Giao dịch này đã được thanh toán thành công." });
            }

            if (tx.Status == 2)
            {
                return BadRequest(new { success = false, message = "Giao dịch đã bị hết hạn hoặc hủy bỏ." });
            }

            var user = tx.User;
            if (user == null)
            {
                return BadRequest(new { success = false, message = "Không tìm thấy thông tin tài khoản người dùng." });
            }

            // Tiến hành cộng tiền vào ví của người dùng
            tx.BalanceBefore = user.WalletBalance;
            user.WalletBalance += tx.Amount;
            tx.BalanceAfter = user.WalletBalance;
            tx.Status = 1; // 1 = Thành công
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Cập nhật lại Cookie Authentication để Header hiển thị ngay số dư mới
            await RefreshUserClaimsCookieAsync(user);

            return Json(new
            {
                success = true,
                isPaid = true,
                amount = tx.Amount,
                newBalance = user.WalletBalance,
                message = $"Mô phỏng thanh toán thành công! Đã nạp +{tx.Amount:N0} VNĐ vào ví tài khoản."
            });
        }

        // =========================================================================
        // 2. CHỨC NĂNG RÚT TIỀN (WITHDRAW) DÀNH CHO ROLE STREAMER
        // =========================================================================

        [HttpGet]
        public async Task<IActionResult> Withdraw()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            await EnsureWalletTablesExistAsync();

            var user = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // Chỉ cho phép tài khoản Streamer sử dụng trang Rút tiền
            if (!User.IsInRole(UserRoles.Streamer) && user.StreamerProfile == null)
            {
                return RedirectToAction(nameof(Deposit));
            }

            // Đồng bộ số dư của Streamer: nếu chưa có dữ liệu donate thì số dư để về 0
            await SyncStreamerBalanceIfNeededAsync(user);

            var profile = user.StreamerProfile;
            var withdrawalHistory = await _context.WithdrawalRequests
                .Where(wr => wr.UserId == userId)
                .OrderByDescending(wr => wr.CreatedAt)
                .Take(15)
                .ToListAsync();

            var totalWithdrawn = await _context.WithdrawalRequests
                .Where(wr => wr.UserId == userId && (wr.Status == 0 || wr.Status == 1))
                .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;

            var pendingAmount = await _context.WithdrawalRequests
                .Where(wr => wr.UserId == userId && wr.Status == 0)
                .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;

            var model = new WithdrawViewModel
            {
                Amount = user.WalletBalance >= 50000 ? user.WalletBalance : 50000,
                BankName = profile?.BankName ?? "MB Bank (Ngân Hàng Quân Đội)",
                BankAccountNumber = profile?.BankAccountNumber ?? "",
                BankAccountName = profile?.BankAccountName ?? user.FullName.ToUpper(),
                CurrentBalance = user.WalletBalance,
                TotalReceived = profile?.TotalReceived ?? 0m,
                TotalWithdrawn = totalWithdrawn,
                PendingWithdrawalAmount = pendingAmount,
                StreamerDisplayName = profile?.DisplayName ?? user.FullName,
                StreamerSlug = profile?.Slug ?? user.AccountId ?? user.Username,
                WithdrawalHistory = withdrawalHistory
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(WithdrawViewModel model)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            await EnsureWalletTablesExistAsync();

            var userForSync = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (userForSync == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (!User.IsInRole(UserRoles.Streamer) && userForSync.StreamerProfile == null)
            {
                return RedirectToAction(nameof(Deposit));
            }

            // Đồng bộ số dư thực tế trước khi kiểm tra rút tiền
            await SyncStreamerBalanceIfNeededAsync(userForSync);

            // Sử dụng Database Transaction để đảm bảo kiểm tra số dư, trừ tiền và tạo yêu cầu rút đồng bộ tuyệt đối
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _context.Users
                    .Include(u => u.StreamerProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return RedirectToAction("Login", "Auth");
                }

                var profile = user.StreamerProfile;

                // 1. Kiểm tra logic chặt chẽ: Số tiền rút hợp lệ và KHÔNG ĐƯỢC VƯỢT QUÁ SỐ DƯ HIỆN TẠI
                if (model.Amount <= 0)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Số tiền rút phải lớn hơn 0 VNĐ.");
                }
                else if (model.Amount < 50000)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Số tiền rút tối thiểu mỗi lần là 50.000 VNĐ.");
                }

                if (model.Amount > user.WalletBalance)
                {
                    ModelState.AddModelError(
                        nameof(model.Amount),
                        $"Số tiền rút ({model.Amount:N0} VNĐ) không được vượt quá số dư khả dụng hiện tại của bạn ({user.WalletBalance:N0} VNĐ).");
                }

                if (!ModelState.IsValid)
                {
                    await transaction.RollbackAsync();

                    model.CurrentBalance = user.WalletBalance;
                    model.TotalReceived = profile?.TotalReceived ?? 0m;
                    model.StreamerDisplayName = profile?.DisplayName ?? user.FullName;
                    model.StreamerSlug = profile?.Slug ?? user.AccountId ?? user.Username;
                    model.ErrorMessage = ModelState[nameof(model.Amount)]?.Errors.FirstOrDefault()?.ErrorMessage
                        ?? "Thông tin yêu cầu rút tiền không hợp lệ. Vui lòng kiểm tra lại.";
                    model.WithdrawalHistory = await _context.WithdrawalRequests
                        .Where(wr => wr.UserId == userId)
                        .OrderByDescending(wr => wr.CreatedAt)
                        .Take(15)
                        .ToListAsync();
                    model.TotalWithdrawn = await _context.WithdrawalRequests
                        .Where(wr => wr.UserId == userId && (wr.Status == 0 || wr.Status == 1))
                        .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;
                    model.PendingWithdrawalAmount = await _context.WithdrawalRequests
                        .Where(wr => wr.UserId == userId && wr.Status == 0)
                        .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;

                    return View(model);
                }

                // 2. Trừ tiền trực tiếp trong số dư của Streamer và cập nhật đồng bộ
                var balanceBefore = user.WalletBalance;
                user.WalletBalance -= model.Amount;
                var balanceAfter = user.WalletBalance;
                user.UpdatedAt = DateTime.UtcNow;

                // Đồng bộ thông tin ngân hàng của StreamerProfile nếu có thay đổi
                if (profile != null)
                {
                    profile.BankName = model.BankName.Trim();
                    profile.BankAccountNumber = model.BankAccountNumber.Trim();
                    profile.BankAccountName = model.BankAccountName.Trim().ToUpper();
                    profile.UpdatedAt = DateTime.UtcNow;
                }

                var txCode = $"WDR_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

                // 3. Tạo yêu cầu rút tiền (WithdrawalRequest)
                var withdrawalRequest = new WithdrawalRequest
                {
                    UserId = user.Id,
                    StreamerProfileId = profile?.Id,
                    TransactionCode = txCode,
                    Amount = model.Amount,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balanceAfter,
                    BankName = model.BankName.Trim(),
                    BankAccountNumber = model.BankAccountNumber.Trim(),
                    BankAccountName = model.BankAccountName.Trim().ToUpper(),
                    Note = string.IsNullOrWhiteSpace(model.Note)
                        ? $"Yêu cầu rút {model.Amount:N0} VNĐ về {model.BankName.Trim()} - STK {model.BankAccountNumber.Trim()}"
                        : model.Note.Trim(),
                    Status = 0, // 0 = Đang chờ xử lý / Đã tạo yêu cầu rút
                    CreatedAt = DateTime.UtcNow
                };

                // 4. Lưu vào lịch sử biến động số dư (WalletTransactions)
                var walletTx = new WalletTransaction
                {
                    UserId = user.Id,
                    TransactionCode = txCode,
                    TransactionType = "WITHDRAW",
                    Amount = model.Amount,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balanceAfter,
                    PaymentMethodName = $"{model.BankName.Trim()} ({model.BankAccountNumber.Trim()})",
                    Status = 0, // 0 = Đang chờ xử lý
                    Note = withdrawalRequest.Note,
                    CreatedAt = DateTime.UtcNow
                };

                _context.WithdrawalRequests.Add(withdrawalRequest);
                _context.WalletTransactions.Add(walletTx);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 5. Cập nhật đồng bộ Cookie Authentication để số dư trên Header và toàn hệ thống khớp 100%
                await RefreshUserClaimsCookieAsync(user);

                TempData["SuccessMessage"] = $"Tạo yêu cầu rút {model.Amount:N0} VNĐ thành công (Mã YC: {txCode})! Số dư còn lại của bạn: {user.WalletBalance:N0} VNĐ.";
                return RedirectToAction(nameof(Withdraw));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi khi tạo yêu cầu rút tiền cho Streamer UserId {UserId}", userId);
                TempData["ErrorMessage"] = "Đã xảy ra lỗi hệ thống khi tạo yêu cầu rút tiền. Vui lòng thử lại.";
                return RedirectToAction(nameof(Withdraw));
            }
        }

        // =========================================================================
        // HÀM HỖ TRỢ: Đồng bộ lại Claims Cookie & Tạo mã QR nạp tiền
        // =========================================================================

        private async Task RefreshUserClaimsCookieAsync(User user)
        {
            var roles = await _authService.GetUserRolesAsync(user.Id);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName ?? user.Username),
                new Claim("AvatarUrl", user.AvatarUrl ?? "/images/default-avatar.png"),
                new Claim("WalletBalance", user.WalletBalance.ToString("N0")),
                new Claim("AccountId", user.AccountId ?? "")
            };

            foreach (var r in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, r));
            }

            if (user.StreamerProfile != null)
            {
                claims.Add(new Claim("StreamerSlug", user.StreamerProfile.Slug));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        private static string GenerateDepositQrBase64(decimal amount, string transferContent)
        {
            var qrPayload = $"BANK:MB Bank|ACC:888899996666|NAME:CONG TY DONATEWEB VIET NAM|AMOUNT:{amount:0}|CONTENT:{transferContent}";
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(10);
            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
        }
    }
}
