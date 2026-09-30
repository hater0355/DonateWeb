namespace DonateWeb.Security.Configuration
{
    /// <summary>
    /// CẤU HÌNH TỔNG THỂ PHÂN HỆ BẢO MẬT & KIỂM DUYỆT (CONTENT MODERATION & SECURITY)
    /// Ánh xạ từ nhánh "Security" trong appsettings.json
    /// </summary>
    public class SecuritySettings
    {
        public const string SectionName = "Security";

        /// <summary>
        /// Cấu hình bộ lọc kiểm duyệt nội dung lời nhắn
        /// </summary>
        public ContentModerationSettings ContentModeration { get; set; } = new();

        /// <summary>
        /// Cấu hình giới hạn tần suất request (Rate Limiting) chống spam bot
        /// </summary>
        public RateLimitingSettings RateLimiting { get; set; } = new();

        /// <summary>
        /// Cấu hình bảo mật và khóa chặt webhook cổng thanh toán
        /// </summary>
        public WebhookSecuritySettings Webhook { get; set; } = new();
    }

    /// <summary>
    /// Cấu hình bộ lọc nội dung (Từ ngữ thô tục, phân biệt chủng tộc, xúc phạm danh dự, spam link)
    /// </summary>
    public class ContentModerationSettings
    {
        /// <summary>
        /// Bật/Tắt tính năng kiểm duyệt lời nhắn
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Ký tự dùng để che từ ngữ vi phạm (mặc định là '*')
        /// </summary>
        public char MaskCharacter { get; set; } = '*';

        /// <summary>
        /// Chặn hoàn toàn hoặc che các liên kết độc hại / spam link rút gọn
        /// </summary>
        public bool BlockMaliciousLinks { get; set; } = true;

        /// <summary>
        /// Thông báo hiển thị thay thế khi phát hiện liên kết ngoài bị chặn
        /// </summary>
        public string BlockedLinkPlaceholder { get; set; } = "[Liên kết đã bị ẩn vì lý do an toàn]";

        /// <summary>
        /// Danh sách từ cấm tùy chỉnh bổ sung do quản trị viên định nghĩa
        /// </summary>
        public List<string> CustomBannedWords { get; set; } = new();
    }

    /// <summary>
    /// Cấu hình giới hạn tần suất gọi request tạo đơn quyên góp theo địa chỉ IP
    /// </summary>
    public class RateLimitingSettings
    {
        /// <summary>
        /// Bật/Tắt kiểm tra Rate Limiting chống spam bot
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Số lượng request tối đa cho phép tạo đơn donate từ một địa chỉ IP trong 1 phút
        /// </summary>
        public int DonateRequestsPerMinute { get; set; } = 5;

        /// <summary>
        /// Thời gian cửa sổ tính toán (giây) - mặc định là 60 giây (1 phút)
        /// </summary>
        public int WindowSeconds { get; set; } = 60;

        /// <summary>
        /// Danh sách IP được miễn trừ kiểm tra Rate Limiting (nếu có, ví dụ IP mạng nội bộ của Admin)
        /// </summary>
        public List<string> WhitelistIps { get; set; } = new();
    }

    /// <summary>
    /// Cấu hình khóa chặt endpoint Webhook thanh toán bằng IP Whitelist và Secret Key
    /// </summary>
    public class WebhookSecuritySettings
    {
        /// <summary>
        /// Khóa bí mật (Secret Key) dùng để đối soát tính hợp lệ của Webhook hoặc tạo chữ ký HMAC-SHA256
        /// </summary>
        public string SecretKey { get; set; } = "DonateWeb_Webhook_SecureSecretKey_2026!#@$";

        /// <summary>
        /// Tên Header chứa Secret Key hoặc chữ ký xác thực (ví dụ: X-Webhook-Secret hoặc X-Signature)
        /// </summary>
        public string SecretHeaderName { get; set; } = "X-Webhook-Secret";

        /// <summary>
        /// Tên Header chứa chữ ký số HMAC-SHA256 (nếu đối tác thanh toán gửi kèm HMAC)
        /// </summary>
        public string SignatureHeaderName { get; set; } = "X-Signature";

        /// <summary>
        /// Danh sách địa chỉ IP / Dải mạng (CIDR) của đối tác thanh toán được phép gọi webhook (Whitelist)
        /// Hỗ trợ: IP cụ thể (127.0.0.1, 103.28.36.15), IPv6 (::1), hoặc CIDR (103.28.36.0/24)
        /// </summary>
        public List<string> AllowedIpAddresses { get; set; } = new()
        {
            "127.0.0.1",
            "::1",
            "localhost",
            // Dải IP mẫu của cổng thanh toán MoMo / VNPay / SePay / MBBank
            "103.28.36.0/24",
            "118.69.0.0/16",
            "203.162.0.0/16"
        };
    }
}
