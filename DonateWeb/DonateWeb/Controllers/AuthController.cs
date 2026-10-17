using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Services;
using DonateWeb.ViewModels.Auth;

namespace DonateWeb.Controllers
{
    public class AuthController : Controller
    {
        private readonly IAuthService _authService;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthService authService,
            IJwtTokenService jwtTokenService,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _authService = authService;
            _jwtTokenService = jwtTokenService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToLocal(returnUrl);
            }

            var model = new LoginViewModel { ReturnUrl = returnUrl };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DonateRateLimit(5, 60, "login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, error, user, roles) = await _authService.LoginAsync(model.UsernameOrEmail, model.Password);

            if (!success || user == null)
            {
                ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            // Thiết lập Claims cho Authentication Cookie & Session
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

            if (user.StreamerProfile != null && user.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
            {
                claims.Add(new Claim("StreamerSlug", user.StreamerProfile.Slug));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            _logger.LogInformation("Người dùng {Username} đã đăng nhập thành công với vai trò: {Roles}.", user.Username, string.Join(", ", roles));

            return RedirectToLocal(model.ReturnUrl);
        }

        [HttpGet]
        public IActionResult Register(string? role = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            var model = new RegisterViewModel
            {
                RoleType = role == "streamer" ? UserRoles.Streamer : UserRoles.Viewer
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, error, user) = await _authService.RegisterAsync(model);

            if (!success || user == null)
            {
                ModelState.AddModelError(string.Empty, error);
                return View(model);
            }

            TempData["SuccessMessage"] = "Đăng ký tài khoản thành công! Vui lòng đăng nhập.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // ==================== Đăng nhập OAuth bên thứ ba ====================

        [HttpGet]
        public IActionResult ExternalLogin(string provider, string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToLocal(returnUrl);
            }

            var providerName = provider?.Trim().ToLowerInvariant() switch
            {
                "google" => "Google",
                "facebook" => "Facebook",
                _ => null
            };

            if (providerName == null)
            {
                TempData["ErrorMessage"] = "DonateWeb hiện hỗ trợ đăng nhập bằng Google và Facebook.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var (clientId, clientSecret, requiredSettings) = providerName switch
            {
                "Google" => (
                    _configuration["Authentication:Google:ClientId"],
                    _configuration["Authentication:Google:ClientSecret"],
                    "GOOGLE_CLIENT_ID và GOOGLE_CLIENT_SECRET"),
                _ => (
                    _configuration["Authentication:Facebook:AppId"],
                    _configuration["Authentication:Facebook:AppSecret"],
                    "FACEBOOK_APP_ID và FACEBOOK_APP_SECRET")
            };

            var isConfigured = IsValidConfig(clientId) && IsValidConfig(clientSecret);

            if (!isConfigured)
            {
                TempData["ErrorMessage"] = $"Đăng nhập {providerName} chưa được cấu hình. Cần đặt {requiredSettings} trong file .env của VPS.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Auth", new { returnUrl });
            var properties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl,
                Items =
                {
                    { "LoginProvider", providerName },
                    { "returnUrl", returnUrl ?? string.Empty }
                }
            };

            return Challenge(properties, providerName);
        }

        [HttpGet]
        public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                await HttpContext.SignOutAsync("ExternalCookie");
                TempData["ErrorMessage"] = $"Lỗi xác thực từ bên thứ 3: {remoteError}";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            // Chỉ chấp nhận danh tính do nhà cung cấp vừa xác thực trong cookie OAuth tạm thời.
            var authenticateResult = await HttpContext.AuthenticateAsync("ExternalCookie");
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                TempData["ErrorMessage"] = "Không thể lấy thông tin đăng nhập từ bên thứ 3. Vui lòng thử lại.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var externalClaims = authenticateResult.Principal;
            var provider = authenticateResult.Properties?.Items.TryGetValue("LoginProvider", out var loginProvider) == true && !string.IsNullOrEmpty(loginProvider)
                ? loginProvider
                : (externalClaims.Identity?.AuthenticationType ?? "External");

            var providerKey = externalClaims.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? externalClaims.FindFirstValue("sub");

            var email = externalClaims.FindFirstValue(ClaimTypes.Email);
            // Google OAuth v2 trả verified_email; OIDC có thể trả email_verified.
            var emailVerifiedClaim = externalClaims.FindFirstValue("urn:google:email_verified_oidc")
                                    ?? externalClaims.FindFirstValue("urn:google:email_verified")
                                    ?? externalClaims.FindFirstValue("email_verified");
            var googleEmailVerified = bool.TryParse(emailVerifiedClaim, out var verified) && verified;
            var displayName = externalClaims.FindFirstValue(ClaimTypes.Name)
                              ?? externalClaims.FindFirstValue(ClaimTypes.GivenName);
            var avatarUrl = externalClaims.FindFirstValue("urn:google:picture")
                            ?? externalClaims.FindFirstValue("urn:facebook:picture")
                            ?? externalClaims.FindFirstValue("picture");

            // Xóa cookie tạm của phiên OAuth bên ngoài
            await HttpContext.SignOutAsync("ExternalCookie");

            if ((!string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(provider, "Facebook", StringComparison.OrdinalIgnoreCase)) ||
                string.IsNullOrWhiteSpace(providerKey))
            {
                TempData["ErrorMessage"] = "Nhà cung cấp không trả về mã tài khoản hợp lệ. Vui lòng thử đăng nhập lại.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = string.Equals(provider, "Facebook", StringComparison.OrdinalIgnoreCase)
                    ? "Facebook không cung cấp email. Hãy cho phép chia sẻ email trong Facebook Login hoặc đăng nhập bằng Google/tài khoản DonateWeb."
                    : "Google không cung cấp email. Vui lòng thử lại hoặc dùng tài khoản DonateWeb.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            if (string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase) && !googleEmailVerified)
            {
                TempData["ErrorMessage"] = "Tài khoản Google cần có địa chỉ email đã xác minh để đăng nhập DonateWeb.";
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            var (success, error, user, roles) = await _authService.ProcessExternalLoginAsync(
                provider,
                providerKey,
                email,
                displayName,
                avatarUrl);

            if (!success || user == null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Login), new { returnUrl });
            }

            // Đăng nhập vào Cookie Scheme chính thức của hệ thống
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName ?? user.Username),
                new Claim("AvatarUrl", user.AvatarUrl ?? "/images/default-avatar.png"),
                new Claim("WalletBalance", user.WalletBalance.ToString("N0")),
                new Claim("AccountId", user.AccountId ?? ""),
                new Claim("AuthProvider", provider)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (user.StreamerProfile != null && user.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
            {
                claims.Add(new Claim("StreamerSlug", user.StreamerProfile.Slug));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            _logger.LogInformation("Người dùng {Username} đã đăng nhập qua {Provider} thành công.", user.Username, provider);

            return RedirectToLocal(returnUrl);
        }

        private static bool IsValidConfig(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !value.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) &&
                   !value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
