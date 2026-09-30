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

        public WalletController(AppDbContext context, IAuthService authService, ILogger<WalletController> logger)
        {
            _context = context;
            _authService = authService;
            _logger = logger;
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
        // 1. CHỨC NĂNG NẠP TIỀN (DEPOSIT) DÀNH CHO ROLE VIEWER
        // =========================================================================

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

            var defaultAmount = 50000m;
            var transferContent = $"NAPVI {user.Username.ToUpper()}";

            var model = new DepositViewModel
            {
                Amount = defaultAmount,
                PaymentMethod = "Chuyển khoản ngân hàng (VietQR)",
                CurrentBalance = user.WalletBalance,
                Username = user.Username,
                FullName = user.FullName,
                TransferContent = transferContent,
                GeneratedQrBase64 = GenerateDepositQrBase64(defaultAmount, transferContent),
                RecentTransactions = await _context.WalletTransactions
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT")
                    .OrderByDescending(wt => wt.CreatedAt)
                    .Take(10)
                    .ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deposit(DepositViewModel model)
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

            if (User.IsInRole(UserRoles.Streamer) || user.StreamerProfile != null)
            {
                TempData["ErrorMessage"] = "Chức năng Nạp tiền chỉ dành cho tài khoản Viewer.";
                return RedirectToAction(nameof(Withdraw));
            }

            var transferContent = $"NAPVI {user.Username.ToUpper()}";
            model.Username = user.Username;
            model.FullName = user.FullName;
            model.CurrentBalance = user.WalletBalance;
            model.TransferContent = transferContent;
            model.GeneratedQrBase64 = GenerateDepositQrBase64(model.Amount > 0 ? model.Amount : 50000, transferContent);

            // Kiểm tra logic số tiền nạp
            if (model.Amount <= 0)
            {
                ModelState.AddModelError(nameof(model.Amount), "Số tiền nạp phải lớn hơn 0 VNĐ.");
            }
            else if (model.Amount < 10000)
            {
                ModelState.AddModelError(nameof(model.Amount), "Số tiền nạp tối thiểu mỗi lần là 10.000 VNĐ.");
            }
            else if (model.Amount > 500000000)
            {
                ModelState.AddModelError(nameof(model.Amount), "Số tiền nạp tối đa mỗi giao dịch là 500.000.000 VNĐ.");
            }

            if (!ModelState.IsValid)
            {
                model.ErrorMessage = ModelState[nameof(model.Amount)]?.Errors.FirstOrDefault()?.ErrorMessage
                    ?? "Vui lòng kiểm tra lại thông tin số tiền nạp.";
                model.RecentTransactions = await _context.WalletTransactions
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT")
                    .OrderByDescending(wt => wt.CreatedAt)
                    .Take(10)
                    .ToListAsync();
                return View(model);
            }

            // Sử dụng Database Transaction để đảm bảo cộng tiền vào số dư và lưu lịch sử đồng bộ, an toàn
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var balanceBefore = user.WalletBalance;
                user.WalletBalance += model.Amount;
                var balanceAfter = user.WalletBalance;
                user.UpdatedAt = DateTime.UtcNow;

                var txCode = $"DEP_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

                var walletTx = new WalletTransaction
                {
                    UserId = user.Id,
                    TransactionCode = txCode,
                    TransactionType = "DEPOSIT",
                    Amount = model.Amount,
                    BalanceBefore = balanceBefore,
                    BalanceAfter = balanceAfter,
                    PaymentMethodName = string.IsNullOrWhiteSpace(model.PaymentMethod) ? "Chuyển khoản ngân hàng (VietQR)" : model.PaymentMethod.Trim(),
                    Status = 1, // 1 = Thành công
                    Note = string.IsNullOrWhiteSpace(model.Note)
                        ? $"Nạp {model.Amount:N0} VNĐ vào ví qua {model.PaymentMethod}"
                        : model.Note.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.WalletTransactions.Add(walletTx);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Đồng bộ lại số dư trong Authentication Cookie để Header và toàn hệ thống cập nhật tức thì
                await RefreshUserClaimsCookieAsync(user);

                TempData["SuccessMessage"] = $"Nạp thành công +{model.Amount:N0} VNĐ vào ví! Số dư hiện tại của bạn: {user.WalletBalance:N0} VNĐ (Mã GD: {txCode}).";
                return RedirectToAction(nameof(Deposit));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi khi nạp tiền cho User ID {UserId}", userId);
                model.ErrorMessage = "Đã xảy ra lỗi trong quá trình xử lý nạp tiền. Vui lòng thử lại.";
                model.RecentTransactions = await _context.WalletTransactions
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT")
                    .OrderByDescending(wt => wt.CreatedAt)
                    .Take(10)
                    .ToListAsync();
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult GenerateDepositQrAjax(decimal amount)
        {
            var username = User.Identity?.Name ?? "VIEWER";
            var safeAmount = amount >= 10000 ? amount : 10000;
            var transferContent = $"NAPVI {username.ToUpper()}";
            var qrBase64 = GenerateDepositQrBase64(safeAmount, transferContent);

            return Json(new
            {
                success = true,
                qrBase64,
                formattedAmount = safeAmount.ToString("N0") + " VNĐ",
                transferContent
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
