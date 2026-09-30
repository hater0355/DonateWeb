using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using DonateWeb.Security.Configuration;
using DonateWeb.Security.RateLimiting;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// SERVICE TRIỂN KHAI BẢO MẬT & XÁC THỰC WEBHOOK CHẶT CHẼ
    /// Thư mục riêng: Security/Webhook/
    /// Cơ chế bảo mật:
    /// 1. Whitelist IP: Kiểm tra IP của máy chủ đối tác thanh toán (hỗ trợ cả Single IP và Dải mạng CIDR).
    /// 2. Secret Key: Kiểm tra mã bí mật qua Header hoặc Token.
    /// 3. HMAC-SHA256: Kiểm tra tính toàn vẹn gói tin tránh bị can thiệp trên đường truyền (Tampering).
    /// 4. So sánh chuỗi bằng CryptographicOperations.FixedTimeEquals chống tấn công Timing Attack.
    /// </summary>
    public class WebhookSecurityService : IWebhookSecurityService
    {
        private readonly WebhookSecuritySettings _settings;
        private readonly IIpRateLimiterService _ipResolver;
        private readonly ILogger<WebhookSecurityService> _logger;

        public WebhookSecurityService(
            IOptions<SecuritySettings> securityOptions,
            IIpRateLimiterService ipResolver,
            ILogger<WebhookSecurityService> logger)
        {
            _settings = securityOptions.Value.Webhook;
            _ipResolver = ipResolver;
            _logger = logger;
        }

        /// <summary>
        /// Kiểm tra xem địa chỉ IP gọi webhook có nằm trong danh sách Whitelist không
        /// </summary>
        public bool IsIpAllowed(string clientIp)
        {
            if (string.IsNullOrWhiteSpace(clientIp)) return false;

            // Chấp nhận localhost cho môi trường dev/testing
            if (clientIp == "127.0.0.1" || clientIp == "::1" || clientIp.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(clientIp, out var clientAddress))
            {
                return false;
            }

            // Chuẩn hóa IPv4 mapped in IPv6
            if (clientAddress.IsIPv4MappedToIPv6)
            {
                clientAddress = clientAddress.MapToIPv4();
            }

            foreach (var allowed in _settings.AllowedIpAddresses)
            {
                if (string.IsNullOrWhiteSpace(allowed)) continue;

                var rule = allowed.Trim();

                // 1. Kiểm tra nếu là CIDR notation (ví dụ: 103.28.36.0/24)
                if (rule.Contains('/'))
                {
                    if (IsIpInCidrRange(clientAddress, rule))
                    {
                        return true;
                    }
                }
                // 2. Kiểm tra IP cụ thể
                else if (IPAddress.TryParse(rule, out var ruleAddress))
                {
                    if (ruleAddress.IsIPv4MappedToIPv6) ruleAddress = ruleAddress.MapToIPv4();
                    if (clientAddress.Equals(ruleAddress))
                    {
                        return true;
                    }
                }
                // 3. So sánh chuỗi trực tiếp (cho localhost, hostname)
                else if (string.Equals(clientIp, rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Xác thực Secret Key bằng kỹ thuật Constant-time comparison
        /// </summary>
        public bool ValidateSecretKey(string? providedSecret)
        {
            if (string.IsNullOrWhiteSpace(providedSecret) || string.IsNullOrWhiteSpace(_settings.SecretKey))
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(_settings.SecretKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedSecret.Trim());

            if (expectedBytes.Length != providedBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }

        /// <summary>
        /// Xác thực chữ ký HMAC-SHA256 của Request Body
        /// </summary>
        public bool ValidateHmacSignature(string rawBody, string? providedSignature)
        {
            if (string.IsNullOrWhiteSpace(providedSignature))
            {
                return false;
            }

            var expectedSignature = ComputeHmacSha256(rawBody);

            // Chữ ký có thể ở định dạng Hex hoặc Base64
            var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant());
            var providedBytes = Encoding.UTF8.GetBytes(providedSignature.Trim().ToLowerInvariant());

            if (expectedBytes.Length != providedBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }

        /// <summary>
        /// Tính toán chuỗi HMAC-SHA256
        /// </summary>
        public string ComputeHmacSha256(string data)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_settings.SecretKey);
            var dataBytes = Encoding.UTF8.GetBytes(data ?? string.Empty);

            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(dataBytes);

            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        /// <summary>
        /// Kiểm tra toàn diện HTTP Request gọi vào Webhook (IP Whitelist + Secret Key / Signature)
        /// </summary>
        public Task<(bool IsValid, int StatusCode, string ErrorMessage)> ValidateRequestAsync(HttpContext context, string rawBody)
        {
            var clientIp = _ipResolver.ResolveClientIp(context);

            // BƯỚC 1: XÁC THỰC IP WHITELIST
            if (!IsIpAllowed(clientIp))
            {
                _logger.LogWarning(
                    "[BẢO MẬT WEBHOOK] Từ chối truy cập: IP {ClientIp} không nằm trong danh sách IP Whitelist của đối tác.",
                    clientIp);

                return Task.FromResult((
                    false,
                    StatusCodes.Status403Forbidden,
                    $"Truy cập bị từ chối (403 Forbidden): Địa chỉ IP ({clientIp}) không nằm trong danh sách trắng (Whitelist) của đối tác thanh toán."));
            }

            // BƯỚC 2: XÁC THỰC SECRET KEY HOẶC CHỮ KÝ HMAC-SHA256
            // 2.1. Lấy secret từ Header cấu hình (ví dụ: X-Webhook-Secret)
            string? secret = null;
            if (context.Request.Headers.TryGetValue(_settings.SecretHeaderName, out var headerSecret))
            {
                secret = headerSecret.ToString();
            }
            // 2.2. Kiểm tra Header Authorization (Bearer <token>)
            else if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                var authStr = authHeader.ToString().Trim();
                if (authStr.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    secret = authStr["Bearer ".Length..].Trim();
                }
            }
            // 2.3. Kiểm tra query param ?secret=... (cho một số cổng webhook đơn giản)
            else if (context.Request.Query.TryGetValue("secret", out var querySecret))
            {
                secret = querySecret.ToString();
            }

            // 2.4. Lấy chữ ký số HMAC từ Header (ví dụ: X-Signature)
            string? signature = null;
            if (context.Request.Headers.TryGetValue(_settings.SignatureHeaderName, out var headerSig))
            {
                signature = headerSig.ToString();
            }

            bool isSecretValid = !string.IsNullOrEmpty(secret) && ValidateSecretKey(secret);
            bool isSignatureValid = !string.IsNullOrEmpty(signature) && ValidateHmacSignature(rawBody, signature);

            if (!isSecretValid && !isSignatureValid)
            {
                _logger.LogWarning(
                    "[BẢO MẬT WEBHOOK] Xác thực thất bại từ IP {ClientIp}: Secret Key hoặc Chữ ký HMAC không khớp.",
                    clientIp);

                return Task.FromResult((
                    false,
                    StatusCodes.Status401Unauthorized,
                    "Xác thực Webhook thất bại (401 Unauthorized): Secret Key hoặc Chữ ký HMAC-SHA256 không hợp lệ hoặc bị thiếu."));
            }

            _logger.LogInformation(
                "[BẢO MẬT WEBHOOK] Xác thực thành công từ IP {ClientIp} qua cơ chế {AuthMethod}.",
                clientIp,
                isSignatureValid ? "HMAC-SHA256 Signature" : "Secret Key");

            return Task.FromResult((true, StatusCodes.Status200OK, string.Empty));
        }

        // ====================================================================
        // HÀM BỔ TRỢ: XỬ LÝ KIỂM TRA DẢI IP CIDR (VÍ DỤ: 103.28.36.0/24)
        // ====================================================================
        private static bool IsIpInCidrRange(IPAddress clientAddress, string cidr)
        {
            try
            {
                var parts = cidr.Split('/');
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseIp) || !int.TryParse(parts[1], out var prefixLength))
                {
                    return false;
                }

                if (baseIp.IsIPv4MappedToIPv6) baseIp = baseIp.MapToIPv4();
                if (clientAddress.AddressFamily != baseIp.AddressFamily) return false;

                var clientBytes = clientAddress.GetAddressBytes();
                var baseBytes = baseIp.GetAddressBytes();

                int fullBytes = prefixLength / 8;
                int remainingBits = prefixLength % 8;

                for (int i = 0; i < fullBytes; i++)
                {
                    if (clientBytes[i] != baseBytes[i]) return false;
                }

                if (remainingBits > 0 && fullBytes < clientBytes.Length)
                {
                    byte mask = (byte)(0xFF << (8 - remainingBits));
                    if ((clientBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
