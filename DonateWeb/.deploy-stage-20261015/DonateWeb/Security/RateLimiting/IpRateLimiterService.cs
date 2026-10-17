using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using DonateWeb.Security.Configuration;

namespace DonateWeb.Security.RateLimiting
{
    /// <summary>
    /// DỊCH VỤ QUẢN LÝ RATE LIMITING THEO ĐỊA CHỈ IP TRONG BỘ NHỚ (SLIDING WINDOW ALGORITHM)
    /// Thư mục riêng: Security/RateLimiting/
    /// Đặc điểm:
    /// - Thuật toán Sliding Window đếm chính xác số request trong vòng N giây gần nhất
    /// - Xử lý an toàn đa luồng (Thread-safe) với ConcurrentDictionary và lock cục bộ
    /// - Tự động dọn dẹp các bản ghi hết hạn để tránh rò rỉ bộ nhớ (Memory Leak)
    /// - Hỗ trợ phân giải IP qua Proxy / CDN (X-Forwarded-For, CF-Connecting-IP, X-Real-IP)
    /// </summary>
    public class IpRateLimiterService : IIpRateLimiterService
    {
        private readonly RateLimitingSettings _settings;
        private readonly ILogger<IpRateLimiterService> _logger;

        // Lưu trữ danh sách mốc thời gian gọi request: Key = "{actionKey}:{clientIp}", Value = List các DateTime (UTC)
        private static readonly ConcurrentDictionary<string, List<DateTime>> _requestLogs = new();

        // Thời điểm dọn dẹp bộ đệm gần nhất
        private static DateTime _lastCleanupTime = DateTime.UtcNow;
        private static readonly object _cleanupLock = new();

        public IpRateLimiterService(
            IOptions<SecuritySettings> securityOptions,
            ILogger<IpRateLimiterService> logger)
        {
            _settings = securityOptions.Value.RateLimiting;
            _logger = logger;
        }

        /// <summary>
        /// Kiểm tra và ghi nhận 1 request mới của IP. Nếu vượt quá giới hạn -> Chặn
        /// </summary>
        public bool CheckLimit(
            string clientIp,
            string actionKey,
            int maxRequests,
            TimeSpan window,
            out int remainingRequests,
            out TimeSpan retryAfter)
        {
            // Nếu tính năng Rate Limiting bị tắt
            if (!_settings.IsEnabled)
            {
                remainingRequests = maxRequests;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            // Kiểm tra IP có nằm trong Whitelist miễn trừ kiểm tra không
            if (_settings.WhitelistIps.Contains(clientIp, StringComparer.OrdinalIgnoreCase))
            {
                remainingRequests = maxRequests;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            var now = DateTime.UtcNow;
            var windowStart = now - window;
            var cacheKey = $"{actionKey}:{clientIp}";

            // Định kỳ dọn dẹp các bản ghi cũ hơn 10 phút một lần
            TriggerPeriodicCleanup(now);

            // Lấy danh sách lịch sử gọi của IP này
            var history = _requestLogs.GetOrAdd(cacheKey, _ => new List<DateTime>());

            lock (history)
            {
                // Loại bỏ các request đã trôi ra ngoài cửa sổ thời gian
                history.RemoveAll(t => t < windowStart);

                // Kiểm tra nếu đã chạm hoặc vượt ngưỡng cho phép
                if (history.Count >= maxRequests)
                {
                    var oldestInWindow = history.FirstOrDefault();
                    var timeUntilReset = (oldestInWindow + window) - now;
                    retryAfter = timeUntilReset > TimeSpan.Zero ? timeUntilReset : TimeSpan.FromSeconds(1);
                    remainingRequests = 0;

                    _logger.LogWarning(
                        "[CHỐNG SPAM BOT] IP {ClientIp} bị chặn do vượt quá giới hạn {Max} request/{WindowSeconds}s cho hành động '{ActionKey}'. Phải chờ: {RetryAfterSeconds:F0}s",
                        clientIp, maxRequests, window.TotalSeconds, actionKey, retryAfter.TotalSeconds);

                    return false;
                }

                // Ghi nhận request mới
                history.Add(now);
                remainingRequests = Math.Max(0, maxRequests - history.Count);
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }

        /// <summary>
        /// Trích xuất IP thực tế của client từ request HTTP
        /// </summary>
        public string ResolveClientIp(HttpContext context)
        {
            if (context == null) return "unknown";

            // 1. Kiểm tra Header do Cloudflare gắn
            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cfIp) && !string.IsNullOrWhiteSpace(cfIp))
            {
                return cfIp.ToString().Trim();
            }

            // 2. Kiểm tra Header X-Forwarded-For (Lấy IP đầu tiên trong chuỗi forwarded)
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
            {
                var ips = forwardedFor.ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (ips.Length > 0 && IPAddress.TryParse(ips[0].Trim(), out var parsedIp))
                {
                    return parsedIp.ToString();
                }
            }

            // 3. Kiểm tra Header X-Real-IP
            if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp) && !string.IsNullOrWhiteSpace(realIp))
            {
                return realIp.ToString().Trim();
            }

            // 4. Lấy RemoteIpAddress trực tiếp từ kết nối Socket TCP
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp != null)
            {
                // Nếu là địa chỉ IPv4 ánh xạ trong IPv6 (::ffff:127.0.0.1) -> chuyển về IPv4
                if (remoteIp.IsIPv4MappedToIPv6)
                {
                    return remoteIp.MapToIPv4().ToString();
                }
                return remoteIp.ToString();
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// Xóa bản ghi giới hạn của một IP
        /// </summary>
        public void ResetLimit(string clientIp, string actionKey)
        {
            var cacheKey = $"{actionKey}:{clientIp}";
            _requestLogs.TryRemove(cacheKey, out _);
        }

        /// <summary>
        /// Tự động dọn dẹp các bản ghi cũ không còn request nào để tránh tiêu tốn RAM
        /// </summary>
        private static void TriggerPeriodicCleanup(DateTime now)
        {
            if ((now - _lastCleanupTime).TotalMinutes < 5) return;

            lock (_cleanupLock)
            {
                if ((now - _lastCleanupTime).TotalMinutes < 5) return;
                _lastCleanupTime = now;

                var cutoff = now.AddMinutes(-5);
                foreach (var kvp in _requestLogs)
                {
                    lock (kvp.Value)
                    {
                        kvp.Value.RemoveAll(t => t < cutoff);
                    }
                    if (kvp.Value.Count == 0)
                    {
                        _requestLogs.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }
}
