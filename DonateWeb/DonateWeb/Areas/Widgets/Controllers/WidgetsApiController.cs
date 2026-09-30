using Microsoft.AspNetCore.Mvc;
using DonateWeb.Areas.Widgets.Services;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [ApiController]
    public class WidgetsApiController : ControllerBase
    {
        private readonly IWidgetService _widgetService;
        private readonly ILogger<WidgetsApiController> _logger;

        public WidgetsApiController(IWidgetService widgetService, ILogger<WidgetsApiController> logger)
        {
            _widgetService = widgetService;
            _logger = logger;
        }

        /// <summary>
        /// REST API Polling thông báo donate mới hoặc thông báo test cho OBS Studio Alert Box
        /// GET api/widgets/alertbox/poll/{slug}?lastId=123
        /// </summary>
        [HttpGet("api/widgets/alertbox/poll/{slug}")]
        public async Task<IActionResult> PollAlert(string slug, [FromQuery] int? lastId)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            var (hasAlert, alert) = await _widgetService.PollAlertAsync(targetSlug, lastId ?? 0);

            return Ok(new
            {
                success = true,
                hasAlert,
                data = alert
            });
        }

        /// <summary>
        /// Kích hoạt một thông báo Alert thử nghiệm lên OBS Browser Source
        /// POST api/widgets/alertbox/test/{slug}
        /// </summary>
        [HttpPost("api/widgets/alertbox/test/{slug}")]
        public async Task<IActionResult> TriggerTestAlert(string slug)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            await _widgetService.TriggerTestAlertAsync(targetSlug);

            return Ok(new
            {
                success = true,
                message = "Đã gửi thông báo thử nghiệm thành công lên OBS!"
            });
        }

        /// <summary>
        /// Lấy tiến độ mục tiêu quyên góp đang kích hoạt của streamer
        /// GET api/widgets/goal/{slug}
        /// </summary>
        [HttpGet("api/widgets/goal/{slug}")]
        public async Task<IActionResult> GetActiveGoal(string slug)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            var activeGoal = await _widgetService.GetActiveGoalAsync(targetSlug);

            return Ok(new
            {
                success = true,
                hasActiveGoal = activeGoal != null,
                data = activeGoal != null ? new
                {
                    id = activeGoal.Id,
                    title = activeGoal.Title,
                    targetAmount = activeGoal.TargetAmount,
                    startingAmount = activeGoal.StartingAmount,
                    currentAmount = activeGoal.CurrentAmount,
                    progressPercentage = activeGoal.ProgressPercentage,
                    formattedProgress = activeGoal.FormattedProgress,
                    fullGoalDisplay = activeGoal.FullGoalDisplay,
                    progressBarColor = activeGoal.ProgressBarColor
                } : null
            });
        }

        /// <summary>
        /// Lấy danh sách Top Donate theo chu kỳ thời gian (day/week/month/all)
        /// GET api/widgets/leaderboard/{slug}?period=month&limit=5
        /// </summary>
        [HttpGet("api/widgets/leaderboard/{slug}")]
        public async Task<IActionResult> GetLeaderboard(string slug, [FromQuery] string? period, [FromQuery] int? limit)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            var donors = await _widgetService.GetLeaderboardDonorsAsync(targetSlug, period ?? "all", limit ?? 5);

            return Ok(new
            {
                success = true,
                data = donors.Select(d => new
                {
                    rank = d.Rank,
                    donorName = d.DonorName,
                    donationCount = d.DonationCount,
                    totalAmount = d.TotalAmount,
                    formattedAmount = d.FormattedAmount
                })
            });
        }

        /// <summary>
        /// Lấy danh sách các khoản quyên góp mới nhất
        /// GET api/widgets/recent-donations/{slug}?limit=10
        /// </summary>
        [HttpGet("api/widgets/recent-donations/{slug}")]
        public async Task<IActionResult> GetRecentDonations(string slug, [FromQuery] int? limit)
        {
            var targetSlug = string.IsNullOrWhiteSpace(slug) ? "tenstreamer" : slug.Trim().ToLower();
            var donations = await _widgetService.GetRecentDonationsAsync(targetSlug, limit ?? 10);

            return Ok(new
            {
                success = true,
                data = donations.Select(d => new
                {
                    id = d.Id,
                    donorName = d.DonorName,
                    amount = d.Amount,
                    formattedAmount = d.FormattedAmount,
                    message = d.Message,
                    timeAgo = d.TimeAgo
                })
            });
        }
    }
}
