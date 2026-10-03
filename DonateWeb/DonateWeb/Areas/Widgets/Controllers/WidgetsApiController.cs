using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Areas.Widgets.Services;
using System.Collections.Concurrent;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [ApiController]
    public class WidgetsApiController : ControllerBase
    {
        private readonly IWidgetService _widgetService;
        private readonly ILogger<WidgetsApiController> _logger;
        private readonly AppDbContext _context;
        private readonly IWindowsSpeechService _windowsSpeechService;
        private static readonly ConcurrentDictionary<string, DateTimeOffset> LastSpeechRequestByToken = new();

        public WidgetsApiController(
            IWidgetService widgetService,
            ILogger<WidgetsApiController> logger,
            AppDbContext context,
            IWindowsSpeechService windowsSpeechService)
        {
            _widgetService = widgetService;
            _logger = logger;
            _context = context;
            _windowsSpeechService = windowsSpeechService;
        }

        [HttpPost("api/widgets/alertbox/tts/{slug}")]
        [RequestSizeLimit(4096)]
        public async Task<IActionResult> SynthesizeAlertSpeech(string slug, [FromBody] AlertSpeechRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 500 || string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest();
            }

            var cleanSlug = slug.Trim().ToLowerInvariant();
            var config = await _widgetService.GetAlertBoxConfigAsync(cleanSlug, "");
            if (!FixedTimeEquals(request.Token, config.WidgetToken))
            {
                return NotFound();
            }

            var now = DateTimeOffset.UtcNow;
            if (LastSpeechRequestByToken.TryGetValue(request.Token, out var lastRequest) && now - lastRequest < TimeSpan.FromMilliseconds(750))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }
            LastSpeechRequestByToken[request.Token] = now;

            try
            {
                var wave = await _windowsSpeechService.SynthesizeVietnameseAsync(request.Text.Trim(), cancellationToken);
                Response.Headers.CacheControl = "no-store";
                return File(wave, "audio/wav");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Windows Vietnamese TTS is unavailable for widget {StreamerSlug}.", cleanSlug);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (PlatformNotSupportedException ex)
            {
                _logger.LogWarning(ex, "Windows TTS was requested on an unsupported host for widget {StreamerSlug}.", cleanSlug);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        private static bool FixedTimeEquals(string provided, string expected)
        {
            var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
            return providedBytes.Length == expectedBytes.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }

        public sealed class AlertSpeechRequest
        {
            public string Token { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
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
