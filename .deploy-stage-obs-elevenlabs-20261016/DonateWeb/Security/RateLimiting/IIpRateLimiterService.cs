using Microsoft.AspNetCore.Http;

namespace DonateWeb.Security.RateLimiting
{
    /// <summary>
    /// GIAO DIỆN DỊCH VỤ GIỚI HẠN TẦN SUẤT REQUEST (RATE LIMITING) THEO ĐỊA CHỈ IP
    /// Thư mục riêng: Security/RateLimiting/
    /// Mục tiêu:
    /// - Giới hạn số lượng request tạo đơn quyên góp/phút từ một IP
    /// - Chống spam bot, tấn công từ chối dịch vụ (DoS), và gửi đơn ảo liên tục
    /// </summary>
    public interface IIpRateLimiterService
    {
        /// <summary>
        /// Kiểm tra xem địa chỉ IP hiện tại có được phép thực hiện hành động hay không.
        /// </summary>
        /// <param name="clientIp">Địa chỉ IP của người dùng/bot</param>
        /// <param name="actionKey">Tên hành động cần giới hạn (ví dụ: "create_donation", "login")</param>
        /// <param name="maxRequests">Số lượt request tối đa cho phép trong cửa sổ thời gian</param>
        /// <param name="window">Khoảng thời gian của cửa sổ (ví dụ: 1 phút)</param>
        /// <param name="remainingRequests">Số lượt gọi còn lại trước khi bị chặn</param>
        /// <param name="retryAfter">Thời gian phải chờ trước khi được gửi tiếp nếu bị chặn</param>
        /// <returns>True nếu hợp lệ (được phép tiếp tục), False nếu vượt quá giới hạn (bị chặn)</returns>
        bool CheckLimit(
            string clientIp,
            string actionKey,
            int maxRequests,
            TimeSpan window,
            out int remainingRequests,
            out TimeSpan retryAfter);

        /// <summary>
        /// Trích xuất địa chỉ IP thực tế của client từ HttpContext, xử lý các trường hợp qua Reverse Proxy (Nginx, Cloudflare, Load Balancer)
        /// </summary>
        /// <param name="context">HttpContext của request hiện tại</param>
        /// <returns>Địa chỉ IP dạng chuỗi</returns>
        string ResolveClientIp(HttpContext context);

        /// <summary>
        /// Xóa bộ nhớ đếm giới hạn cho một IP cụ thể (dùng khi Admin mở khóa hoặc test)
        /// </summary>
        void ResetLimit(string clientIp, string actionKey);
    }
}
