using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using DonateWeb.Security.ContentModeration;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Security.Webhook;

namespace DonateWeb.Security.Controllers
{
    /// <summary>
    /// API HỖ TRỢ KIỂM THỬ NHANH CÁC TÍNH NĂNG BẢO MẬT & KIỂM DUYỆT (TESTING & DEMO)
    /// Thư mục riêng: Security/Controllers/
    /// Cung cấp các endpoint để tester/developer dễ dàng thử nghiệm:
    /// 1. Kiểm duyệt lời nhắn (Content Moderation Test): Thử lọc từ tục tĩu, kỳ thị, link độc hại
    /// 2. Thử nghiệm Rate Limiting theo IP
    /// 3. Sinh chữ ký HMAC-SHA256 phục vụ test gọi Webhook
    /// </summary>
    [ApiController]
    [Route("api/security/test")]
    public class SecurityTestApiController : ControllerBase
    {
        private readonly IContentModerationService _moderationService;
        private readonly IIpRateLimiterService _rateLimiter;
        private readonly IWebhookSecurityService _webhookSecurity;
        private readonly IWebHostEnvironment _environment;

        public SecurityTestApiController(
            IContentModerationService moderationService,
            IIpRateLimiterService rateLimiter,
            IWebhookSecurityService webhookSecurity,
            IWebHostEnvironment environment)
        {
            _moderationService = moderationService;
            _rateLimiter = rateLimiter;
            _webhookSecurity = webhookSecurity;
            _environment = environment;
        }

        /// <summary>
        /// Test kiểm duyệt lời nhắn: Gửi vào văn bản bất kỳ để xem kết quả phân tích vi phạm
        /// POST /api/security/test/moderation
        /// Body JSON: { "text": "Lời nhắn thử nghiệm...", "donorName": "Tên..." }
        /// </summary>
        [HttpPost("moderation")]
        public IActionResult TestModeration([FromBody] TestModerationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return BadRequest(new { success = false, message = "Vui lòng truyền text để kiểm tra." });
            }

            var result = _moderationService.ModerateContent(request.Text, request.DonorName);

            return Ok(new
            {
                success = true,
                isClean = result.IsClean,
                originalText = result.OriginalText,
                sanitizedText = result.SanitizedText,
                summary = result.GetSummary(),
                flaggedReasons = result.FlaggedReasons,
                detectedKeywords = result.DetectedKeywords,
                flags = new
                {
                    hasProfanity = result.HasProfanity,
                    hasRacism = result.HasRacism,
                    hasDefamation = result.HasDefamation,
                    hasMaliciousLink = result.HasMaliciousLink,
                    containsAnyLink = result.ContainsAnyLink
                }
            });
        }

        /// <summary>
        /// Test Rate Limiting theo IP: Gửi liên tục để xem cơ chế đếm ngược và chặn
        /// POST /api/security/test/rate-limit
        /// </summary>
        [HttpPost("rate-limit")]
        public IActionResult TestRateLimit()
        {
            var clientIp = _rateLimiter.ResolveClientIp(HttpContext);
            var isAllowed = _rateLimiter.CheckLimit(
                clientIp,
                actionKey: "test_limit",
                maxRequests: 5,
                window: TimeSpan.FromSeconds(60),
                out var remainingRequests,
                out var retryAfter);

            if (!isAllowed)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    success = false,
                    error = "RATE_LIMIT_EXCEEDED",
                    clientIp,
                    remainingRequests = 0,
                    retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds),
                    message = $"IP {clientIp} đã bị chặn do vượt quá 5 request/phút."
                });
            }

            return Ok(new
            {
                success = true,
                clientIp,
                remainingRequests,
                message = $"Yêu cầu hợp lệ! Còn {remainingRequests} lượt gọi trong cửa sổ 60s hiện tại."
            });
        }

        /// <summary>
        /// Sinh mã chữ ký HMAC-SHA256 cho chuỗi body bất kỳ để dùng thử nghiệm Webhook
        /// POST /api/security/test/hmac-generate
        /// </summary>
        [HttpPost("hmac-generate")]
        public IActionResult GenerateHmac([FromBody] TestHmacRequest request)
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            var payload = request?.Payload ?? string.Empty;
            var signature = _webhookSecurity.ComputeHmacSha256(payload);

            return Ok(new
            {
                success = true,
                payload,
                computedHmacSignature = signature,
                instruction = "Dùng signature này gắn vào Header 'X-Signature' khi gửi POST request tới /api/webhook/payment"
            });
        }
    }

    public class TestModerationRequest
    {
        public string? Text { get; set; }
        public string? DonorName { get; set; }
    }

    public class TestHmacRequest
    {
        public string? Payload { get; set; }
    }
}
