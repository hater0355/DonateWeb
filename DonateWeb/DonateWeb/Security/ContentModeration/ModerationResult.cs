namespace DonateWeb.Security.ContentModeration
{
    /// <summary>
    /// KẾT QUẢ KIỂM DUYỆT LỜI NHẮN VÀ NỘI DUNG QUYÊN GÓP
    /// Chứa thông tin chi tiết về các lỗi vi phạm phát hiện được và văn bản đã qua làm sạch (sanitized).
    /// </summary>
    public class ModerationResult
    {
        /// <summary>
        /// Trạng thái an toàn: True nếu nội dung hoàn toàn sạch sẽ, không vi phạm bất kỳ tiêu chuẩn nào.
        /// </summary>
        public bool IsClean => FlaggedReasons.Count == 0;

        /// <summary>
        /// Văn bản gốc do người ủng hộ gửi lên
        /// </summary>
        public string OriginalText { get; set; } = string.Empty;

        /// <summary>
        /// Văn bản đã qua xử lý an toàn (đã che từ tục tĩu thành ***, gỡ link độc hại) để phát lên OBS Overlay
        /// </summary>
        public string SanitizedText { get; set; } = string.Empty;

        /// <summary>
        /// Danh sách các lý do vi phạm (ví dụ: "Từ ngữ thô tục", "Phân biệt chủng tộc", "Xúc phạm danh dự", "Spam link độc hại")
        /// </summary>
        public List<string> FlaggedReasons { get; set; } = new();

        /// <summary>
        /// Danh sách các từ khóa vi phạm cụ thể bị phát hiện trong nội dung
        /// </summary>
        public List<string> DetectedKeywords { get; set; } = new();

        /// <summary>
        /// Cờ đánh dấu có chứa từ ngữ thô tục/tục tĩu
        /// </summary>
        public bool HasProfanity { get; set; }

        /// <summary>
        /// Cờ đánh dấu có chứa nội dung phân biệt chủng tộc / kỳ thị
        /// </summary>
        public bool HasRacism { get; set; }

        /// <summary>
        /// Cờ đánh dấu có nội dung xúc phạm danh dự, lăng mạ
        /// </summary>
        public bool HasDefamation { get; set; }

        /// <summary>
        /// Cờ đánh dấu có chứa link độc hại / spam URL / cờ bạc lậu
        /// </summary>
        public bool HasMaliciousLink { get; set; }

        /// <summary>
        /// Cờ đánh dấu có chứa liên kết bên ngoài (bất kể độc hại hay không)
        /// </summary>
        public bool ContainsAnyLink { get; set; }

        /// <summary>
        /// Trả về chuỗi tóm tắt các vi phạm để ghi chú hoặc thông báo cho người dùng
        /// </summary>
        public string GetSummary()
        {
            if (IsClean) return "Nội dung an toàn, hợp lệ.";
            return $"Phát hiện vi phạm: {string.Join(", ", FlaggedReasons)} (Từ khóa: {string.Join(", ", DetectedKeywords)})";
        }
    }
}
