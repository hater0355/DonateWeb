using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;
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
        private readonly IPayOSService _payOS;
        private readonly IContentModerationService _contentModeration;
        private readonly IStreamerIdentityDocumentService _identityDocumentService;
        private readonly IStreamerBankQrService _streamerBankQrService;
        private readonly StreamerAgreementContentService _agreementContent;
        private readonly IDataProtector _contractSnapshotProtector;

        public StreamerController(
            IStreamerService streamerService,
            IAuthService authService,
            IWebHostEnvironment webHostEnvironment,
            IQrCodeService qrCodeService,
            AppDbContext context,
            IConfiguration configuration,
            ILogger<StreamerController> logger,
            IPayOSService payOS,
            IContentModerationService contentModeration,
            IStreamerIdentityDocumentService identityDocumentService,
            IStreamerBankQrService streamerBankQrService,
            StreamerAgreementContentService agreementContent,
            IDataProtectionProvider dataProtectionProvider)
        {
            _streamerService = streamerService;
            _authService = authService;
            _webHostEnvironment = webHostEnvironment;
            _qrCodeService = qrCodeService;
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _payOS = payOS;
            _contentModeration = contentModeration;
            _identityDocumentService = identityDocumentService;
            _streamerBankQrService = streamerBankQrService;
            _agreementContent = agreementContent;
            _contractSnapshotProtector = dataProtectionProvider.CreateProtector("DonateWeb.StreamerContractSnapshot.v1");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Contract()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var profile = await _context.StreamerProfiles.Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null || profile.ApprovalStatus != StreamerApprovalStatus.Approved ||
                string.IsNullOrWhiteSpace(profile.RequiredContractVersion))
                return RedirectToAction(nameof(Config));

            var identity = _identityDocumentService.UnprotectIdentity(profile.IdentityDataProtected);
            if (identity == null)
                return View("Contract", MakeContractViewModel(profile, null, null, "Không đọc được hồ sơ định danh đã mã hóa. Vui lòng liên hệ hỗ trợ trước khi ký."));

            var acceptance = await _context.StreamerContractAcceptances.AsNoTracking()
                .FirstOrDefaultAsync(a => a.StreamerProfileId == profile.Id && a.ContractVersion == profile.RequiredContractVersion);
            string? snapshot = null;
            if (acceptance != null)
            {
                try { snapshot = _contractSnapshotProtector.Unprotect(acceptance.ContractSnapshotProtected); }
                catch (System.Security.Cryptography.CryptographicException) { }
            }

            var model = MakeContractViewModel(profile, identity, acceptance, null);
            model.AcceptedSnapshot = snapshot;
            if (acceptance != null && snapshot == null)
                model.ErrorMessage = "Không mở được bản hợp đồng đã ký. Vui lòng liên hệ hỗ trợ; bản ghi và mã kiểm tra vẫn được lưu.";
            return View("Contract", model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ContractAccept(string signerFullName, bool acceptAgreement, bool acknowledgePrivacy)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var profile = await _context.StreamerProfiles.Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null || profile.ApprovalStatus != StreamerApprovalStatus.Approved ||
                string.IsNullOrWhiteSpace(profile.RequiredContractVersion))
                return RedirectToAction(nameof(Config));

            var existing = await _context.StreamerContractAcceptances.AsNoTracking()
                .FirstOrDefaultAsync(a => a.StreamerProfileId == profile.Id && a.ContractVersion == profile.RequiredContractVersion);
            if (existing != null)
                return RedirectToAction(nameof(Contract));

            var identity = _identityDocumentService.UnprotectIdentity(profile.IdentityDataProtected);
            var options = _configuration.GetSection(PlatformContractOptions.SectionName).Get<PlatformContractOptions>() ?? new();
            string? error = null;
            if (!options.IsComplete)
                error = "Bên vận hành chưa hoàn tất thông tin pháp lý của nền tảng. Chưa thể ký hợp đồng cho tới khi chủ nền tảng cấu hình đủ tư cách, địa chỉ, mã số thuế/mã định danh phù hợp với tư cách hoạt động và email liên hệ.";
            else if (identity == null || profile.User == null)
                error = "Không đọc được thông tin định danh hoặc tài khoản. Vui lòng liên hệ hỗ trợ.";
            else if (!acceptAgreement || !acknowledgePrivacy)
                error = "Bạn cần xác nhận đã đọc hợp đồng và chính sách bảo mật trước khi ký.";
            else if (string.IsNullOrWhiteSpace(signerFullName) ||
                     !string.Equals(NormalizePersonName(signerFullName), NormalizePersonName(identity.FullName), StringComparison.OrdinalIgnoreCase))
                error = "Họ tên xác nhận phải trùng với họ tên trên hồ sơ CCCD đã được duyệt.";
            else if (!TryGetAdultBirthDate(identity.DateOfBirth, out _))
                error = "Quy trình ký hiện chỉ hỗ trợ người từ đủ 18 tuổi. Nếu thông tin ngày sinh không chính xác, hãy liên hệ hỗ trợ để được hướng dẫn.";

            if (error != null)
            {
                var viewModel = MakeContractViewModel(profile, identity, null, error);
                return View("Contract", viewModel);
            }

            var signedAt = DateTime.UtcNow;
            var snapshotText = _agreementContent.BuildSnapshot(options, profile.User!, profile, identity!, GetPublicBaseUrl(), signedAt, signerFullName);
            var acceptance = new StreamerContractAcceptance
            {
                StreamerProfileId = profile.Id,
                UserId = userId,
                ContractVersion = profile.RequiredContractVersion!,
                ContractSnapshotProtected = _contractSnapshotProtector.Protect(snapshotText),
                ContractSnapshotSha256 = StreamerAgreementContentService.Sha256(snapshotText),
                SignerFullName = signerFullName.Trim(),
                SignedAtUtc = signedAt,
                ClientIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 500 } agent ? agent[..500] : Request.Headers.UserAgent.ToString()
            };
            _context.StreamerContractAcceptances.Add(acceptance);
            profile.IsActive = true;
            profile.UpdatedAt = signedAt;
            await _context.SaveChangesAsync();
            await RefreshUserCookieClaimsAsync(userId);

            TempData["SuccessMessage"] = "Hợp đồng đã được ký và hồ sơ Streamer đã được kích hoạt.";
            return RedirectToAction(nameof(Contract));
        }

        private StreamerContractViewModel MakeContractViewModel(
            StreamerProfile profile,
            StreamerIdentityData? identity,
            StreamerContractAcceptance? acceptance,
            string? error)
        {
            var options = _configuration.GetSection(PlatformContractOptions.SectionName).Get<PlatformContractOptions>() ?? new();
            return new StreamerContractViewModel
            {
                Version = _agreementContent.Document.Version,
                Title = _agreementContent.Document.Title,
                Sections = _agreementContent.Document.Sections,
                PlatformDetailsComplete = options.IsComplete,
                PlatformLegalName = options.OperatorLegalName,
                PlatformCapacity = options.OperatorCapacity,
                PlatformAddress = options.OperatorAddress,
                PlatformRegistrationNumber = options.OperatorRegistrationNumber,
                PlatformEmail = options.ContactEmail,
                PlatformWebsite = GetPublicBaseUrl(),
                StreamerFullName = identity?.FullName ?? string.Empty,
                StreamerCitizenIdMasked = StreamerAgreementContentService.MaskIdentityNumber(identity?.CitizenId),
                StreamerAddress = identity?.PermanentAddress ?? string.Empty,
                StreamerEmail = profile.User?.Email ?? string.Empty,
                StreamerChannelName = profile.DisplayName,
                StreamerSlug = profile.Slug,
                ErrorMessage = error,
                HasAccepted = acceptance != null,
                AcceptedHash = acceptance?.ContractSnapshotSha256,
                SignedAtUtc = acceptance?.SignedAtUtc
            };
        }

        private static string NormalizePersonName(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

        private static bool TryGetAdultBirthDate(string value, out DateOnly dateOfBirth)
        {
            if (!DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out dateOfBirth))
                return false;
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var age = today.Year - dateOfBirth.Year;
            if (dateOfBirth > today.AddYears(-age)) age--;
            return age >= 18;
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

        private string GetPublicBaseUrl()
        {
            var configuredBaseUrl = _configuration["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
            if (Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredUri) &&
                (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
            {
                return configuredBaseUrl!;
            }

            return $"{Request.Scheme}://{Request.Host}";
        }

        private async Task PopulateConfigQrFieldsAsync(StreamerProfileConfigViewModel model, StreamerProfile streamer)
        {
            var identifier = !string.IsNullOrWhiteSpace(model.Slug) ? model.Slug : streamer.Id.ToString();
            model.DonatePageUrl = $"{GetPublicBaseUrl()}/{Uri.EscapeDataString(identifier)}";
            model.QrCodeImageBase64 = GenerateQrCodeBase64(model.DonatePageUrl);
            model.BankDonationTransferCode = $"DW{streamer.Id}";

            // QR của mọi streamer dùng chung tài khoản đã liên kết với SePay.
            model.CentralBankName = _configuration["SePay:BankName"]?.Trim();
            model.CentralBankAccountNumber = _configuration["SePay:AccountNumber"]?.Trim();
            model.CentralBankAccountName = _configuration["SePay:AccountName"]?.Trim();
            model.BankDonationQrDataUri = streamer.IsActive && streamer.ApprovalStatus == StreamerApprovalStatus.Approved
                ? await _streamerBankQrService.CreateStaticQrImageDataUriAsync(
                    model.CentralBankName,
                    model.CentralBankAccountNumber,
                    model.CentralBankAccountName,
                    model.BankDonationTransferCode,
                    HttpContext.RequestAborted)
                : null;
        }

        /// <summary>Gắn QR chuyển khoản livestream dùng chung tài khoản SePay và mã riêng của streamer.</summary>
        private async Task PopulateLiveDonationQrAsync(StreamerDonateViewModel model, StreamerProfile streamer)
        {
            model.BankDonationTransferCode = $"DW{streamer.Id}";
            model.CentralBankName = _configuration["SePay:BankName"]?.Trim();
            model.CentralBankAccountNumber = _configuration["SePay:AccountNumber"]?.Trim();
            model.CentralBankAccountName = _configuration["SePay:AccountName"]?.Trim();
            model.BankDonationQrDataUri = streamer.IsActive && streamer.ApprovalStatus == StreamerApprovalStatus.Approved
                ? await _streamerBankQrService.CreateStaticQrImageDataUriAsync(
                    model.CentralBankName,
                    model.CentralBankAccountNumber,
                    model.CentralBankAccountName,
                    model.BankDonationTransferCode,
                    HttpContext.RequestAborted)
                : null;
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

            // Hồ sơ đang chờ duyệt/từ chối chỉ được xem bởi chính chủ; không công khai trang nhận donate.
            if ((streamer.ApprovalStatus != StreamerApprovalStatus.Approved || !streamer.IsActive) && !isOwnerStreamer)
            {
                ViewBag.NotFoundSlug = targetIdentifier ?? streamer.Slug;
                return View("StreamerNotFound");
            }

            ViewBag.IsOwnerStreamer = isOwnerStreamer;
            ViewBag.IsFollowing = isFollowing;

            var defaultDonorName = User.Identity?.IsAuthenticated == true ? (User.FindFirst("FullName")?.Value ?? User.Identity.Name ?? "") : "";
            var defaultAmount = streamer.MinDonateAmount > 0 ? streamer.MinDonateAmount : 20000;

            // QR cố định ở thẻ "Quét nhanh" là QR chuyển khoản livestream qua tài khoản SePay trung tâm.
            // Mã DW{id} trong nội dung giúp webhook phân bổ đúng streamer và phát alert.

            var model = new StreamerDonateViewModel
            {
                StreamerProfileId = streamer.Id,
                Slug = streamer.Slug,
                DisplayName = streamer.DisplayName,
                AvatarUrl = string.IsNullOrWhiteSpace(streamer.AvatarUrl) ? "/images/default-avatar.png" : streamer.AvatarUrl,
                BannerUrl = string.IsNullOrWhiteSpace(streamer.BannerUrl) ? "/images/default-banner.jpg" : streamer.BannerUrl,
                GreetingMessage = streamer.GreetingMessage,
                Bio = ProfileBioSanitizer.Sanitize(streamer.Bio),
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
                PaymentMethod = (User.Identity?.IsAuthenticated == true && currentWalletBalance >= defaultAmount)
                    ? PaymentMethod.Wallet
                    : PaymentMethod.BankTransfer
            };

            await PopulateLiveDonationQrAsync(model, streamer);

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
            model.Bio = ProfileBioSanitizer.Sanitize(streamer.Bio);
            model.MinDonateAmount = streamer.MinDonateAmount;
            model.BankName = streamer.BankName;
            model.BankAccountNumber = streamer.BankAccountNumber;
            model.BankAccountName = streamer.BankAccountName;
            model.PaymentQrUrl = streamer.PaymentQrUrl;
            model.IsActive = streamer.IsActive;
            model.LockReason = streamer.LockReason;
            model.CurrentWalletBalance = currentWalletBalance;
            await PopulateLiveDonationQrAsync(model, streamer);

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
        /// Tạo đơn donate PayOS riêng cho từng khoản ủng hộ.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [DonateRateLimit(5, 60, "bank_donation_create")]
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

            var rawDonorName = string.IsNullOrWhiteSpace(donorName) ? "Người ủng hộ ẩn danh" : donorName.Trim();
            var cleanDonorName = _contentModeration.ModerateContent(rawDonorName).SanitizedText.Trim();
            var cleanMessage = _contentModeration.ModerateContent(message, rawDonorName).SanitizedText.Trim();
            if (string.IsNullOrWhiteSpace(cleanDonorName)) cleanDonorName = "Người ủng hộ ẩn danh";
            if (cleanDonorName.Length > 50) cleanDonorName = cleanDonorName[..50];
            if (cleanMessage.Length > 500) cleanMessage = cleanMessage[..500];

            var baseUrl = GetPublicBaseUrl();
            var streamerPageUrl = $"{baseUrl}/{Uri.EscapeDataString(streamer.Slug)}";
            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000 + Random.Shared.Next(0, 1000);
            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

            // Lưu đơn Pending trước để webhook đối chiếu theo orderCode.
            var donation = new Donation
            {
                StreamerProfileId = streamer.Id,
                DonorUserId = currentUserId,
                DonorName = cleanDonorName,
                Amount = amount,
                Message = string.IsNullOrWhiteSpace(cleanMessage) ? "Đã gửi ủng hộ" : cleanMessage,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = DonationStatus.Pending,
                TransactionCode = $"DON_{orderCode}",
                CreatedAt = DateTime.UtcNow
            };

            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();

            try
            {
                var payment = await _payOS.createPaymentLink(new PaymentData
                {
                    orderCode = orderCode,
                    amount = decimal.ToInt32(amount),
                    // Mô tả PayOS ngắn để tương thích với các ngân hàng giới hạn nội dung.
                    description = "DONATE",
                    cancelUrl = $"{streamerPageUrl}?payment=cancel&orderCode={orderCode}",
                    returnUrl = $"{streamerPageUrl}?payment=return&orderCode={orderCode}",
                    buyerName = cleanDonorName,
                    expiredAt = (int)expiresAt.ToUnixTimeSeconds()
                });

                if (string.IsNullOrWhiteSpace(payment.qrCode))
                    throw new InvalidOperationException("PayOS không trả về dữ liệu QR cho đơn thanh toán.");

                return Json(new
                {
                    success = true,
                    donationId = donation.Id,
                    transactionCode = donation.TransactionCode,
                    orderCode,
                    memo = payment.description,
                    amount = donation.Amount,
                    formattedAmount = $"{donation.Amount:N0} VNĐ",
                    bankId = payment.bin,
                    accountNo = payment.accountNumber,
                    accountName = payment.accountName,
                    qrUrl = GenerateQrCodeBase64(payment.qrCode),
                    checkoutUrl = payment.checkoutUrl,
                    streamerName = streamer.DisplayName,
                    expireSeconds = Math.Max(1, (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds)
                });
            }
            catch (Exception ex)
            {
                donation.Status = DonationStatus.Failed;
                await _context.SaveChangesAsync();
                _logger.LogError(ex, "Không tạo được payment link PayOS cho donation {DonationId}.", donation.Id);
                return Json(new { success = false, message = "Không tạo được thanh toán PayOS. Vui lòng kiểm tra cấu hình PayOS rồi thử lại." });
            }
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
            var identifier = !string.IsNullOrWhiteSpace(streamer.Slug) ? streamer.Slug : streamer.Id.ToString();
            var donatePageUrl = $"{GetPublicBaseUrl()}/{Uri.EscapeDataString(identifier)}";

            // Sinh mã QR Base64 PNG chuẩn phân giải cao (Level Q) nhúng web và OBS
            var qrCodeBase64 = GenerateQrCodeBase64(donatePageUrl);
            var model = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                DisplayName = streamer.DisplayName,
                Slug = streamer.Slug,
                GreetingMessage = streamer.GreetingMessage,
                Bio = ProfileBioSanitizer.Sanitize(streamer.Bio),
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
                QrCodeImageBase64 = qrCodeBase64,
            };

            await PopulateConfigQrFieldsAsync(model, streamer);
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

            var isOwner = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId)
                && streamer.UserId == currentUserId;
            if (streamer.ApprovalStatus != StreamerApprovalStatus.Approved && !isOwner)
            {
                return NotFound();
            }

            var identifier = !string.IsNullOrWhiteSpace(streamer.Slug) ? streamer.Slug : streamer.Id.ToString();
            var donatePageUrl = $"{GetPublicBaseUrl()}/{Uri.EscapeDataString(identifier)}";
            var qrCodeBase64 = GenerateQrCodeBase64(donatePageUrl);

            var model = new StreamerProfileConfigViewModel
            {
                Id = streamer.Id,
                DisplayName = streamer.DisplayName,
                Slug = streamer.Slug,
                GreetingMessage = streamer.GreetingMessage,
                Bio = ProfileBioSanitizer.Sanitize(streamer.Bio),
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

            var bankFields = new[] { model.BankName, model.BankAccountNumber, model.BankAccountName };
            var hasAnyBankField = bankFields.Any(value => !string.IsNullOrWhiteSpace(value));
            var hasAllBankFields = bankFields.All(value => !string.IsNullOrWhiteSpace(value));
            if (hasAnyBankField && !hasAllBankFields)
            {
                ModelState.AddModelError(nameof(model.BankName), "Để lưu thông tin nhận tiền khi rút doanh thu, hãy điền đủ ngân hàng, số tài khoản và tên chủ tài khoản.");
            }
            if (!string.IsNullOrWhiteSpace(model.BankAccountNumber) &&
                !System.Text.RegularExpressions.Regex.IsMatch(model.BankAccountNumber.Trim(), "^[A-Za-z0-9]{6,19}$"))
            {
                ModelState.AddModelError(nameof(model.BankAccountNumber), "Số tài khoản phải gồm 6–19 chữ cái hoặc chữ số, không có khoảng trắng.");
            }

            if (!ModelState.IsValid)
            {
                model.Bio = ProfileBioSanitizer.Sanitize(model.Bio);
                model.AvatarUrl = streamer.AvatarUrl;
                model.BannerUrl = streamer.BannerUrl;
                model.PaymentQrUrl = streamer.PaymentQrUrl;
                await PopulateConfigQrFieldsAsync(model, streamer);
                return View(model);
            }

            var fileToSave = avatarFile ?? model.AvatarFile;
            var uploadedAvatarPath = await SaveAvatarFileAsync(fileToSave, userId);

            var bannerToSave = bannerFile ?? model.BannerFile;
            var uploadedBannerPath = await SaveBannerFileAsync(bannerToSave, userId);

            if (!ModelState.IsValid)
            {
                model.Bio = ProfileBioSanitizer.Sanitize(model.Bio);
                model.AvatarUrl = streamer.AvatarUrl;
                model.BannerUrl = streamer.BannerUrl;
                model.PaymentQrUrl = streamer.PaymentQrUrl;
                await PopulateConfigQrFieldsAsync(model, streamer);
                return View(model);
            }

            model.AvatarUrl = !string.IsNullOrWhiteSpace(uploadedAvatarPath)
                ? uploadedAvatarPath
                : streamer.AvatarUrl;

            model.BannerUrl = !string.IsNullOrWhiteSpace(uploadedBannerPath)
                ? uploadedBannerPath
                : (!string.IsNullOrWhiteSpace(model.BannerUrl) ? model.BannerUrl.Trim() : streamer.BannerUrl);

            // Thông tin ngân hàng cá nhân là nơi nhận tiền khi streamer rút doanh thu;
            // QR donate luôn dùng tài khoản trung tâm cấu hình ở SePay.
            model.PaymentQrUrl = streamer.PaymentQrUrl;

            var (success, error) = await _streamerService.UpdateProfileConfigAsync(userId, model);
            if (!success)
            {
                model.Bio = ProfileBioSanitizer.Sanitize(model.Bio);
                ModelState.AddModelError(string.Empty, error);
                await PopulateConfigQrFieldsAsync(model, streamer);
                return View(model);
            }

            await RefreshUserCookieClaimsAsync(userId);
            TempData["SuccessMessage"] = "Cập nhật cấu hình trang donate thành công! Xem trang của bạn tại: /" + model.Slug;
            return RedirectToAction(nameof(Config));
        }

        /// <summary>
        /// Mở form đăng ký Streamer
        /// </summary>
        [Authorize(Roles = UserRoles.Viewer)]
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
                    model.Slug = user.Username.ToLower();
                    model.DisplayName = user.FullName;
                }
            }

            return View(model);
        }

        /// <summary>
        /// Xử lý đăng ký Streamer và gửi lệnh duyệt đến Quản trị viên
        /// </summary>
        [Authorize(Roles = UserRoles.Viewer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(12 * 1024 * 1024)]
        public async Task<IActionResult> Register(StreamerRegisterViewModel model)
        {
            int? currentUserId = null;
            User? currentUser = null;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
                currentUser = await _authService.GetUserByIdAsync(uid);
                ViewBag.PendingProfile = currentUser?.StreamerProfile;
            }

            if (!model.IdentityDataConsent)
            {
                ModelState.AddModelError(nameof(model.IdentityDataConsent), "Bạn cần đồng ý để nền tảng xử lý thông tin và ảnh CCCD nhằm xét duyệt hồ sơ streamer.");
            }

            if (!DateOnly.TryParseExact(model.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                ModelState.AddModelError(nameof(model.DateOfBirth), "Ngày sinh không hợp lệ.");
            if (!DateOnly.TryParseExact(model.IdentityIssuedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                ModelState.AddModelError(nameof(model.IdentityIssuedDate), "Ngày cấp CCCD không hợp lệ.");
            if (model.Gender is not ("Nam" or "Nữ" or "Khác"))
                ModelState.AddModelError(nameof(model.Gender), "Giới tính không hợp lệ.");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            const long maxIdentityImageBytes = 5L * 1024 * 1024;
            if (model.CitizenIdFrontImage!.Length > maxIdentityImageBytes)
                ModelState.AddModelError(nameof(model.CitizenIdFrontImage), "Ảnh mặt trước không được vượt quá 5 MB.");
            if (model.CitizenIdBackImage!.Length > maxIdentityImageBytes)
                ModelState.AddModelError(nameof(model.CitizenIdBackImage), "Ảnh mặt sau không được vượt quá 5 MB.");
            if (!ModelState.IsValid)
                return View(model);

            var identityData = new StreamerIdentityData
            {
                FullName = model.FullName.Trim(),
                DateOfBirth = model.DateOfBirth!.Trim(),
                CitizenId = model.CitizenId.Trim(),
                Gender = model.Gender.Trim(),
                Nationality = model.Nationality.Trim(),
                PlaceOfOrigin = model.PlaceOfOrigin.Trim(),
                PlaceOfBirth = model.PlaceOfBirth.Trim(),
                PermanentAddress = model.PermanentAddress!.Trim(),
                IdentityIssuedDate = model.IdentityIssuedDate!.Trim(),
                IdentityIssuedBy = model.IdentityIssuedBy.Trim(),
                CurrentAddress = model.CurrentAddress?.Trim()
            };
            var protectedIdentityData = _identityDocumentService.ProtectIdentity(identityData);

            string? frontDocumentKey = null;
            string? backDocumentKey = null;
            try
            {
                frontDocumentKey = await _identityDocumentService.SaveDocumentAsync(model.CitizenIdFrontImage!, HttpContext.RequestAborted);
                backDocumentKey = await _identityDocumentService.SaveDocumentAsync(model.CitizenIdBackImage!, HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                await _identityDocumentService.DeleteDocumentAsync(frontDocumentKey);
                await _identityDocumentService.DeleteDocumentAsync(backDocumentKey);
                throw;
            }
            catch (InvalidDataException exception)
            {
                await _identityDocumentService.DeleteDocumentAsync(frontDocumentKey);
                await _identityDocumentService.DeleteDocumentAsync(backDocumentKey);
                ModelState.AddModelError(string.Empty, exception.Message);
                return View(model);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await _identityDocumentService.DeleteDocumentAsync(frontDocumentKey);
                await _identityDocumentService.DeleteDocumentAsync(backDocumentKey);
                _logger.LogError(exception, "Failed to store private streamer identity documents");
                ModelState.AddModelError(string.Empty, "Không thể lưu ảnh CCCD lúc này. Vui lòng thử lại sau.");
                return View(model);
            }

            var identitySubmission = new StreamerIdentitySubmission(
                protectedIdentityData,
                frontDocumentKey,
                backDocumentKey,
                DateTime.UtcNow);

            var oldFrontKey = currentUser?.StreamerProfile?.CitizenIdFrontDocumentKey;
            var oldBackKey = currentUser?.StreamerProfile?.CitizenIdBackDocumentKey;
            (bool Success, string Error, StreamerProfile? Profile) result;
            try
            {
                result = await _streamerService.RegisterStreamerAsync(currentUserId, model, identitySubmission);
            }
            catch (Exception exception)
            {
                await _identityDocumentService.DeleteDocumentAsync(frontDocumentKey);
                await _identityDocumentService.DeleteDocumentAsync(backDocumentKey);
                _logger.LogError(exception, "Failed to submit streamer application for user {UserId}", currentUserId);
                ModelState.AddModelError(string.Empty, "Không thể gửi hồ sơ lúc này. Vui lòng thử lại sau.");
                return View(model);
            }

            if (!result.Success || result.Profile == null)
            {
                await _identityDocumentService.DeleteDocumentAsync(frontDocumentKey);
                await _identityDocumentService.DeleteDocumentAsync(backDocumentKey);
                ModelState.AddModelError(string.Empty, result.Error);
                return View(model);
            }

            if (!string.Equals(oldFrontKey, frontDocumentKey, StringComparison.Ordinal))
                await _identityDocumentService.DeleteDocumentAsync(oldFrontKey);
            if (!string.Equals(oldBackKey, backDocumentKey, StringComparison.Ordinal))
                await _identityDocumentService.DeleteDocumentAsync(oldBackKey);

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
