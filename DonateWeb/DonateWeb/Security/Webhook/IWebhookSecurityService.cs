using Microsoft.AspNetCore.Http;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// GIAO DIỆN BẢO MẬT VÀ XÁC THỰC WEBHOOK CHO CỔNG THANH TOÁN
    /// Thư mục riêng: Security/Webhook/
    /// Yêu cầu bảo mật cao cấp:
    /// 1. Whitelist IP: Chỉ cho phép các IP trong danh sách được phê duyệt (hỗ trợ cả CIDR / dải mạng) gọi webhook.
    /// 2. Secret Key & HMAC-SHA256: Xác thực tính toàn vẹn và nguồn gốc dữ liệu qua Khóa bí mật hoặc Chữ ký băm.
    /// 3. Chống tấn công Timing Attack bằng so sánh chuỗi thời gian không đổi (Constant-time comparison).
    /// </summary>
    public interface IWebhookSecurityService
    {
        /// <summary>
        /// Kiểm tra xem IP của client có nằm trong danh sách IP Whitelist cho phép gọi webhook không.
        /// </summary>
        /// <param name="clientIp">Địa chỉ IP gọi đến</param>
        /// <returns>True nếu hợp lệ, False nếu bị chặn</returns>
        bool IsIpAllowed(string clientIp);

        /// <summary>
        /// Xác thực Secret Key từ Header hoặc Query Parameter
        /// </summary>
        /// <param name="providedSecret">Secret key do phía đối tác gửi kèm</param>
        /// <returns>True nếu trùng khớp hoàn toàn, False nếu sai hoặc thiếu</returns>
        bool ValidateSecretKey(string? providedSecret);

        /// <summary>
        /// Xác thực chữ ký số HMAC-SHA256 của Request Body với Secret Key đã cấu hình
        /// </summary>
        /// <param name="rawBody">Nội dung thô của request body</param>
        /// <param name="providedSignature">Chữ ký số do đối tác gửi trong Header hoặc Body</param>
        /// <returns>True nếu chữ ký hợp lệ</returns>
        bool ValidateHmacSignature(string rawBody, string? providedSignature);

        /// <summary>
        /// Tạo chữ ký HMAC-SHA256 cho chuỗi dữ liệu đầu vào (dùng để test hoặc đối soát)
        /// </summary>
        string ComputeHmacSha256(string data);

        /// <summary>
        /// Kiểm tra toàn diện cả IP Whitelist và Secret Key / HMAC cho một HTTP Request
        /// </summary>
        /// <param name="context">HttpContext của request gọi webhook</param>
        /// <param name="rawBody">Body thô của request</param>
        /// <returns>Tuple (IsValid, StatusCode, ErrorMessage)</returns>
        Task<(bool IsValid, int StatusCode, string ErrorMessage)> ValidateRequestAsync(HttpContext context, string rawBody);
    }
}
