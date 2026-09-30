namespace DonateWeb.Security.ContentModeration
{
    /// <summary>
    /// GIAO DIỆN DỊCH VỤ KIỂM DUYỆT NỘI DUNG LỜI NHẮN & CHỐNG SPAM LINK ĐỘC HẠI
    /// Đặt trong thư mục riêng: Security/ContentModeration/
    /// Chức năng:
    /// 1. Quét và phát hiện từ ngữ thô tục, xúc phạm danh dự, phân biệt chủng tộc.
    /// 2. Quét và ngăn chặn các liên kết ngoài, link lừa đảo, cờ bạc lậu, spam link rút gọn.
    /// 3. Khử các thủ thuật lách luật (leetspeak, xen kẽ dấu chấm/sao: d.m, v*cl, c_ặ_c).
    /// 4. Làm sạch nội dung (Sanitize) trước khi hiển thị lên màn hình Stream OBS Studio.
    /// </summary>
    public interface IContentModerationService
    {
        /// <summary>
        /// Phân tích toàn diện một thông điệp (Message) và tên người gửi (DonorName).
        /// Trả về đối tượng ModerationResult chứa chi tiết vi phạm và phiên bản văn bản đã được làm sạch.
        /// </summary>
        /// <param name="message">Lời nhắn của người donate</param>
        /// <param name="donorName">Tên hiển thị của người donate (tùy chọn)</param>
        /// <returns>Kết quả kiểm duyệt</returns>
        ModerationResult ModerateContent(string? message, string? donorName = null);

        /// <summary>
        /// Phương thức tiện ích rút gọn: Làm sạch lời nhắn để đưa lên OBS Studio Overlay.
        /// Tự động che từ ngữ tục tĩu thành *** và thay thế link độc hại thành placeholder an toàn.
        /// </summary>
        /// <param name="message">Lời nhắn gốc</param>
        /// <returns>Lời nhắn đã lọc sạch</returns>
        string SanitizeForStream(string? message);

        /// <summary>
        /// Kiểm tra nhanh xem thông điệp có hoàn toàn sạch sẽ hay không.
        /// </summary>
        /// <param name="message">Lời nhắn cần kiểm tra</param>
        /// <returns>True nếu không vi phạm, False nếu có vi phạm</returns>
        bool IsClean(string? message);
    }
}
