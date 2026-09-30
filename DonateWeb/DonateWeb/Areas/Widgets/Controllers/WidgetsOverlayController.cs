using Microsoft.AspNetCore.Mvc;
using DonateWeb.Areas.Widgets.Services;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [Area("Widgets")]
    [Route("Widgets/Overlay")]
    [Route("Widgets/WidgetsOverlay")]
    public class WidgetsOverlayController : Controller
    {
        private readonly IWidgetService _widgetService;
        private readonly ILogger<WidgetsOverlayController> _logger;

        public WidgetsOverlayController(IWidgetService widgetService, ILogger<WidgetsOverlayController> logger)
        {
            _widgetService = widgetService;
            _logger = logger;
        }

        private string GetBaseUrl()
        {
            return $"{Request.Scheme}://{Request.Host}";
        }

        /// <summary>
        /// Màn hình Overlay Alert Box nhúng vào Browser Source của OBS Studio
        /// URL: /Widgets/Overlay/AlertBox/{slug} hoặc /Widgets/WidgetsOverlay/AlertBox?slug={slug}
        /// </summary>
        [HttpGet("AlertBox/{slug?}")]
        public async Task<IActionResult> AlertBox(string? slug, [FromQuery] string? key)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            ViewBag.StreamerSlug = targetSlug;
            ViewBag.WidgetKey = key ?? "";

            var model = await _widgetService.GetAlertBoxConfigAsync(targetSlug, GetBaseUrl());
            return View(model);
        }

        /// <summary>
        /// Màn hình Overlay Goal (Mục tiêu quyên góp) nhúng vào OBS
        /// URL: /Widgets/Overlay/Goal/{slug} hoặc /Widgets/WidgetsOverlay/Goal?slug={slug}
        /// </summary>
        [HttpGet("Goal/{slug?}")]
        public async Task<IActionResult> Goal(string? slug, [FromQuery] string? key)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            ViewBag.StreamerSlug = targetSlug;
            ViewBag.WidgetKey = key ?? "";

            var activeGoal = await _widgetService.GetActiveGoalAsync(targetSlug);
            return View(activeGoal);
        }

        /// <summary>
        /// Màn hình Overlay Bảng xếp hạng vinh danh Top Donate nhúng vào OBS
        /// URL: /Widgets/Overlay/Leaderboard/{slug} hoặc /Widgets/WidgetsOverlay/Leaderboard?slug={slug}
        /// </summary>
        [HttpGet("Leaderboard/{slug?}")]
        public async Task<IActionResult> Leaderboard(string? slug, [FromQuery] string? period)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            var targetPeriod = string.IsNullOrWhiteSpace(period) ? "all" : period.Trim().ToLower();

            ViewBag.StreamerSlug = targetSlug;
            ViewBag.Period = targetPeriod;

            var topDonors = await _widgetService.GetLeaderboardDonorsAsync(targetSlug, targetPeriod, 5);
            return View(topDonors);
        }

        /// <summary>
        /// Màn hình Overlay Danh sách Tin nhắn & Khoản donate mới nhất nhúng vào OBS
        /// URL: /Widgets/Overlay/RecentMessages/{slug} hoặc /Widgets/WidgetsOverlay/RecentMessages?slug={slug}
        /// </summary>
        [HttpGet("RecentMessages/{slug?}")]
        public async Task<IActionResult> RecentMessages(string? slug, [FromQuery] int? limit)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            ViewBag.StreamerSlug = targetSlug;

            var recentDonations = await _widgetService.GetRecentDonationsAsync(targetSlug, limit ?? 5);
            return View(recentDonations);
        }
    }
}
