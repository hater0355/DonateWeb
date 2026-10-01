using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder; // [MỚI THÊM] Thư viện QRCoder để sinh mã QR thanh toán chứa số tiền và nội dung chuyển khoản
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;
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
        private readonly ILogger<StreamerController> _logger;

        public StreamerController(
            IStreamerService streamerService,
            IAuthService authService,
            IWebHostEnvironment webHostEnvironment,
            IQrCodeService qrCodeService,
            ILogger<StreamerController> logger)
        {
            _streamerService = streamerService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
            _qrCodeService = qrCodeService;
            _logger = logger;
        }

        /// <summary>
        /// Lưu file ảnh đại diện được tải lên từ máy tính vào thư mục vật lý wwwroot/images/avatars
        /// và trả về đường dẫn lưu vào Database (/images/avatars/ten-file)
        /// </summary>
        private async Task<string?> SaveAvatarFileAsync(IFormFile? avatarFile, int userId)
        {
            if (avatarFile == null || avatarFile.Length == 0)
            {
                return null;
            }

            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "avatars");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var extension = Path.GetExtension(avatarFile.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var uniqueFileName = $"avatar_{userId}_{Guid.NewGuid():N}{extension}";
            var physicalFilePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(physicalFilePath, FileMode.Create))
            {
                await avatarFile.CopyToAsync(fileStream);
            }

            return $"/images/avatars/{uniqueFileName}";
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
            decimal currentWalletBalance = 0; // [MỚI THÊM] Lấy số dư tài khoản hiện tại của Viewer
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUid))
            {
                isOwnerStreamer = (streamer.UserId == currentUid);
                var currentUser = await _authService.GetUserByIdAsync(currentUid);
                if (currentUser != null)
                {
                    currentWalletBalance = currentUser.WalletBalance;
                }
            }
            ViewBag.IsOwnerStreamer = isOwnerStreamer;

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

            return View(model);
        }

        /// <summary>
        /// Cho phép Streamer cập nhật trực tiếp Thông tin cá nhân & Lời giới thiệu (Bio) ngay trên trang cá nhân gộp
        /// </summary>
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBio(string slug, string displayName, string? greetingMessage, string? bio, IFormFile? avatarFile, string? bannerUrl)
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

            // Lưu file ảnh đại diện vào wwwroot/images/avatars nếu người dùng có chọn file mới
            var uploadedAvatarPath = await SaveAvatarFileAsync(avatarFile, userId);

            var configModel = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                Slug = streamer.Slug,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? streamer.DisplayName : displayName.Trim(),
                GreetingMessage = string.IsNullOrWhiteSpace(greetingMessage) ? streamer.GreetingMessage : greetingMessage.Trim(),
                Bio = bio,
                AvatarUrl = !string.IsNullOrWhiteSpace(uploadedAvatarPath) ? uploadedAvatarPath : streamer.AvatarUrl,
                BannerUrl = string.IsNullOrWhiteSpace(bannerUrl) ? streamer.BannerUrl : bannerUrl.Trim(),
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
        /// Bảng điều khiển tổng quan dành cho Streamer (cho phép cả Viewer và Admin xem khám phá)
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
            var featuredStreamers = await _streamerService.GetFeaturedStreamersAsync(8);
            ViewBag.Streamer = streamer;
            ViewBag.FeaturedStreamers = featuredStreamers;
            return View();
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
        public async Task<IActionResult> Config(StreamerProfileConfigViewModel model, IFormFile? avatarFile)
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
                return View(model);
            }

            var fileToSave = avatarFile ?? model.AvatarFile;
            var uploadedAvatarPath = await SaveAvatarFileAsync(fileToSave, userId);
            model.AvatarUrl = !string.IsNullOrWhiteSpace(uploadedAvatarPath)
                ? uploadedAvatarPath
                : streamer.AvatarUrl;

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
}
