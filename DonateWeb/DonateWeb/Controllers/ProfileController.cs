using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Security.Uploads;
using DonateWeb.ViewModels;

namespace DonateWeb.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProfileController(AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // Nếu là tài khoản Streamer, hợp nhất trang Thông tin cá nhân và Trang công khai thành 1 trang duy nhất tại /{slug}
            if (user.StreamerProfile != null && !string.IsNullOrWhiteSpace(user.StreamerProfile.Slug))
            {
                return Redirect("/" + user.StreamerProfile.Slug);
            }

            var viewModel = new UserProfileViewModel
            {
                AccountId = user.AccountId,
                Email = user.Email,
                FullName = user.FullName,
                DisplayName = user.StreamerProfile?.DisplayName ?? user.Username,
                AvatarUrl = user.AvatarUrl,
                CoverUrl = user.StreamerProfile?.BannerUrl ?? "/images/default-banner.jpg",
                PhoneNumber = user.PhoneNumber ?? "",
                PaymentQrUrl = user.StreamerProfile?.PaymentQrUrl ?? "",
                Bio = user.StreamerProfile?.Bio ?? "",
                Category = user.StreamerProfile != null ? "Streamer / Content Creator" : "Viewer / Donor"
            };

            ViewBag.UserEntity = user;
            ViewBag.IsStreamer = user.StreamerProfile != null;
            ViewBag.StreamerSlug = user.StreamerProfile?.Slug;

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(UserProfileViewModel model, IFormFile? avatarFile)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            var fileToUpload = avatarFile ?? model.AvatarFile;
            var (uploadedAvatarPath, uploadError) = await AvatarUploadService.SaveAsync(
                fileToUpload,
                _webHostEnvironment.WebRootPath,
                userId,
                HttpContext.RequestAborted);
            if (uploadError != null)
            {
                TempData["ErrorMessage"] = uploadError;
                return RedirectToAction(nameof(Index));
            }

            user.FullName = model.FullName?.Trim() ?? user.FullName;
            user.PhoneNumber = model.PhoneNumber?.Trim();
            if (uploadedAvatarPath != null)
            {
                user.AvatarUrl = uploadedAvatarPath;
            }

            if (user.StreamerProfile != null)
            {
                user.StreamerProfile.DisplayName = model.DisplayName?.Trim() ?? user.StreamerProfile.DisplayName;
                user.StreamerProfile.Bio = model.Bio?.Trim();
                user.StreamerProfile.AvatarUrl = user.AvatarUrl;
                user.StreamerProfile.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrWhiteSpace(model.CoverUrl))
                {
                    user.StreamerProfile.BannerUrl = model.CoverUrl.Trim();
                }
                if (!string.IsNullOrWhiteSpace(model.PaymentQrUrl))
                {
                    user.StreamerProfile.PaymentQrUrl = model.PaymentQrUrl.Trim();
                }
            }

            await _context.SaveChangesAsync();

            // Cập nhật lại Cookie Claims để hiển thị ảnh đại diện mới ngay lập tức trên Header
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
            foreach (var ur in user.UserRoles)
            {
                if (ur.Role != null) claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));
            }
            if (user.StreamerProfile != null)
            {
                claims.Add(new Claim("StreamerSlug", user.StreamerProfile.Slug));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            TempData["SuccessMessage"] = "Cập nhật thông tin cá nhân và ảnh đại diện thành công!";

            return RedirectToAction(nameof(Index));
        }
    }
}
