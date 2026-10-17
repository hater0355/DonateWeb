using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [ApiController]
    public class WidgetsApiController : ControllerBase
    {
        private readonly IWidgetService _widgetService;
        private readonly AppDbContext _context;
        private readonly IHubContext<PaymentHub> _hubContext;

        public WidgetsApiController(
            IWidgetService widgetService,
            AppDbContext context,
            IHubContext<PaymentHub> hubContext)
        {
            _widgetService = widgetService;
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet("api/widgets/tts/alerts/{slug}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetTtsAlerts(
            string slug,
            [FromHeader(Name = "X-Widget-Token")] string? widgetToken,
            [FromQuery] int? afterId,
            CancellationToken cancellationToken)
        {
            var cleanSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(widgetToken) || afterId < 0)
                return Unauthorized();

            var config = await _widgetService.GetAlertBoxConfigAsync(cleanSlug, string.Empty);
            if (!FixedTimeEquals(widgetToken, config.WidgetToken))
                return Unauthorized();

            var baseQuery = _context.Donations.AsNoTracking()
                .Where(d => d.StreamerProfileId == config.StreamerProfileId && d.Status == DonationStatus.Success);
            var latestId = await baseQuery.MaxAsync(d => (int?)d.Id, cancellationToken) ?? 0;
            if (!afterId.HasValue || !config.IsTtsEnabled)
                return Ok(new TtsCatchupResultDto { Cursor = latestId });

            var donations = await baseQuery
                .Where(d => d.Id > afterId.Value && d.Amount >= config.MinAmountToAlert)
                .OrderBy(d => d.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            var result = new TtsCatchupResultDto
            {
                Cursor = donations.Count == 100 ? donations[^1].Id : latestId
            };
            foreach (var donation in donations)
            {
                var alert = await _widgetService.CreateDonationAlertAsync(donation, cleanSlug);
                if (alert is not null && alert.IsTtsEnabled)
                {
                    result.Alerts.Add(ToTtsAlert(alert));
                }
            }

            return Ok(result);
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
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost("api/widgets/alertbox/test/{slug}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerTestAlert(string slug)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Forbid();
            }

            var ownedSlug = await _context.StreamerProfiles
                .Where(profile => profile.UserId == userId)
                .Select(profile => profile.Slug)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(ownedSlug) ||
                !string.Equals(ownedSlug, slug.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            await _widgetService.TriggerTestAlertAsync(ownedSlug.Trim().ToLowerInvariant());
            var config = await _widgetService.GetAlertBoxConfigAsync(ownedSlug, string.Empty);
            if (config.IsTtsEnabled)
            {
                await _hubContext.Clients.Group("Tts_" + ownedSlug.Trim().ToLowerInvariant())
                    .SendAsync("ReceiveTtsTest", new TtsDonationAlertDto
                    {
                        DonorName = "Khán giả thử nghiệm",
                        Amount = 50000m,
                        Message = "Đây là lời nhắn thử giọng đọc donate."
                    });
            }

            return Ok(new
            {
                success = true,
                message = "Đã gửi thông báo thử nghiệm thành công lên OBS!"
            });
        }

        private static TtsDonationAlertDto ToTtsAlert(AlertPollResultDto alert) => new()
        {
            DonationId = alert.DonationId,
            DonorName = alert.DonorName,
            Amount = alert.Amount,
            Message = alert.Message ?? string.Empty
        };

        private static bool FixedTimeEquals(string provided, string expected)
        {
            var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
            return providedBytes.Length == expectedBytes.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
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
