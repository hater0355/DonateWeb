using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Areas.Widgets.ViewModels
{
    // ====================================================================
    // 1. VIEWMODELS CHO ALERT BOX (THÔNG BÁO DONATE TRÊN LIVESTREAM OBS)
    // ====================================================================

    /// <summary>
    /// ViewModel quản trị và hiển thị Alert Box trên OBS Browser Source
    /// </summary>
    public class AlertBoxConfigViewModel
    {
        public int StreamerProfileId { get; set; }

        public string StreamerSlug { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string WidgetToken { get; set; } = Guid.NewGuid().ToString("N");

        public string ObsOverlayUrl { get; set; } = string.Empty;

        public string? SuccessMessage { get; set; }

        public string? ErrorMessage { get; set; }

        // --- Cấu hình hình ảnh / GIF hoặc Video và âm thanh ---
        [Display(Name = "Loại Media hiển thị")]
        [MaxLength(20)]
        public string MediaType { get; set; } = "image"; // "image" (ảnh GIF / tĩnh) hoặc "video" (WebM trong suốt / MP4)

        [Display(Name = "Hình ảnh GIF / Video hiển thị")]
        [MaxLength(1000)]
        public string ImageUrl { get; set; } = "https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif";

        public string MediaUrl
        {
            get => ImageUrl;
            set => ImageUrl = value;
        }

        [Display(Name = "Âm thanh thông báo (MP3)")]
        [MaxLength(1000)]
        public string SoundUrl { get; set; } = "https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3";

        [Display(Name = "Âm lượng âm thanh (%)")]
        [Range(0, 100, ErrorMessage = "Âm lượng từ 0 đến 100%")]
        public int SoundVolume { get; set; } = 80;

        [Display(Name = "Thời gian hiển thị (giây)")]
        [Range(3, 30, ErrorMessage = "Thời gian hiển thị từ 3 đến 30 giây")]
        public int DurationSeconds { get; set; } = 6;

        // --- Kiểu dáng chữ và font ---
        [Display(Name = "Màu chữ")]
        [MaxLength(30)]
        public string TextColor { get; set; } = "#10b981";

        [Display(Name = "Phông chữ")]
        [MaxLength(50)]
        public string FontFamily { get; set; } = "Inter";

        [Display(Name = "Cỡ chữ (px)")]
        [Range(16, 72, ErrorMessage = "Cỡ chữ từ 16px đến 72px")]
        public int FontSize { get; set; } = 28;

        // --- Hiệu ứng Animation Animate.css ---
        [Display(Name = "Hiệu ứng xuất hiện")]
        [MaxLength(50)]
        public string AnimationIn { get; set; } = "fadeInDown";

        [Display(Name = "Hiệu ứng biến mất")]
        [MaxLength(50)]
        public string AnimationOut { get; set; } = "fadeOutUp";

        // --- Nội dung và điều kiện kích hoạt ---
        [Display(Name = "Mẫu câu thông báo")]
        [MaxLength(255)]
        public string MessageTemplate { get; set; } = "{donor} vừa ủng hộ {amount} VNĐ!";

        [Display(Name = "Mức donate tối thiểu để phát thông báo (VNĐ)")]
        [Range(0, 1000000000)]
        public decimal MinAmountToAlert { get; set; } = 10000;

        [Display(Name = "Bật giọng đọc Text-To-Speech")]
        public bool IsTtsEnabled { get; set; } = true;
    }

    // ====================================================================
    // 2. VIEWMODELS CHO GOALS (MỤC TIÊU QUYÊN GÓP STREAMER)
    // ====================================================================

    /// <summary>
    /// ViewModel chi tiết một mục tiêu quyên góp
    /// </summary>
    public class StreamerGoalViewModel
    {
        public int Id { get; set; }

        public int StreamerProfileId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tiêu đề mục tiêu")]
        [MaxLength(150, ErrorMessage = "Tiêu đề không quá 150 ký tự")]
        [Display(Name = "Tiêu đề mục tiêu")]
        public string Title { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số tiền mục tiêu")]
        [Range(10000, 10000000000, ErrorMessage = "Số tiền mục tiêu tối thiểu là 10.000 VNĐ")]
        [Display(Name = "Số tiền mục tiêu (VNĐ)")]
        public decimal TargetAmount { get; set; } = 10000000;

        [Range(0, 10000000000, ErrorMessage = "Số tiền khởi điểm không hợp lệ")]
        [Display(Name = "Số tiền khởi điểm ban đầu (VNĐ)")]
        public decimal StartingAmount { get; set; } = 0;

        [Range(0, 10000000000, ErrorMessage = "Số tiền đã đạt không hợp lệ")]
        [Display(Name = "Tổng số tiền đã đạt được (VNĐ)")]
        public decimal CurrentAmount { get; set; } = 0;

        [Required]
        [MaxLength(30)]
        [Display(Name = "Màu thanh chạy tiến độ")]
        public string ProgressBarColor { get; set; } = "#10b981";

        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        [Display(Name = "Hạn chót kết thúc")]
        public DateTime? EndDate { get; set; }

        [Display(Name = "Kích hoạt phát trên OBS")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Tỷ lệ phần trăm tiến độ đạt được (0 - 100%)
        /// </summary>
        public int ProgressPercentage => TargetAmount > 0
            ? (int)Math.Min(100, Math.Round((CurrentAmount / TargetAmount) * 100))
            : 0;

        /// <summary>
        /// Chuỗi định dạng tiến độ quyên góp: "5.000.000đ / 10.000.000đ (50%)"
        /// </summary>
        public string FormattedProgress => $"{CurrentAmount:N0}đ / {TargetAmount:N0}đ ({ProgressPercentage}%)";

        /// <summary>
        /// Chuỗi hiển thị đầy đủ tiêu đề và tiến độ mục tiêu:
        /// Ví dụ: "Mua PC mới: 5.000.000đ / 10.000.000đ (50%)"
        /// </summary>
        public string FullGoalDisplay => $"{Title}: {CurrentAmount:N0}đ / {TargetAmount:N0}đ ({ProgressPercentage}%)";
    }

    /// <summary>
    /// ViewModel cho trang quản lý danh sách Mục tiêu quyên góp
    /// </summary>
    public class GoalManagementViewModel
    {
        public string ObsOverlayUrl { get; set; } = string.Empty;

        public string? SuccessMessage { get; set; }

        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Mục tiêu hiện tại đang kích hoạt hiển thị trên OBS
        /// </summary>
        public StreamerGoalViewModel? ActiveGoal { get; set; }

        /// <summary>
        /// Form khởi tạo mục tiêu mới
        /// </summary>
        public StreamerGoalViewModel NewGoal { get; set; } = new StreamerGoalViewModel();

        /// <summary>
        /// Danh sách toàn bộ mục tiêu của streamer
        /// </summary>
        public List<StreamerGoalViewModel> Goals { get; set; } = new List<StreamerGoalViewModel>();
    }

    // ====================================================================
    // 3. VIEWMODELS CHO LEADERBOARD (BẢNG XẾP HẠNG TOP DONATE OBS)
    // ====================================================================

    /// <summary>
    /// DTO đối tượng người ủng hộ trong bảng xếp hạng
    /// </summary>
    public class LeaderboardDonorDto
    {
        public int Rank { get; set; }

        public string DonorName { get; set; } = string.Empty;

        public int DonationCount { get; set; }

        public decimal TotalAmount { get; set; }

        public string FormattedAmount => $"{TotalAmount:N0}đ";
    }

    /// <summary>
    /// DTO thông tin lượt quyên góp gần nhất
    /// </summary>
    public class RecentDonationDto
    {
        public int Id { get; set; }

        public string DonorName { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string FormattedAmount => $"{Amount:N0}đ";

        public string? Message { get; set; }

        public DateTime CreatedAt { get; set; }

        public string TimeAgo { get; set; } = "Vừa xong";
    }

    /// <summary>
    /// ViewModel tổng thể trang cấu hình và hiển thị Bảng xếp hạng
    /// </summary>
    public class LeaderboardViewModel
    {
        public string StreamerSlug { get; set; } = string.Empty;

        public string ObsOverlayUrl { get; set; } = string.Empty;

        /// <summary>
        /// Chu kỳ lọc: "day" (Hôm nay), "week" (Tuần này), "month" (Tháng này), "all" (Toàn thời gian)
        /// </summary>
        public string Period { get; set; } = "all";

        public List<LeaderboardDonorDto> TopDonors { get; set; } = new List<LeaderboardDonorDto>();

        public List<RecentDonationDto> RecentDonations { get; set; } = new List<RecentDonationDto>();
    }

    // ====================================================================
    // 4. DTO CHO REST API VÀ POLLING THỜI GIAN THỰC OBS
    // ====================================================================

    /// <summary>
    /// Payload trả về khi OBS AlertBox kiểm tra thông báo donate mới
    /// </summary>
    public class AlertPollResultDto
    {
        public int DonationId { get; set; }

        public string DonorName { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string FormattedAmount { get; set; } = string.Empty;

        public string? Message { get; set; }

        public string DisplayText { get; set; } = string.Empty;

        /// <summary>
        /// "image" hoặc "video"
        /// </summary>
        public string MediaType { get; set; } = "image";

        public string ImageUrl { get; set; } = string.Empty;

        public string MediaUrl
        {
            get => ImageUrl;
            set => ImageUrl = value;
        }

        public string SoundUrl { get; set; } = string.Empty;

        public int SoundVolume { get; set; } = 80;

        public int DurationSeconds { get; set; } = 6;

        public string TextColor { get; set; } = "#10b981";

        public string FontFamily { get; set; } = "Inter";

        public int FontSize { get; set; } = 28;

        public string AnimationIn { get; set; } = "fadeInDown";

        public string AnimationOut { get; set; } = "fadeOutUp";

        public bool IsTtsEnabled { get; set; } = false;
    }
}
