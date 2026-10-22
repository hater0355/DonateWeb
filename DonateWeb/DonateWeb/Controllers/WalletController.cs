using System.Security.Claims;
using System.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using QRCoder;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;
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
        private readonly IPayOSService _payOS;
        private readonly IWebHostEnvironment _environment;

        public WalletController(
            AppDbContext context,
            IAuthService authService,
            ILogger<WalletController> logger,
            IConfiguration configuration,
            IPayOSService payOS,
            IWebHostEnvironment environment)
        {
            _context = context;
            _authService = authService;
            _logger = logger;
            _configuration = configuration;
            _payOS = payOS;
            _environment = environment;
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
                        OrderCode BIGINT NULL,
                        PaymentQrCode NVARCHAR(2000) NULL,
                        PaymentCheckoutUrl NVARCHAR(1000) NULL,
                        PaymentBankId NVARCHAR(20) NULL,
                        PaymentAccountNo NVARCHAR(50) NULL,
                        PaymentAccountName NVARCHAR(100) NULL,
                        FailedAt DATETIME2 NULL,
                        CompletedAt DATETIME2 NULL,
                        [Status] INT NOT NULL DEFAULT 1,
                        Note NVARCHAR(500) NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_WalletTransactions PRIMARY KEY CLUSTERED (Id),
                        CONSTRAINT FK_WalletTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
                    );
                    CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_TransactionCode ON dbo.WalletTransactions (TransactionCode);
                    CREATE NONCLUSTERED INDEX IX_WalletTransactions_UserId_CreatedAt ON dbo.WalletTransactions (UserId, CreatedAt);
                END

                IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WalletTransactions') AND name = 'OrderCode')
                    BEGIN
                        ALTER TABLE dbo.WalletTransactions ADD OrderCode BIGINT NULL;
                    END
                    IF COL_LENGTH('dbo.WalletTransactions', 'PaymentQrCode') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD PaymentQrCode NVARCHAR(2000) NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'PaymentCheckoutUrl') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD PaymentCheckoutUrl NVARCHAR(1000) NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'PaymentBankId') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD PaymentBankId NVARCHAR(20) NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'PaymentAccountNo') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD PaymentAccountNo NVARCHAR(50) NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'PaymentAccountName') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD PaymentAccountName NVARCHAR(100) NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'FailedAt') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD FailedAt DATETIME2 NULL;
                    IF COL_LENGTH('dbo.WalletTransactions', 'CompletedAt') IS NULL
                        ALTER TABLE dbo.WalletTransactions ADD CompletedAt DATETIME2 NULL;
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WalletTransactions_OrderCode')
                    BEGIN
                        EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_OrderCode ON dbo.WalletTransactions (OrderCode) WHERE [OrderCode] IS NOT NULL;');
                    END
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
            if (user.StreamerProfile?.ApprovalStatus == StreamerApprovalStatus.Approved)
            {
                return RedirectToAction(nameof(Withdraw));
            }

            var (bankId, accountNo, accountName, bankName) = GetBankConfig();
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-10);
            await _context.WalletTransactions
                .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                    && wt.Status == WalletTransaction.StatusPending && wt.CreatedAt <= cutoff)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(wt => wt.Status, WalletTransaction.StatusFailed)
                    .SetProperty(wt => wt.FailedAt, now));

            var pendingDeposit = await _context.WalletTransactions
                .AsNoTracking()
                .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                    && wt.Status == WalletTransaction.StatusPending && wt.CreatedAt > cutoff)
                .OrderByDescending(wt => wt.CreatedAt)
                .FirstOrDefaultAsync();
            ViewBag.DepositBlockedUntil = await GetDepositBlockedUntilAsync(userId, now);
            ViewBag.EnablePaymentSimulation = _environment.IsDevelopment();

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
                PaymentInfo = pendingDeposit == null || string.IsNullOrWhiteSpace(pendingDeposit.PaymentQrCode)
                    ? null
                    : BuildDepositPaymentInfo(pendingDeposit),
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
        /// Tạo đơn nạp tiền tự động qua PayOS SDK, sinh orderCode timestamp mili-giây,
        /// lưu WalletTransaction trạng thái Pending, gọi _payOS.createPaymentLink
        /// và trả về JSON { success = true, orderCode, checkoutUrl, qrCode, ... }
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [DonateRateLimit(5, 60, "wallet_deposit_create")]
        public async Task<IActionResult> CreateDeposit([FromForm] DepositRequestViewModel? request)
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

            if (user.StreamerProfile?.ApprovalStatus == StreamerApprovalStatus.Approved)
            {
                return Json(new { success = false, message = "Chức năng Nạp tiền chỉ dành cho tài khoản Người dùng (Viewer)." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Số tiền nạp không hợp lệ." });
            }

            decimal depositAmount = request?.Amount ?? 0;

            // Validate số tiền tối thiểu (10.000 VNĐ)
            if (depositAmount < 10000)
            {
                return Json(new { success = false, message = "Số tiền nạp tối thiểu mỗi lần là 10.000 VNĐ." });
            }

            if (depositAmount > 500000000)
            {
                return Json(new { success = false, message = "Số tiền nạp tối đa mỗi giao dịch là 500.000.000 VNĐ." });
            }

            if (decimal.Truncate(depositAmount) != depositAmount)
            {
                return Json(new { success = false, message = "Số tiền nạp phải là số nguyên VNĐ." });
            }

            WalletTransaction? walletTx = null;
            try
            {
                await using var depositTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var lockResource = $"WalletDepositCreate:{userId}";
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
                    DECLARE @lockResult INT;
                    EXEC @lockResult = sys.sp_getapplock
                        @Resource = {lockResource},
                        @LockMode = 'Exclusive',
                        @LockOwner = 'Transaction',
                        @LockTimeout = 10000;
                    IF @lockResult < 0
                        THROW 51000, 'Could not acquire wallet deposit creation lock.', 1;");

                var now = DateTime.UtcNow;
                var cutoff = now.AddMinutes(-10);
                await _context.WalletTransactions
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                        && wt.Status == WalletTransaction.StatusPending && wt.CreatedAt <= cutoff)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(wt => wt.Status, WalletTransaction.StatusFailed)
                        .SetProperty(wt => wt.FailedAt, now));

                var existingPending = await _context.WalletTransactions
                    .AsNoTracking()
                    .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                        && wt.Status == WalletTransaction.StatusPending && wt.CreatedAt > cutoff)
                    .OrderByDescending(wt => wt.CreatedAt)
                    .FirstOrDefaultAsync();

                if (existingPending != null)
                {
                    await depositTransaction.CommitAsync();
                    if (string.IsNullOrWhiteSpace(existingPending.PaymentQrCode))
                    {
                        return Json(new
                        {
                            success = false,
                            pending = true,
                            message = "Mã nạp tiền đang được tạo. Vui lòng đợi vài giây rồi thử mở lại trang nạp tiền."
                        });
                    }

                    var existingPayment = BuildDepositPaymentInfo(existingPending);
                    return Json(new
                    {
                        success = true,
                        orderCode = existingPending.OrderCode,
                        checkoutUrl = existingPending.PaymentCheckoutUrl,
                        qrCode = existingPending.PaymentQrCode,
                        data = existingPayment,
                        amount = existingPending.Amount,
                        description = existingPayment.Memo,
                        message = "Đang tiếp tục mã QR chưa hết hạn của bạn."
                    });
                }

                var blockedUntil = await GetDepositBlockedUntilAsync(userId, now);
                if (blockedUntil.HasValue && blockedUntil.Value > now)
                {
                    await depositTransaction.CommitAsync();
                    var remainingSeconds = Math.Max(1, (int)Math.Ceiling((blockedUntil.Value - now).TotalSeconds));
                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        success = false,
                        blockedUntil = blockedUntil.Value,
                        remainingSeconds,
                        message = $"Bạn đã có 3 lần tạo mã không thanh toán thành công. Vui lòng thử lại sau {Math.Ceiling(remainingSeconds / 60d):0} phút."
                    });
                }

                // Sinh orderCode bằng timestamp mili-giây (yêu cầu của payOS: số nguyên long)
                long orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var txCode = $"DEP_{orderCode}";

                // Tạo bản ghi WalletTransaction trạng thái Pending (0) lưu vào DB
                walletTx = new WalletTransaction
                {
                    UserId = user.Id,
                    OrderCode = orderCode,
                    TransactionCode = txCode,
                    TransactionType = "DEPOSIT",
                    Amount = depositAmount,
                    BalanceBefore = user.WalletBalance,
                    BalanceAfter = user.WalletBalance,
                    PaymentMethodName = "PayOS VietQR",
                    Status = WalletTransaction.StatusPending,
                    Note = $"NAP {orderCode}",
                    CreatedAt = DateTime.UtcNow
                };

                _context.WalletTransactions.Add(walletTx);
                await _context.SaveChangesAsync();
                await depositTransaction.CommitAsync();

                // Cấu hình URL quay lại
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var cancelUrl = $"{baseUrl}/Wallet/Deposit";
                var returnUrl = $"{baseUrl}/Wallet/DepositSuccess?orderCode={orderCode}";

                var rawDescription = $"NAP {orderCode}";
                var description = rawDescription.Length > 25 ? rawDescription.Substring(0, 25) : rawDescription;

                var paymentData = new PaymentData
                {
                    orderCode = orderCode,
                    amount = (int)depositAmount,
                    description = description,
                    cancelUrl = cancelUrl,
                    returnUrl = returnUrl,
                    buyerName = user.FullName ?? user.Username,
                    buyerEmail = user.Email,
                    expiredAt = (int)DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()
                };

                // Gọi PayOS SDK tạo Payment Link
                var result = await _payOS.createPaymentLink(paymentData);
                var (bankId, accountNo, accountName, bankName) = GetBankConfig();
                if (!string.IsNullOrEmpty(result.accountNumber)) accountNo = result.accountNumber;
                if (!string.IsNullOrEmpty(result.accountName)) accountName = result.accountName;
                var fallbackQrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={(long)depositAmount}&addInfo={Uri.EscapeDataString(description)}&accountName={Uri.EscapeDataString(accountName)}";
                walletTx.PaymentQrCode = !string.IsNullOrWhiteSpace(result.qrCode) ? result.qrCode : fallbackQrUrl;
                walletTx.PaymentCheckoutUrl = result.checkoutUrl;
                walletTx.PaymentBankId = result.bin;
                walletTx.PaymentAccountNo = result.accountNumber;
                walletTx.PaymentAccountName = result.accountName;
                await _context.SaveChangesAsync();

                var paymentModel = new DepositPaymentViewModel
                {
                    TransactionId = walletTx.Id,
                    OrderCode = orderCode,
                    TransactionCode = walletTx.TransactionCode,
                    Amount = walletTx.Amount,
                    Memo = description,
                    BankId = !string.IsNullOrEmpty(result.bin) ? result.bin : bankId,
                    BankName = bankName,
                    AccountNo = accountNo,
                    AccountName = accountName,
                    QrUrl = !string.IsNullOrEmpty(result.qrCode) ? result.qrCode : fallbackQrUrl,
                    QrCode = result.qrCode,
                    CheckoutUrl = result.checkoutUrl,
                    CreatedAt = walletTx.CreatedAt,
                    ExpireAt = walletTx.CreatedAt.AddMinutes(10),
                    ExpireSeconds = 600
                };

                // Trả về kết quả JSON gồm: { success = true, orderCode, checkoutUrl, qrCode, ... }
                return Json(new
                {
                    success = true,
                    orderCode = orderCode,
                    checkoutUrl = result.checkoutUrl,
                    qrCode = result.qrCode,
                    data = paymentModel,
                    amount = depositAmount,
                    description = description,
                    message = "Tạo mã QR nạp tiền qua PayOS thành công."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gọi PayOS createPaymentLink cho UserId {UserId}", userId);
                if (walletTx != null)
                {
                    walletTx.Status = WalletTransaction.StatusFailed;
                    walletTx.FailedAt = DateTime.UtcNow;
                    try
                    {
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception updateException)
                    {
                        _logger.LogError(updateException, "Không thể đánh dấu giao dịch nạp {TransactionId} thất bại sau lỗi tạo link PayOS.", walletTx.Id);
                    }
                }
                return Json(new
                {
                    success = false,
                    message = "Đã xảy ra lỗi trong quá trình tạo đơn thanh toán PayOS. Vui lòng thử lại sau."
                });
            }
        }

        private DepositPaymentViewModel BuildDepositPaymentInfo(WalletTransaction transaction)
        {
            var (configuredBankId, configuredAccountNo, configuredAccountName, bankName) = GetBankConfig();
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((transaction.CreatedAt.AddMinutes(10) - DateTime.UtcNow).TotalSeconds));
            var memo = $"NAP {transaction.OrderCode}";
            return new DepositPaymentViewModel
            {
                TransactionId = transaction.Id,
                OrderCode = transaction.OrderCode ?? 0,
                TransactionCode = transaction.TransactionCode,
                Amount = transaction.Amount,
                Memo = memo,
                BankId = string.IsNullOrWhiteSpace(transaction.PaymentBankId) ? configuredBankId : transaction.PaymentBankId,
                BankName = bankName,
                AccountNo = string.IsNullOrWhiteSpace(transaction.PaymentAccountNo) ? configuredAccountNo : transaction.PaymentAccountNo,
                AccountName = string.IsNullOrWhiteSpace(transaction.PaymentAccountName) ? configuredAccountName : transaction.PaymentAccountName,
                QrUrl = transaction.PaymentQrCode ?? string.Empty,
                QrCode = transaction.PaymentQrCode ?? string.Empty,
                CheckoutUrl = transaction.PaymentCheckoutUrl ?? string.Empty,
                CreatedAt = transaction.CreatedAt,
                ExpireAt = transaction.CreatedAt.AddMinutes(10),
                ExpireSeconds = remainingSeconds
            };
        }

        private async Task<DateTime?> GetDepositBlockedUntilAsync(int userId, DateTime now)
        {
            var latestSuccessAt = await _context.WalletTransactions
                .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                    && wt.Status == WalletTransaction.StatusCompleted)
                .MaxAsync(wt => (DateTime?)(wt.CompletedAt ?? wt.CreatedAt));

            var failures = await _context.WalletTransactions
                .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                    && wt.Status == WalletTransaction.StatusFailed
                    && wt.FailedAt.HasValue
                    && (!latestSuccessAt.HasValue || wt.FailedAt.Value > latestSuccessAt.Value))
                .OrderBy(wt => wt.FailedAt)
                .Select(wt => wt.FailedAt!.Value)
                .ToListAsync();

            var consecutiveFailures = 0;
            DateTime? blockedUntil = null;
            foreach (var failedAt in failures)
            {
                if (blockedUntil.HasValue)
                {
                    if (failedAt < blockedUntil.Value)
                    {
                        continue;
                    }

                    blockedUntil = null;
                    consecutiveFailures = 0;
                }

                consecutiveFailures++;
                if (consecutiveFailures == 3)
                {
                    blockedUntil = failedAt.AddMinutes(10);
                    consecutiveFailures = 0;
                }
            }

            return blockedUntil > now ? blockedUntil : null;
        }

        /// <summary>
        /// GET: /Wallet/CheckStatus?orderCode=...
        /// Action polling kiểm tra trạng thái đơn hàng PayOS theo orderCode
        /// Trả về JSON { isPaid = true/false }
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CheckStatus(long orderCode)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized();
            }

            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            await _context.WalletTransactions
                .Where(wt => wt.OrderCode == orderCode && wt.UserId == userId
                    && wt.TransactionType == "DEPOSIT"
                    && wt.Status == WalletTransaction.StatusPending
                    && wt.CreatedAt <= cutoff)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(wt => wt.Status, WalletTransaction.StatusFailed)
                    .SetProperty(wt => wt.FailedAt, DateTime.UtcNow));

            var tx = await _context.WalletTransactions
                .Include(wt => wt.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(wt => wt.OrderCode == orderCode && wt.UserId == userId);

            if (tx == null)
            {
                return NotFound(new { success = false, isPaid = false, message = "Không tìm thấy giao dịch với mã này." });
            }

            var isPaid = tx.Status == WalletTransaction.StatusCompleted;
            return Json(new
            {
                success = true,
                isPaid = isPaid,
                status = tx.Status,
                orderCode = tx.OrderCode,
                amount = tx.Amount,
                newBalance = tx.User?.WalletBalance
            });
        }

        /// <summary>
        /// GET: /Wallet/CheckDepositStatus?transactionId=...
        /// Hỗ trợ cả transactionId và orderCode để tương thích ngược
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CheckDepositStatus([FromQuery] long? transactionId, [FromQuery] long? orderCode)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized();
            }

            var code = orderCode ?? transactionId;
            if (!code.HasValue)
            {
                return BadRequest(new { success = false, isPaid = false, message = "Thiếu orderCode hoặc transactionId." });
            }

            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            await _context.WalletTransactions
                .Where(wt => wt.UserId == userId && wt.TransactionType == "DEPOSIT"
                    && (wt.OrderCode == code.Value || (code.Value <= int.MaxValue && wt.Id == (int)code.Value))
                    && wt.Status == WalletTransaction.StatusPending
                    && wt.CreatedAt <= cutoff)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(wt => wt.Status, WalletTransaction.StatusFailed)
                    .SetProperty(wt => wt.FailedAt, DateTime.UtcNow));

            var tx = await _context.WalletTransactions
                .Include(wt => wt.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(wt => wt.UserId == userId &&
                    (wt.OrderCode == code.Value || (code.Value <= int.MaxValue && wt.Id == (int)code.Value)));

            if (tx == null)
            {
                return NotFound(new { success = false, isPaid = false, message = "Không tìm thấy giao dịch." });
            }

            var isPaid = tx.Status == WalletTransaction.StatusCompleted;
            return Json(new
            {
                success = true,
                isPaid = isPaid,
                status = tx.Status,
                orderCode = tx.OrderCode,
                amount = tx.Amount,
                transactionCode = tx.TransactionCode,
                newBalance = isPaid ? tx.User?.WalletBalance : null,
                message = isPaid
                    ? "Giao dịch đã được thanh toán thành công!"
                    : tx.Status == WalletTransaction.StatusFailed
                        ? "Mã thanh toán đã hết hạn hoặc bị hủy. Vui lòng tạo mã mới nếu bạn vẫn muốn nạp tiền."
                        : "Đang chờ thanh toán..."
            });
        }

        /// <summary>
        /// GET: /Wallet/DepositSuccess?orderCode=...
        /// Trang hiển thị thông báo nạp tiền thành công sau khi chuyển hướng từ cổng PayOS
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DepositSuccess(long? orderCode)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            WalletTransaction? tx = null;
            if (orderCode.HasValue)
            {
                tx = await _context.WalletTransactions
                    .FirstOrDefaultAsync(wt => wt.OrderCode == orderCode.Value && wt.UserId == userId);
            }

            ViewBag.OrderCode = orderCode;
            ViewBag.Transaction = tx;
            ViewBag.WalletBalance = user.WalletBalance;

            return View(user);
        }

        /// <summary>
        /// POST: /Wallet/SimulateSuccess?orderCode=...
        /// Action hỗ trợ TEST / DEMO: Giả lập thanh toán thành công cho đơn đang Pending
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SimulateSuccess([FromQuery] long? orderCode, [FromQuery] int? transactionId)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized(new { success = false, message = "Chưa đăng nhập." });
            }

            var tx = await _context.WalletTransactions
                .Include(wt => wt.User)
                .FirstOrDefaultAsync(wt => wt.TransactionType == "DEPOSIT" &&
                    ((orderCode.HasValue && wt.OrderCode == orderCode.Value && wt.UserId == userId)
                    || (transactionId.HasValue && wt.Id == transactionId.Value && wt.UserId == userId)));

            if (tx == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy giao dịch." });
            }

            if (tx.Status == 1)
            {
                return Json(new { success = true, isPaid = true, message = "Giao dịch này đã được thanh toán thành công từ trước.", newBalance = tx.User?.WalletBalance, amount = tx.Amount, orderCode = tx.OrderCode });
            }

            if (tx.Status != WalletTransaction.StatusPending)
            {
                return Conflict(new { success = false, message = "Giao dịch không còn ở trạng thái chờ thanh toán." });
            }

            var user = tx.User;
            if (user == null)
            {
                return BadRequest(new { success = false, message = "Không tìm thấy thông tin tài khoản người dùng." });
            }

            tx.BalanceBefore = user.WalletBalance;
            user.WalletBalance += tx.Amount;
            tx.BalanceAfter = user.WalletBalance;
            tx.Status = 1;
            tx.CompletedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await RefreshUserClaimsCookieAsync(user);

            return Json(new
            {
                success = true,
                isPaid = true,
                orderCode = tx.OrderCode,
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

            // Chỉ streamer đã được duyệt mới xem và thao tác với doanh thu.
            if (user.StreamerProfile?.ApprovalStatus != StreamerApprovalStatus.Approved)
            {
                return Forbid();
            }

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
                Amount = user.StreamerEarningsBalance >= 50000 ? user.StreamerEarningsBalance : 50000,
                BankName = profile?.BankName ?? "MB Bank (Ngân Hàng Quân Đội)",
                BankAccountNumber = profile?.BankAccountNumber ?? "",
                BankAccountName = profile?.BankAccountName ?? user.FullName.ToUpper(),
                CurrentBalance = user.StreamerEarningsBalance,
                SpendingBalance = user.WalletBalance,
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

            if (userForSync.StreamerProfile?.ApprovalStatus != StreamerApprovalStatus.Approved)
            {
                return RedirectToAction(nameof(Deposit));
            }

            // Sử dụng Database Transaction để đảm bảo kiểm tra số dư, trừ tiền và tạo yêu cầu rút đồng bộ tuyệt đối
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

                if (profile?.ApprovalStatus != StreamerApprovalStatus.Approved)
                {
                    await transaction.RollbackAsync();
                    return Forbid();
                }

                // 1. Kiểm tra logic chặt chẽ: Số tiền rút hợp lệ và KHÔNG ĐƯỢC VƯỢT QUÁ SỐ DƯ HIỆN TẠI
                if (model.Amount <= 0)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Số tiền rút phải lớn hơn 0 VNĐ.");
                }
                else if (model.Amount < 50000)
                {
                    ModelState.AddModelError(nameof(model.Amount), "Số tiền rút tối thiểu mỗi lần là 50.000 VNĐ.");
                }

                if (model.Amount > user.StreamerEarningsBalance)
                {
                    ModelState.AddModelError(
                        nameof(model.Amount),
                        $"Số tiền rút ({model.Amount:N0} VNĐ) không được vượt quá doanh thu khả dụng ({user.StreamerEarningsBalance:N0} VNĐ).");
                }

                if (!ModelState.IsValid)
                {
                    await transaction.RollbackAsync();

                    model.CurrentBalance = user.StreamerEarningsBalance;
                    model.SpendingBalance = user.WalletBalance;
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

                // 2. Trừ tiền ở tài khoản doanh thu; ví dùng để donate không bị ảnh hưởng.
                var balanceBefore = user.StreamerEarningsBalance;
                user.StreamerEarningsBalance -= model.Amount;
                var balanceAfter = user.StreamerEarningsBalance;
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

                TempData["SuccessMessage"] = $"Tạo yêu cầu rút {model.Amount:N0} VNĐ thành công (Mã YC: {txCode})! Doanh thu còn lại: {user.StreamerEarningsBalance:N0} VNĐ.";
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

        /// <summary>
        /// Chuyển một phần doanh thu streamer sang ví tiêu dùng để donate hoặc mua hàng.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferEarningsToWallet(decimal amount)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            if (amount <= 0 || amount > 500000000m)
            {
                TempData["ErrorMessage"] = "Nhập số tiền chuyển từ 1 đến 500.000.000 VNĐ.";
                return RedirectToAction(nameof(Withdraw));
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var user = await _context.Users
                    .Include(u => u.StreamerProfile)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user?.StreamerProfile?.ApprovalStatus != StreamerApprovalStatus.Approved)
                {
                    await transaction.RollbackAsync();
                    return Forbid();
                }

                if (amount > user.StreamerEarningsBalance)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = $"Doanh thu khả dụng chỉ còn {user.StreamerEarningsBalance:N0} VNĐ.";
                    return RedirectToAction(nameof(Withdraw));
                }

                var earningsBefore = user.StreamerEarningsBalance;
                var spendingBefore = user.WalletBalance;
                user.StreamerEarningsBalance -= amount;
                user.WalletBalance += amount;
                user.UpdatedAt = DateTime.UtcNow;

                var transferCode = $"TRF_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
                _context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = user.Id,
                    TransactionCode = transferCode,
                    TransactionType = "EARNINGS_TRANSFER",
                    Amount = amount,
                    BalanceBefore = spendingBefore,
                    BalanceAfter = user.WalletBalance,
                    PaymentMethodName = "Doanh thu streamer",
                    Status = WalletTransaction.StatusCompleted,
                    Note = $"Chuyển {amount:N0} VNĐ từ doanh thu streamer sang ví donate. Doanh thu: {earningsBefore:N0} → {user.StreamerEarningsBalance:N0} VNĐ.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await RefreshUserClaimsCookieAsync(user);

                TempData["SuccessMessage"] = $"Đã chuyển {amount:N0} VNĐ sang ví dùng để donate.";
                return RedirectToAction(nameof(Withdraw));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi chuyển doanh thu sang ví tiêu dùng cho UserId {UserId}", userId);
                TempData["ErrorMessage"] = "Không thể chuyển tiền lúc này. Vui lòng thử lại.";
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

            if (user.StreamerProfile?.ApprovalStatus == StreamerApprovalStatus.Approved)
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
