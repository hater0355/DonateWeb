using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;

namespace DonateWeb.Areas.Widgets.Controllers
{
[Area("Widgets")]
[Authorize(Roles = UserRoles.Streamer)]
[Route("Widgets/Config")]
    [Route("Widgets/WidgetsConfig")]
    public class WidgetsConfigController : Controller
    {
        private readonly IWidgetService _widgetService;
        private readonly ILogger<WidgetsConfigController> _logger;
        private readonly AppDbContext _context;

        public WidgetsConfigController(IWidgetService widgetService, ILogger<WidgetsConfigController> logger, AppDbContext context)
        {
            _widgetService = widgetService;
            _logger = logger;
            _context = context;
        }

        private async Task<string?> GetOwnedStreamerSlugAsync(string? requestedSlug = null)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }

            var slug = await _context.StreamerProfiles
                .Where(profile => profile.UserId == userId)
                .Select(profile => profile.Slug)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(slug) ||
                (!string.IsNullOrWhiteSpace(requestedSlug) &&
                 !string.Equals(slug, requestedSlug.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return slug.Trim().ToLowerInvariant();
        }

        private string GetBaseUrl()
        {
            return $"{Request.Scheme}://{Request.Host}";
        }

        // ====================================================================
        // 1. ALERT BOX CONFIG
        // ====================================================================

        [HttpGet("")]
        [HttpGet("AlertBox")]
        public async Task<IActionResult> AlertBox(string? slug)
        {
            var targetSlug = await GetOwnedStreamerSlugAsync(slug);
            if (targetSlug == null) return Forbid();
            var model = await _widgetService.GetAlertBoxConfigAsync(targetSlug, GetBaseUrl());

            if (TempData["SuccessMessage"] != null)
            {
                model.SuccessMessage = TempData["SuccessMessage"]?.ToString();
            }

            return View(model);
        }

        [HttpPost("AlertBox")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlertBox(AlertBoxConfigViewModel model)
        {
            var ownedSlug = await GetOwnedStreamerSlugAsync(model.StreamerSlug);
            if (ownedSlug == null) return Forbid();
            model.StreamerSlug = ownedSlug;

            model.ObsOverlayUrl = $"{GetBaseUrl()}/Widgets/Overlay/AlertBox/{model.StreamerSlug}";

            if (!ModelState.IsValid)
            {
                model.ErrorMessage = "Vui lòng kiểm tra lại các thông số cấu hình.";
                return View(model);
            }

            await _widgetService.SaveAlertBoxConfigAsync(model);
            model.SuccessMessage = "Lưu cấu hình Alert Box thành công! Bạn có thể xem thử hoặc kiểm tra trên OBS.";
            return View(model);
        }

        [HttpPost("ResetAlertBox")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetAlertBox()
        {
            var slug = await GetOwnedStreamerSlugAsync();
            if (slug == null) return Forbid();
            await _widgetService.ResetAlertBoxConfigAsync(slug);
            TempData["SuccessMessage"] = "Đã khôi phục cài đặt Alert Box về mặc định!";
            return RedirectToAction(nameof(AlertBox));
        }

        [HttpPost("TriggerTestAlert")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerTestAlert()
        {
            var slug = await GetOwnedStreamerSlugAsync();
            if (slug == null) return Forbid();
            await _widgetService.TriggerTestAlertAsync(slug);
            return Json(new { success = true, message = "Đã phát thông báo thử nghiệm lên OBS thành công!" });
        }

        // ====================================================================
        // 2. GOALS MANAGEMENT
        // ====================================================================

        [HttpGet("Goals")]
        public async Task<IActionResult> Goals(string? slug)
        {
            var targetSlug = await GetOwnedStreamerSlugAsync(slug);
            if (targetSlug == null) return Forbid();
            var model = await _widgetService.GetGoalManagementAsync(targetSlug, GetBaseUrl());

            if (TempData["SuccessMessage"] != null)
            {
                model.SuccessMessage = TempData["SuccessMessage"]?.ToString();
            }
            if (TempData["ErrorMessage"] != null)
            {
                model.ErrorMessage = TempData["ErrorMessage"]?.ToString();
            }

            return View(model);
        }

        [HttpPost("SaveGoal")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGoal(GoalManagementViewModel model)
        {
            var slug = await GetOwnedStreamerSlugAsync();
            if (slug == null) return Forbid();

            if (model.NewGoal == null || string.IsNullOrWhiteSpace(model.NewGoal.Title))
            {
                TempData["ErrorMessage"] = "Tiêu đề mục tiêu không được để trống!";
                return RedirectToAction(nameof(Goals));
            }

            await _widgetService.SaveGoalAsync(slug, model.NewGoal);
            TempData["SuccessMessage"] = "Lưu mục tiêu Donate thành công!";
            return RedirectToAction(nameof(Goals));
        }

        [HttpPost("ToggleGoal")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleGoal(int id)
        {
            var slug = await GetOwnedStreamerSlugAsync();
            if (slug == null) return Forbid();
            var success = await _widgetService.ToggleGoalAsync(slug, id);
            if (success)
            {
                TempData["SuccessMessage"] = "Cập nhật trạng thái mục tiêu thành công!";
            }
            return RedirectToAction(nameof(Goals));
        }

        [HttpPost("DeleteGoal")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGoal(int id)
        {
            var slug = await GetOwnedStreamerSlugAsync();
            if (slug == null) return Forbid();
            var success = await _widgetService.DeleteGoalAsync(slug, id);
            if (success)
            {
                TempData["SuccessMessage"] = "Đã xóa mục tiêu Donate thành công!";
            }
            return RedirectToAction(nameof(Goals));
        }

        // ====================================================================
        // 3. LEADERBOARD CONFIG & PREVIEW
        // ====================================================================

        [HttpGet("Leaderboard")]
        public async Task<IActionResult> Leaderboard(string? period, string? slug)
        {
            var targetSlug = await GetOwnedStreamerSlugAsync(slug);
            if (targetSlug == null) return Forbid();
            var model = await _widgetService.GetLeaderboardViewModelAsync(targetSlug, period ?? "all", GetBaseUrl());
            return View(model);
        }
    }
}
