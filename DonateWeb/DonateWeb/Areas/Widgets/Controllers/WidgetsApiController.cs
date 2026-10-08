using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Security.ContentModeration;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [ApiController]
    public class WidgetsApiController : ControllerBase
    {
        private readonly IWidgetService _widgetService;
        private readonly ILogger<WidgetsApiController> _logger;
        private readonly AppDbContext _context;
        private readonly IContentModerationService _moderationService;

        public WidgetsApiController(
            IWidgetService widgetService,
            ILogger<WidgetsApiController> logger,
            AppDbContext context,
            IContentModerationService moderationService)
        {
            _widgetService = widgetService;
            _logger = logger;
            _context = context;
            _moderationService = moderationService;
        }

        [HttpGet("api/widgets/tts/alerts/{slug}")]
        public async Task<IActionResult> GetTtsAlerts(string slug, [FromQuery] int? afterId, CancellationToken cancellationToken)
        {
            var cleanSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
            var providedToken = Request.Headers["X-Widget-Token"].ToString();
            var configRow = await (
                from config in _context.AlertBoxConfigs.AsNoTracking()
                join profile in _context.StreamerProfiles.AsNoTracking() on config.StreamerProfileId equals profile.Id
                where profile.Slug.ToLower() == cleanSlug
                select new { config.WidgetToken, config.IsTtsEnabled, config.MinAmountToAlert })
                .FirstOrDefaultAsync(cancellationToken);

            if (configRow == null || !FixedTimeEquals(providedToken, configRow.WidgetToken))
                return Unauthorized();

            var profileId = await _context.StreamerProfiles.AsNoTracking()
                .Where(profile => profile.Slug.ToLower() == cleanSlug)
                .Select(profile => profile.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (profileId == 0) return NotFound();

            var query = _context.Donations.AsNoTracking()
                .Where(donation => donation.StreamerProfileId == profileId && donation.Status == DonationStatus.Success);
            if (!afterId.HasValue)
            {
                var latest = await query.Select(donation => (int?)donation.Id).MaxAsync(cancellationToken) ?? 0;
                return Ok(new { cursor = latest, alerts = Array.Empty<TtsDonationAlertDto>() });
            }

            var donations = await query.Where(donation => donation.Id > afterId.Value)
                .OrderBy(donation => donation.Id).Take(50).ToListAsync(cancellationToken);
            var alerts = new List<TtsDonationAlertDto>();
            if (configRow.IsTtsEnabled)
            {
                foreach (var donation in donations)
                {
                    var alert = DonationAlertFactory.Create(donation, new AlertBoxConfigViewModel
                    {
                        IsTtsEnabled = true,
                        MinAmountToAlert = configRow.MinAmountToAlert
                    }, _moderationService);
                    if (alert == null) continue;
                    alerts.Add(new TtsDonationAlertDto
                    {
                        DonationId = donation.Id,
                        DonorName = alert.DonorName,
                        Amount = alert.Amount,
                        Message = alert.Message ?? string.Empty
                    });
                }
            }

            Response.Headers.CacheControl = "no-store";
            return Ok(new { cursor = donations.Count > 0 ? donations[^1].Id : afterId.Value, alerts });
        }

        private static bool FixedTimeEquals(string provided, string expected)
        {
            var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
            return providedBytes.Length == expectedBytes.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
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
