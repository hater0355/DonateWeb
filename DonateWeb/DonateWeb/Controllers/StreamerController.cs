using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder; // [MỚI THÊM] Thư viện QRCoder để sinh mã QR thanh toán chứa số tiền và nội dung chuyển khoản
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Security.Uploads;
using DonateWeb.Services;
using DonateWeb.ViewModels.Streamer;

namespace DonateWeb.Controllers
{
    public class StreamerController : Controller
    {
        private readonly IStreamerService _streamerService;
        private readonly IAuthService _authService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IQrCodeService _qrCodeService;
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StreamerController> _logger;

        public StreamerController(
            IStreamerService streamerService,
            IAuthService authService,
            IWebHostEnvironment webHostEnvironment,
            IQrCodeService qrCodeService,
            AppDbContext context,
            IConfiguration configuration,
            ILogger<StreamerController> logger)
        {
            _streamerService = streamerService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
            _qrCodeService = qrCodeService;
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Lưu file ảnh đại diện được tải lên từ máy tính vào thư mục vật lý wwwroot/images/avatars
        /// và trả về đường dẫn lưu vào Database (/images/avatars/ten-file)
        /// </summary>
        private async Task<string?> SaveAvatarFileAsync(IFormFile? avatarFile, int userId)
        {
            var (path, error) = await AvatarUploadService.SaveAsync(avatarFile, _webHostEnvironment.WebRootPath, userId, HttpContext.RequestAborted);
            if (error != null)
            {
                ModelState.AddModelError(nameof(avatarFile), error);
                return null;
            }

            return path;
        }

        /// <summary>
        /// Lưu file ảnh bìa được tải lên từ máy tính vào thư mục vật lý wwwroot/images/banners
        /// và trả về đường dẫn lưu vào Database (/images/banners/ten-file)
        /// </summary>
        private async Task<string?> SaveBannerFileAsync(IFormFile? bannerFile, int userId)
        {
            var (path, error) = await BannerUploadService.SaveAsync(bannerFile, _webHostEnvironment.WebRootPath, userId, HttpContext.RequestAborted);
            if (error != null)
            {
                ModelState.AddModelError(nameof(bannerFile), error);
                return null;
            }

            return path;
        }

        private async Task RefreshUserCookieClaimsAsync(int userId)
        {
            var updatedUser = await _authService.GetUserByIdAsync(userId);
            if (updatedUser == null) return;

            var roles = await _authService.GetUserRolesAsync(updatedUser.Id);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, updatedUser.Id.ToString()),
                new Claim(ClaimTypes.Name, updatedUser.Username),
                new Claim(ClaimTypes.Email, updatedUser.Email),
                new Claim("FullName", updatedUser.FullName ?? updatedUser.Username),
                new Claim("AvatarUrl", updatedUser.AvatarUrl ?? "/images/default-avatar.png"),
                new Claim("WalletBalance", updatedUser.WalletBalance.ToString("N0")),
                new Claim("AccountId", updatedUser.AccountId ?? "")
            };
            foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
            if (updatedUser.StreamerProfile != null) claims.Add(new Claim("StreamerSlug", updatedUser.StreamerProfile.Slug));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        /// <summary>
        /// Tạo mã QR Code dạng chuỗi Data URI Base64 PNG từ URL trang donate hoặc bất kỳ văn bản nào
        /// </summary>
        /// <param name="url">Đường dẫn URL trang donate (ví dụ: https://localhost:7111/tenstreamer)</param>
        /// <returns>Chuỗi Data URI Base64 dạng data:image/png;base64,... để gắn trực tiếp vào thẻ img</returns>
        public string GenerateQrCodeBase64(string url)
        {
            return _qrCodeService.GenerateQrCodeBase64(url, 10);
        }

        // =========================================================================
        // [MỚI THÊM] Hàm hỗ trợ sinh ảnh mã QR (Base64 PNG) chứa thông tin:
        // Ngân hàng, Số tài khoản, Chủ tài khoản, Số tiền (Amount) và Nội dung chuyển khoản
        // =========================================================================
        private string GeneratePaymentQrBase64(string? bankName, string? bankAccountNo, string? bankAccountName, decimal amount, string transferContent)
        {
            var bank = string.IsNullOrWhiteSpace(bankName) ? "MB Bank" : bankName.Trim();
            var accNo = string.IsNullOrWhiteSpace(bankAccountNo) ? "0987654321" : bankAccountNo.Trim();
            var accName = string.IsNullOrWhiteSpace(bankAccountName) ? "STREAMER DONATE" : bankAccountName.Trim().ToUpper();

            // Chuỗi payload thanh toán chứa đầy đủ thông tin Ngân hàng, Số tiền và Nội dung chuyển khoản
            var qrPayload = $"BANK:{bank}|ACC:{accNo}|NAME:{accName}|AMOUNT:{amount:0}|CONTENT:{transferContent}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(10);

            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
        }

        // =========================================================================
        // [MỚI THÊM] API Endpoint trả về mã QR động ngay khi Viewer thay đổi Số tiền,
        // Tên người gửi hoặc Lời nhắn trên giao diện (AJAX)
        // =========================================================================
        [HttpGet]
        public async Task<IActionResult> GenerateQrAjax(string slug, decimal amount, string? donorName, string? message)
        {
            var streamer = await _streamerService.GetBySlugAsync(slug);
            if (streamer == null)
            {
                return NotFound();
            }

            var streamerIdentifier = !string.IsNullOrWhiteSpace(streamer.User?.AccountId) ? streamer.User.AccountId : streamer.Slug;
            var cleanDonor = string.IsNullOrWhiteSpace(donorName) ? "Viewer" : donorName.Trim();
            var transferContent = string.IsNullOrWhiteSpace(message)
                ? $"DONATE {streamerIdentifier} {cleanDonor}"
                : $"DONATE {streamerIdentifier} {cleanDonor} - {message.Trim()}";

            var qrBase64 = GeneratePaymentQrBase64(
                streamer.BankName,
                streamer.BankAccountNumber,
                streamer.BankAccountName ?? streamer.DisplayName,
                amount,
                transferContent);

            return Json(new
            {
                success = true,
                qrBase64,
                transferContent,
                formattedAmount = amount.ToString("N0") + " VNĐ"
            });
        }

        /// <summary>
        /// Trang nhận donate / Trang cá nhân theo slug (ví dụ: /tenstreamer hoặc /streamer/donate?slug=tenstreamer)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Donate(string? slug, string? id)
        {
            string? targetIdentifier = !string.IsNullOrWhiteSpace(slug) ? slug : id;
            if (string.IsNullOrWhiteSpace(targetIdentifier))
            {
                var currentStreamerSlug = User.FindFirst("StreamerSlug")?.Value;
                var currentAccountId = User.FindFirst("AccountId")?.Value;
                targetIdentifier = !string.IsNullOrWhiteSpace(currentStreamerSlug)
                    ? currentStreamerSlug
                    : (!string.IsNullOrWhiteSpace(currentAccountId) ? currentAccountId : null);
            }

            StreamerProfile? streamer = null;
            if (!string.IsNullOrWhiteSpace(targetIdentifier))
            {
                streamer = await _streamerService.GetBySlugAsync(targetIdentifier.Trim());
            }

            if (streamer == null)
            {
                // Fallback nếu không truyền tham số: lấy streamer đầu tiên đang hoạt động trên hệ thống
                var featured = await _streamerService.GetFeaturedStreamersAsync(1);
                streamer = featured.FirstOrDefault();
            }

            if (streamer == null)
            {
                ViewBag.NotFoundSlug = targetIdentifier ?? "Streamer";
                return View("StreamerNotFound");
            }

            // Kiểm tra nếu người đang xem là chủ sở hữu kênh Streamer này (hoặc tài khoản Streamer đang xem trang của mình)
            bool isOwnerStreamer = false;
            bool isFollowing = false;
            decimal currentWalletBalance = 0; // [MỚI THÊM] Lấy số dư tài khoản hiện tại của Viewer
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUid))
            {
                isOwnerStreamer = (streamer.UserId == currentUid);
                isFollowing = await _streamerService.IsFollowingAsync(currentUid, streamer.Id);
                var currentUser = await _authService.GetUserByIdAsync(currentUid);
                if (currentUser != null)
                {
                    currentWalletBalance = currentUser.WalletBalance;
                }
            }
            ViewBag.IsOwnerStreamer = isOwnerStreamer;
            ViewBag.IsFollowing = isFollowing;

            var defaultDonorName = User.Identity?.IsAuthenticated == true ? (User.FindFirst("FullName")?.Value ?? User.Identity.Name ?? "") : "";
            var defaultAmount = streamer.MinDonateAmount > 0 ? streamer.MinDonateAmount : 20000;

            // [MỚI THÊM] Tạo nội dung chuyển khoản và sinh mã QR ban đầu khi mở trang
            var streamerIdentifier = !string.IsNullOrWhiteSpace(streamer.User?.AccountId) ? streamer.User.AccountId : streamer.Slug;
            var initialTransferContent = $"DONATE {streamerIdentifier} {(string.IsNullOrWhiteSpace(defaultDonorName) ? "Viewer" : defaultDonorName)}";
            var initialQrBase64 = GeneratePaymentQrBase64(
                streamer.BankName,
                streamer.BankAccountNumber,
                streamer.BankAccountName ?? streamer.DisplayName,
                defaultAmount,
                initialTransferContent);

            var model = new StreamerDonateViewModel
            {
                StreamerProfileId = streamer.Id,
                Slug = streamerIdentifier,
                DisplayName = streamer.DisplayName,
                AvatarUrl = string.IsNullOrWhiteSpace(streamer.AvatarUrl) ? "/images/default-avatar.png" : streamer.AvatarUrl,
                BannerUrl = string.IsNullOrWhiteSpace(streamer.BannerUrl) ? "/images/default-banner.jpg" : streamer.BannerUrl,
                GreetingMessage = streamer.GreetingMessage,
                Bio = streamer.Bio,
                MinDonateAmount = streamer.MinDonateAmount,
                FollowerCount = streamer.FollowerCount,
                Rank = streamer.Rank,
                IsVerified = streamer.IsVerified,
                IsActive = streamer.IsActive,
                LockReason = streamer.LockReason,
                IsFollowedByCurrentUser = isFollowing,
                IsCurrentUserOwner = isOwnerStreamer,
                BankName = streamer.BankName,
                BankAccountNumber = streamer.BankAccountNumber,
                BankAccountName = streamer.BankAccountName,
                PaymentQrUrl = streamer.PaymentQrUrl,
                YoutubeUrl = streamer.YoutubeUrl,
                TwitchUrl = streamer.TwitchUrl,
                DiscordUrl = streamer.DiscordUrl,
                FacebookUrl = streamer.FacebookUrl,
                TiktokUrl = streamer.TiktokUrl,
                DonorName = defaultDonorName,
                Amount = defaultAmount,
                // [MỚI THÊM] Gán số dư tài khoản, nội dung chuyển khoản và mã QR vừa sinh vào ViewModel
                CurrentWalletBalance = currentWalletBalance,
                TransferContent = initialTransferContent,
                GeneratedQrBase64 = initialQrBase64,
                PaymentMethod = (User.Identity?.IsAuthenticated == true && currentWalletBalance >= defaultAmount)
                    ? PaymentMethod.Wallet
                    : PaymentMethod.BankTransfer
            };

            if (streamer.ApprovalStatus == StreamerApprovalStatus.Pending)
            {
                model.ErrorMessage = "Kênh này đang trong thời gian chờ Quản trị viên xét duyệt. Chức năng gửi donate sẽ được kích hoạt sau khi kênh được phê duyệt chính thức.";
            }
            else if (streamer.ApprovalStatus == StreamerApprovalStatus.Rejected)
            {
                model.ErrorMessage = "Kênh này chưa được Quản trị viên phê duyệt.";
            }

            // Lấy danh sách các streamer khác đã có trong database
            var otherStreamers = await _context.StreamerProfiles
                .AsNoTracking()
                .Include(sp => sp.User)
                .Where(sp => sp.IsActive && sp.Id != streamer.Id)
                .OrderByDescending(sp => sp.TotalReceived)
                .ThenByDescending(sp => sp.FollowerCount)
                .Take(8)
                .ToListAsync();
            ViewBag.OtherStreamers = otherStreamers;

            // Lấy danh sách Status đã đăng của streamer này
            var streamerStatuses = await _context.StreamerStatuses
                .AsNoTracking()
                .Where(s => s.StreamerProfileId == streamer.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();
            ViewBag.StreamerStatuses = streamerStatuses;

            var likedCookie = Request.Cookies["LikedStatuses"] ?? "";
            var likedIds = likedCookie.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            ViewBag.LikedStatusIds = likedIds;

            return View(model);
        }

        /// <summary>
        /// Cho phép Streamer cập nhật trực tiếp Thông tin cá nhân & Lời giới thiệu (Bio) ngay trên trang cá nhân gộp
        /// </summary>
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBio(string slug, string displayName, string? greetingMessage, string? bio, IFormFile? avatarFile, IFormFile? bannerFile, string? bannerUrl)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _streamerService.GetByUserIdAsync(userId);
            if (streamer == null)
            {
                return RedirectToAction("Register", "Streamer");
            }

            // Kiểm tra bảo mật: Streamer chỉ có thể sửa trang của chính mình, chặn sửa trang của streamer khác
            var targetStreamer = await _streamerService.GetBySlugAsync(slug);
            if (targetStreamer != null && targetStreamer.UserId != userId)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền chỉnh sửa thông tin của streamer khác.";
                return Redirect("/" + (targetStreamer.User?.AccountId ?? targetStreamer.Slug));
            }

            // Lưu file ảnh đại diện vào wwwroot/images/avatars nếu người dùng có chọn file mới
            var uploadedAvatarPath = await SaveAvatarFileAsync(avatarFile, userId);
            // Lưu file ảnh bìa vào wwwroot/images/banners nếu người dùng có chọn file mới
            var uploadedBannerPath = await SaveBannerFileAsync(bannerFile, userId);
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.Values.SelectMany(value => value.Errors).First().ErrorMessage;
                return Redirect("/" + streamer.Slug);
            }

            var finalBannerUrl = !string.IsNullOrWhiteSpace(uploadedBannerPath)
                ? uploadedBannerPath
                : (!string.IsNullOrWhiteSpace(bannerUrl) ? bannerUrl.Trim() : streamer.BannerUrl);

            var configModel = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                Slug = streamer.Slug,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? streamer.DisplayName : displayName.Trim(),
                GreetingMessage = string.IsNullOrWhiteSpace(greetingMessage) ? streamer.GreetingMessage : greetingMessage.Trim(),
                Bio = bio,
                AvatarUrl = !string.IsNullOrWhiteSpace(uploadedAvatarPath) ? uploadedAvatarPath : streamer.AvatarUrl,
                BannerUrl = finalBannerUrl,
                MinDonateAmount = streamer.MinDonateAmount,
                BankName = streamer.BankName,
                BankAccountNumber = streamer.BankAccountNumber,
                BankAccountName = streamer.BankAccountName,
                PaymentQrUrl = streamer.PaymentQrUrl,
                YoutubeUrl = streamer.YoutubeUrl,
                TwitchUrl = streamer.TwitchUrl,
                DiscordUrl = streamer.DiscordUrl,
                FacebookUrl = streamer.FacebookUrl,
                TiktokUrl = streamer.TiktokUrl
            };

            var (success, error) = await _streamerService.UpdateProfileConfigAsync(userId, configModel);
            if (success)
            {
                await RefreshUserCookieClaimsAsync(userId);
                TempData["SuccessMessage"] = "Đã cập nhật Thông tin cá nhân & Lời giới thiệu (Bio) thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = error;
            }

            return Redirect("/" + streamer.Slug);
        }

        /// <summary>
        /// Xử lý gửi donate cho streamer (Được bảo vệ bởi bộ lọc Rate Limiting chống spam bot)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [DonateRateLimit]
        public async Task<IActionResult> Donate(StreamerDonateViewModel model)
        {
            var streamer = await _streamerService.GetBySlugAsync(model.Slug);
            if (streamer == null)
            {
                ViewBag.NotFoundSlug = model.Slug;
                return View("StreamerNotFound");
            }

            int? currentUserId = null;
            decimal currentWalletBalance = 0;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
                var currentUser = await _authService.GetUserByIdAsync(uid);
                if (currentUser != null)
                {
                    currentWalletBalance = currentUser.WalletBalance;
                }
            }

            // Kiểm tra quyền sở hữu kênh khi post donate
            ViewBag.IsOwnerStreamer = currentUserId.HasValue && (streamer.UserId == currentUserId.Value);

            // Gán lại thông tin streamer hiển thị
            model.StreamerProfileId = streamer.Id;
            model.DisplayName = streamer.DisplayName;
            model.AvatarUrl = streamer.AvatarUrl;
            model.BannerUrl = streamer.BannerUrl;
            model.GreetingMessage = streamer.GreetingMessage;
            model.Bio = streamer.Bio;
            model.MinDonateAmount = streamer.MinDonateAmount;
            model.BankName = streamer.BankName;
            model.BankAccountNumber = streamer.BankAccountNumber;
            model.BankAccountName = streamer.BankAccountName;
            model.PaymentQrUrl = streamer.PaymentQrUrl;
            model.IsActive = streamer.IsActive;
            model.LockReason = streamer.LockReason;
            model.CurrentWalletBalance = currentWalletBalance;

            // [MỚI THÊM] Sinh lại nội dung chuyển khoản và mã QR với số tiền & nội dung mà Viewer vừa nhập
            var cleanDonorName = string.IsNullOrWhiteSpace(model.DonorName) ? "Viewer" : model.DonorName.Trim();
            model.TransferContent = string.IsNullOrWhiteSpace(model.Message)
                ? $"DONATE {streamer.Slug} {cleanDonorName}"
                : $"DONATE {streamer.Slug} {cleanDonorName} - {model.Message.Trim()}";
            model.GeneratedQrBase64 = GeneratePaymentQrBase64(
                streamer.BankName,
                streamer.BankAccountNumber,
                streamer.BankAccountName ?? streamer.DisplayName,
                model.Amount,
                model.TransferContent);

            // KIỂM TRA: Nếu kênh streamer đang chờ duyệt hoặc bị từ chối thì chặn gửi donate
            if (streamer.ApprovalStatus == StreamerApprovalStatus.Pending)
            {
                model.ErrorMessage = "Kênh này đang chờ Quản trị viên xét duyệt. Chưa thể gửi donate vào lúc này.";
                return View(model);
            }
            if (streamer.ApprovalStatus == StreamerApprovalStatus.Rejected)
            {
                model.ErrorMessage = "Kênh này chưa được Quản trị viên phê duyệt. Không thể gửi donate.";
                return View(model);
            }

            // KIỂM TRA: Nếu kênh streamer đang bị Quản trị viên khóa vi phạm thì chặn gửi donate
            if (!streamer.IsActive)
            {
                model.ErrorMessage = $"Kênh streamer này hiện đang bị tạm khóa do vi phạm tiêu chuẩn cộng đồng (Lý do: {streamer.LockReason ?? "Vi phạm chính sách"}). Không thể gửi donate vào lúc này.";
                return View(model);
            }

            if (model.Amount < streamer.MinDonateAmount)
            {
                ModelState.AddModelError(nameof(model.Amount), $"Số tiền donate tối thiểu cho streamer này là {streamer.MinDonateAmount:N0} VNĐ.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, error, donation) = await _streamerService.ProcessDonationAsync(currentUserId, model);

            if (!success)
            {
                model.ErrorMessage = error;
                return View(model);
            }

            // [MỚI THÊM] Nếu thanh toán bằng số dư tài khoản thành công, cập nhật lại số dư hiển thị trên Header Cookie và ViewModel
            if (currentUserId.HasValue)
            {
                var updatedUser = await _authService.GetUserByIdAsync(currentUserId.Value);
                if (updatedUser != null)
                {
                    model.CurrentWalletBalance = updatedUser.WalletBalance;
                    var roles = await _authService.GetUserRolesAsync(updatedUser.Id);
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, updatedUser.Id.ToString()),
                        new Claim(ClaimTypes.Name, updatedUser.Username),
                        new Claim(ClaimTypes.Email, updatedUser.Email),
                        new Claim("FullName", updatedUser.FullName ?? updatedUser.Username),
                        new Claim("AvatarUrl", updatedUser.AvatarUrl ?? "/images/default-avatar.png"),
                        new Claim("WalletBalance", updatedUser.WalletBalance.ToString("N0")),
                        new Claim("AccountId", updatedUser.AccountId ?? "")
                    };
                    foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
                    if (updatedUser.StreamerProfile != null) claims.Add(new Claim("StreamerSlug", updatedUser.StreamerProfile.Slug));

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                }
            }

            var methodText = model.PaymentMethod == PaymentMethod.Wallet
                ? "từ Số dư tài khoản"
                : "qua Chuyển khoản ngân hàng (QR)";
            model.SuccessMessage = $"Cảm ơn bạn đã Donate {model.Amount:N0} VNĐ ({methodText}) cho {streamer.DisplayName}! Mã giao dịch: {donation?.TransactionCode}";
            return View(model);
        }

        /// <summary>
        /// POST: /Streamer/CreateBankDonation
        /// Tạo đơn Donate ở trạng thái Pending, sinh mã Memo VietQR dạng "DN{id}" để người dùng quét mã.
        /// Khi PayOS báo webhook, trạng thái sẽ tự động chuyển thành Success.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBankDonation([FromForm] string slug, [FromForm] string? donorName, [FromForm] decimal amount, [FromForm] string? message)
        {
            var streamer = await _streamerService.GetBySlugAsync(slug);
            if (streamer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy streamer." });
            }

            if (!streamer.IsActive)
            {
                return Json(new { success = false, message = "Kênh streamer này hiện đang bị khóa." });
            }

            if (streamer.ApprovalStatus != StreamerApprovalStatus.Approved)
            {
                return Json(new { success = false, message = "Kênh streamer chưa được phê duyệt để nhận donate." });
            }

            if (amount < 1000m || amount > 100000000m || amount != decimal.Truncate(amount))
            {
                return Json(new { success = false, message = "Số tiền donate phải là số nguyên từ 1.000 đến 100.000.000 VNĐ." });
            }

            if (amount < streamer.MinDonateAmount)
            {
                return Json(new { success = false, message = $"Số tiền ủng hộ tối thiểu là {streamer.MinDonateAmount:N0} VNĐ." });
            }

            int? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
            }

            var cleanDonorName = string.IsNullOrWhiteSpace(donorName) ? "Người ủng hộ ẩn danh" : donorName.Trim();

            // 1. Tạo đơn Donate ở trạng thái Pending
            var donation = new Donation
            {
                StreamerProfileId = streamer.Id,
                DonorUserId = currentUserId,
                DonorName = cleanDonorName,
                Amount = amount,
                Message = message?.Trim(),
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = DonationStatus.Pending,
                TransactionCode = $"DON_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
                CreatedAt = DateTime.UtcNow
            };

            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();

            // 2. Gán memo chuẩn dạng "DN" + donation.Id (Ví dụ: DN84920)
            var memo = $"DN{donation.Id}";
            donation.TransactionCode = memo;
            await _context.SaveChangesAsync();

            // 3. Lấy thông tin tài khoản ngân hàng (Ưu tiên tài khoản riêng của Streamer, fallback tài khoản hệ thống)
            var bankId = !string.IsNullOrWhiteSpace(streamer.BankName) ? streamer.BankName : (_configuration["BankConfig:BankId"] ?? "MB");
            var accountNo = !string.IsNullOrWhiteSpace(streamer.BankAccountNumber) ? streamer.BankAccountNumber : (_configuration["BankConfig:AccountNo"] ?? "0354031024");
            var accountName = !string.IsNullOrWhiteSpace(streamer.BankAccountName) ? streamer.BankAccountName : (_configuration["BankConfig:AccountName"] ?? "NGUYEN ANH HIEU");

            var encodedMemo = Uri.EscapeDataString(memo);
            var encodedAccountName = Uri.EscapeDataString(accountName);
            var vietQrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={(long)amount}&addInfo={encodedMemo}&accountName={encodedAccountName}";

            return Json(new
            {
                success = true,
                donationId = donation.Id,
                transactionCode = memo,
                memo = memo,
                amount = donation.Amount,
                formattedAmount = $"{donation.Amount:N0} VNĐ",
                bankId = bankId,
                accountNo = accountNo,
                accountName = accountName,
                qrUrl = vietQrUrl,
                streamerName = streamer.DisplayName,
                expireSeconds = 600
            });
        }

        /// <summary>
        /// GET: /Streamer/CheckDonationStatus?donationId=...&transactionCode=...
        /// Polling AJAX kiểm tra trạng thái của đơn Donate
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckDonationStatus([FromQuery] int? donationId, [FromQuery] string? transactionCode)
        {
            var query = _context.Donations.Include(d => d.StreamerProfile).AsQueryable();

            Donation? donation = null;
            if (donationId.HasValue && donationId.Value > 0)
            {
                donation = await query.FirstOrDefaultAsync(d => d.Id == donationId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(transactionCode))
            {
                var clean = transactionCode.Trim();
                donation = await query.FirstOrDefaultAsync(d => d.TransactionCode == clean);
            }

            if (donation == null)
            {
                return NotFound(new { success = false, isPaid = false, message = "Không tìm thấy đơn donate." });
            }

            var isPaid = donation.Status == DonationStatus.Success;

            return Json(new
            {
                success = true,
                isPaid = isPaid,
                status = donation.Status.ToString(),
                amount = donation.Amount,
                transactionCode = donation.TransactionCode,
                streamerName = donation.StreamerProfile?.DisplayName,
                message = isPaid ? "Ủng hộ streamer thành công!" : "Đang chờ chuyển khoản..."
            });
        }

        /// <summary>
        /// Trang Streamer Yêu Thích (Icon trái tim): hiển thị danh sách các Streamer mà người dùng đã nhấn Theo dõi.
        /// URL: /Streamer/Overview hoặc /Streamer/Favorites
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Overview()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _streamerService.GetByUserIdAsync(userId);
            var followedStreamers = await _streamerService.GetFollowedStreamersAsync(userId);
            ViewBag.Streamer = streamer;
            ViewBag.FollowedStreamers = followedStreamers;
            return View();
        }

        [Authorize]
        [HttpGet("/Streamer/Favorites")]
        public Task<IActionResult> Favorites() => Overview();

        /// <summary>
        /// API / Action bật/tắt theo dõi (Follow / Unfollow) streamer
        /// Hỗ trợ cả AJAX JSON POST và standard Form POST
        /// </summary>
        [Authorize]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ToggleFollow([FromBody] FollowRequestModel? jsonModel, [FromForm] int? streamerProfileId, [FromForm] string? returnUrl)
        {
            var targetId = jsonModel?.StreamerProfileId ?? streamerProfileId ?? 0;
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                if (Request.Headers["Accept"].ToString().Contains("application/json") || jsonModel != null)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập để theo dõi Streamer." });
                }
                return RedirectToAction("Login", "Auth");
            }

            var (isFollowing, followerCount, message) = await _streamerService.ToggleFollowAsync(userId, targetId);

            if (Request.Headers["Accept"].ToString().Contains("application/json") || jsonModel != null)
            {
                return Json(new
                {
                    success = true,
                    isFollowing,
                    followerCount,
                    message
                });
            }

            TempData["SuccessMessage"] = message;
            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return LocalRedirect(returnUrl);
            }
            return RedirectToAction(nameof(Overview));
        }

        /// <summary>
        /// Đăng Status mới cho Streamer (chỉ chủ kênh mới có quyền đăng)
        /// </summary>
        [Authorize]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CreateStatus([FromForm] int streamerProfileId, [FromForm] string? content, [FromForm] IFormFile? imageFile, [FromForm] string? returnUrl)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.Id == streamerProfileId);
            if (streamer == null || streamer.UserId != currentUid)
            {
                TempData["ErrorMessage"] = "Bạn không có quyền đăng status trên kênh này.";
                return Redirect(returnUrl ?? "/");
            }

            if (string.IsNullOrWhiteSpace(content) && (imageFile == null || imageFile.Length == 0))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập nội dung caption hoặc chọn ảnh để đăng status.";
                return Redirect(returnUrl ?? $"/streamer/{streamer.Slug}");
            }

            string? uploadedImagePath = null;
            if (imageFile != null && imageFile.Length > 0)
            {
                var (path, error) = await StatusUploadService.SaveAsync(imageFile, _webHostEnvironment.WebRootPath, streamer.Id, HttpContext.RequestAborted);
                if (!string.IsNullOrEmpty(error))
                {
                    TempData["ErrorMessage"] = error;
                    return Redirect(returnUrl ?? $"/streamer/{streamer.Slug}");
                }
                uploadedImagePath = path;
            }

            var status = new StreamerStatus
            {
                StreamerProfileId = streamer.Id,
                Content = (content ?? "").Trim(),
                ImageUrl = uploadedImagePath,
                LikeCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.StreamerStatuses.Add(status);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đã đăng status thành công!";
            return Redirect(returnUrl ?? $"/streamer/{streamer.Slug}");
        }

        /// <summary>
        /// Xóa Status (chỉ chủ kênh sở hữu status mới được xóa)
        /// </summary>
        [Authorize]
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteStatus([FromForm] int statusId, [FromForm] string? returnUrl)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUid))
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập." });
            }

            var status = await _context.StreamerStatuses
                .Include(s => s.StreamerProfile)
                .FirstOrDefaultAsync(s => s.Id == statusId);

            if (status == null)
            {
                return Json(new { success = false, message = "Không tìm thấy status." });
            }

            if (status.StreamerProfile.UserId != currentUid)
            {
                return Json(new { success = false, message = "Bạn không có quyền xóa bài viết này." });
            }

            if (!string.IsNullOrEmpty(status.ImageUrl))
            {
                try
                {
                    var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, status.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
                catch { }
            }

            var slug = status.StreamerProfile.Slug;
            _context.StreamerStatuses.Remove(status);
            await _context.SaveChangesAsync();

            if (Request.Headers["Accept"].ToString().Contains("application/json") || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, message = "Đã xóa status thành công." });
            }

            TempData["SuccessMessage"] = "Đã xóa status thành công!";
            return Redirect(returnUrl ?? $"/streamer/{slug}");
        }

        /// <summary>
        /// Bật/tắt thả tim Status (hỗ trợ cả Viewer, Streamer, Khách vãng lai)
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ToggleLikeStatus([FromForm] int statusId)
        {
            var status = await _context.StreamerStatuses.FirstOrDefaultAsync(s => s.Id == statusId);
            if (status == null)
            {
                return Json(new { success = false, message = "Không tìm thấy status." });
            }

            var likedCookie = Request.Cookies["LikedStatuses"] ?? "";
            var likedIds = likedCookie.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            bool isLiked;

            if (likedIds.Contains(statusId.ToString()))
            {
                likedIds.Remove(statusId.ToString());
                status.LikeCount = Math.Max(0, status.LikeCount - 1);
                isLiked = false;
            }
            else
            {
                likedIds.Add(statusId.ToString());
                status.LikeCount += 1;
                isLiked = true;
            }

            await _context.SaveChangesAsync();

            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(365),
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                IsEssential = true
            };
            Response.Cookies.Append("LikedStatuses", string.Join(",", likedIds), cookieOptions);

            return Json(new { success = true, isLiked, likeCount = status.LikeCount });
        }

        /// <summary>
        /// Trang cấu hình trang nhận donate (slug, avatar, banner, lời chào, mức tối thiểu)
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Config()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _streamerService.GetByUserIdAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa có hồ sơ Streamer. Vui lòng đăng ký trước khi cấu hình!";
                return RedirectToAction("Register", "Streamer");
            }

            // Xây dựng đường dẫn trang donate của streamer: https://localhost:7111/{ID hoặc Slug}
            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            var identifier = !string.IsNullOrWhiteSpace(streamer.Slug) ? streamer.Slug : streamer.Id.ToString();
            var donatePageUrl = $"{scheme}://{host}/{identifier}";

            // Sinh mã QR Base64 PNG chuẩn phân giải cao (Level Q) nhúng web và OBS
            var qrCodeBase64 = GenerateQrCodeBase64(donatePageUrl);

            var model = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                DisplayName = streamer.DisplayName,
                Slug = streamer.Slug,
                GreetingMessage = streamer.GreetingMessage,
                Bio = streamer.Bio,
                AvatarUrl = streamer.AvatarUrl,
                BannerUrl = streamer.BannerUrl,
                MinDonateAmount = streamer.MinDonateAmount,
                BankName = streamer.BankName,
                BankAccountNumber = streamer.BankAccountNumber,
                BankAccountName = streamer.BankAccountName,
                PaymentQrUrl = streamer.PaymentQrUrl,
                YoutubeUrl = streamer.YoutubeUrl,
                TwitchUrl = streamer.TwitchUrl,
                DiscordUrl = streamer.DiscordUrl,
                FacebookUrl = streamer.FacebookUrl,
                TiktokUrl = streamer.TiktokUrl,
                DonatePageUrl = donatePageUrl,
                QrCodeImageBase64 = qrCodeBase64
            };

            return View(model);
        }

        /// <summary>
        /// Xem thông tin chi tiết và mã QR của Streamer
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Details(string? slug, string? id)
        {
            string? target = !string.IsNullOrWhiteSpace(slug) ? slug : id;
            if (string.IsNullOrWhiteSpace(target))
            {
                var currentStreamerSlug = User.FindFirst("StreamerSlug")?.Value;
                target = currentStreamerSlug;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return RedirectToAction(nameof(Config));
            }

            var streamer = await _streamerService.GetBySlugAsync(target);
            if (streamer == null)
            {
                return NotFound();
            }

            var scheme = Request.Scheme;
            var host = Request.Host.Value;
            var identifier = !string.IsNullOrWhiteSpace(streamer.Slug) ? streamer.Slug : streamer.Id.ToString();
            var donatePageUrl = $"{scheme}://{host}/{identifier}";
            var qrCodeBase64 = GenerateQrCodeBase64(donatePageUrl);

            var model = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                DisplayName = streamer.DisplayName,
                Slug = streamer.Slug,
                GreetingMessage = streamer.GreetingMessage,
                Bio = streamer.Bio,
                AvatarUrl = streamer.AvatarUrl,
                BannerUrl = streamer.BannerUrl,
                MinDonateAmount = streamer.MinDonateAmount,
                DonatePageUrl = donatePageUrl,
                QrCodeImageBase64 = qrCodeBase64
            };

            return View(model);
        }

        /// <summary>
        /// Lưu cấu hình trang nhận donate
        /// </summary>
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Config(StreamerProfileConfigViewModel model, IFormFile? avatarFile, IFormFile? bannerFile)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _streamerService.GetByUserIdAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa có hồ sơ Streamer. Vui lòng đăng ký trước!";
                return RedirectToAction("Register", "Streamer");
            }

            if (!ModelState.IsValid)
            {
                model.AvatarUrl = streamer.AvatarUrl;
                model.BannerUrl = streamer.BannerUrl;
                return View(model);
            }

            var fileToSave = avatarFile ?? model.AvatarFile;
            var uploadedAvatarPath = await SaveAvatarFileAsync(fileToSave, userId);

            var bannerToSave = bannerFile ?? model.BannerFile;
            var uploadedBannerPath = await SaveBannerFileAsync(bannerToSave, userId);

            if (!ModelState.IsValid)
            {
                model.AvatarUrl = streamer.AvatarUrl;
                model.BannerUrl = streamer.BannerUrl;
                return View(model);
            }

            model.AvatarUrl = !string.IsNullOrWhiteSpace(uploadedAvatarPath)
                ? uploadedAvatarPath
                : streamer.AvatarUrl;

            model.BannerUrl = !string.IsNullOrWhiteSpace(uploadedBannerPath)
                ? uploadedBannerPath
                : (!string.IsNullOrWhiteSpace(model.BannerUrl) ? model.BannerUrl.Trim() : streamer.BannerUrl);

            var (success, error) = await _streamerService.UpdateProfileConfigAsync(userId, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            await RefreshUserCookieClaimsAsync(userId);
            TempData["SuccessMessage"] = "Cập nhật cấu hình trang donate thành công! Xem trang của bạn tại: /" + model.Slug;
            return RedirectToAction(nameof(Config));
        }

        /// <summary>
        /// Mở form đăng ký Streamer
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Register()
        {
            var model = new StreamerRegisterViewModel();

            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var user = await _authService.GetUserByIdAsync(userId);
                if (user != null)
                {
                    if (user.StreamerProfile != null)
                    {
                        if (user.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
                        {
                            TempData["SuccessMessage"] = "Bạn đã có hồ sơ Streamer chính thức rồi!";
                            return RedirectToAction("Config", "Streamer");
                        }

                        // Hồ sơ đang chờ duyệt hoặc bị từ chối
                        ViewBag.PendingProfile = user.StreamerProfile;
                        model.FullName = user.FullName;
                        model.PhoneNumber = user.PhoneNumber ?? "";
                        model.Email = user.Email;
                        model.Slug = user.StreamerProfile.Slug;
                        model.DisplayName = user.StreamerProfile.DisplayName;
                        model.BankName = user.StreamerProfile.BankName ?? "";
                        model.BankAccountNumber = user.StreamerProfile.BankAccountNumber ?? "";
                        model.BankAccountName = user.StreamerProfile.BankAccountName ?? "";
                        model.FacebookUrl = user.StreamerProfile.FacebookUrl;
                        model.YoutubeUrl = user.StreamerProfile.YoutubeUrl;
                        model.TiktokUrl = user.StreamerProfile.TiktokUrl;
                        return View(model);
                    }

                    model.FullName = user.FullName;
                    model.PhoneNumber = user.PhoneNumber ?? "";
                    model.Email = user.Email;
                    model.Slug = user.Username.ToLower();
                    model.DisplayName = user.FullName;
                }
            }

            return View(model);
        }

        /// <summary>
        /// Xử lý đăng ký Streamer và gửi lệnh duyệt đến Quản trị viên
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(StreamerRegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            int? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
            }

            var (success, error, profile) = await _streamerService.RegisterStreamerAsync(currentUserId, model);

            if (!success || profile == null)
            {
                ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            TempData["SuccessMessage"] = "Đăng ký làm Streamer thành công! Yêu cầu của bạn đã được gửi đến Quản trị viên để xét duyệt. Bạn sẽ được cấp quyền và kích hoạt kênh ngay khi được phê duyệt.";
            return RedirectToAction(nameof(Register));
        }

        /// <summary>
        /// Hủy tư cách Streamer của tài khoản, đưa tài khoản về vai trò Viewer và đổi đầu ID thành 9
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelStreamer()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var (success, message, user) = await _streamerService.CancelStreamerAsync(userId);
            if (!success || user == null)
            {
                TempData["ErrorMessage"] = message;
                return RedirectToAction(nameof(Config));
            }

            // Làm mới Cookie Authentication để gỡ bỏ quyền Streamer và claim StreamerSlug, cập nhật ID đầu 9
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

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            TempData["SuccessMessage"] = message;
            return RedirectToAction("Index", "Profile");
        }
    }

    public class FollowRequestModel
    {
        public int StreamerProfileId { get; set; }
    }
}
