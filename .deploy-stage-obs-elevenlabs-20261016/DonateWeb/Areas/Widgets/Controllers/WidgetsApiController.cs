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
using System.Globalization;

namespace DonateWeb.Areas.Widgets.Controllers
{
    [ApiController]
    public class WidgetsApiController : ControllerBase
    {
        private readonly IWidgetService _widgetService;
        private readonly AppDbContext _context;
        private readonly IHubContext<PaymentHub> _hubContext;
        private readonly IElevenLabsTtsService? _elevenLabsTtsService;
        private readonly IDonationTtsAudioCache? _ttsAudioCache;
        private readonly ILogger<WidgetsApiController> _logger;

        public WidgetsApiController(
            IWidgetService widgetService,
            AppDbContext context,
            IHubContext<PaymentHub> hubContext,
            IElevenLabsTtsService? elevenLabsTtsService = null,
            IDonationTtsAudioCache? ttsAudioCache = null,
            ILogger<WidgetsApiController>? logger = null)
        {
            _widgetService = widgetService;
            _context = context;
            _hubContext = hubContext;
            _elevenLabsTtsService = elevenLabsTtsService;
            _ttsAudioCache = ttsAudioCache;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WidgetsApiController>.Instance;
        }

        /// <summary>
        /// Tạo audio cho một donate gần đây đã thanh toán thành công.
        /// OBS chỉ gửi ID giao dịch; nội dung được lấy lại từ CSDL và kiểm duyệt phía máy chủ.
        /// ID bằng 0 chỉ phát một câu thử cố định, không nhận văn bản tùy ý từ trình duyệt.
        /// </summary>
        [HttpGet("api/widgets/tts/audio/{slug}/{donationId:int}")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetDonationTtsAudio(
            string slug,
            int donationId,
            [FromHeader(Name = "X-Widget-Token")] string? widgetToken,
            CancellationToken cancellationToken)
        {
            var cleanSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cleanSlug) || donationId < 0 || string.IsNullOrWhiteSpace(widgetToken))
                return Unauthorized();

            var config = await _widgetService.GetAlertBoxConfigAsync(cleanSlug, string.Empty);
            if (config.StreamerProfileId <= 0 || !config.IsTtsEnabled ||
                !FixedTimeEquals(widgetToken, config.WidgetToken))
            {
                return Unauthorized();
            }

            if (_elevenLabsTtsService?.IsConfigured != true || _ttsAudioCache is null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "ElevenLabs chưa được cấu hình." });
            }

            Response.Headers.CacheControl = "private, no-store";
            var cacheKey = $"{config.StreamerProfileId}:{donationId}";
            if (_ttsAudioCache.TryGet(cacheKey, out var cachedAudio))
                return File(cachedAudio, "audio/mpeg");

            string spokenText;
            if (donationId == 0)
            {
                // Câu cố định để nút thử Alert cũng có thể kiểm tra ElevenLabs.
                // Giữ câu thử nghiệm trùng với lời nhắn đang hiển thị trong AlertBox.
                spokenText = "Khán giả thử nghiệm ủng hộ 50.000 đồng. Lời nhắn: Chào streamer! Chúc kênh của bạn luôn phát triển và có thật nhiều niềm vui nhé!";
            }
            else
            {
                // Chỉ cho phép phát giao dịch thành công gần đây để token của widget không thể dùng để đọc lại cả lịch sử.
                var recentCutoff = DateTime.UtcNow.AddMinutes(-30);
                var donation = await _context.Donations.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == donationId &&
                        d.StreamerProfileId == config.StreamerProfileId &&
                        d.Status == DonationStatus.Success &&
                        d.Amount >= config.MinAmountToAlert &&
                        d.CreatedAt >= recentCutoff,
                        cancellationToken);

                if (donation is null)
                    return NotFound();

                var alert = await _widgetService.CreateDonationAlertAsync(donation, cleanSlug);
                if (alert is null || !alert.IsTtsEnabled)
                    return NotFound();

                var donorName = LimitSpeechText(alert.DonorName, 100);
                var message = LimitSpeechText(alert.Message, 220);
                if (string.IsNullOrWhiteSpace(donorName))
                    donorName = "Người ủng hộ ẩn danh";
                if (string.IsNullOrWhiteSpace(message))
                    message = "Đã gửi ủng hộ";

                spokenText = $"{donorName} ủng hộ {donation.Amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đồng. Lời nhắn: {message}";
            }

            try
            {
                var audio = await _elevenLabsTtsService.SynthesizeAsync(spokenText, cancellationToken);
                _ttsAudioCache.Set(cacheKey, audio);
                return File(audio, "audio/mpeg");
            }
            catch (HttpRequestException exception)
            {
                // Không trả nội dung lỗi của ElevenLabs cho trình duyệt để tránh lộ thông tin cấu hình.
                _logger.LogWarning(exception, "Không tạo được audio ElevenLabs cho streamer {StreamerId}.", config.StreamerProfileId);
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Không tạo được giọng đọc ElevenLabs." });
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(StatusCodes.Status504GatewayTimeout, new { message = "ElevenLabs phản hồi quá lâu." });
            }
        }

        private static string LimitSpeechText(string? value, int maxLength)
        {
            var cleanValue = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return cleanValue.Length <= maxLength ? cleanValue : cleanValue[..maxLength];
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
