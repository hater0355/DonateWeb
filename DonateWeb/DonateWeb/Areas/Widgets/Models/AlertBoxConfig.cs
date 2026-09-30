using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Entities;

namespace DonateWeb.Areas.Widgets.Models
{
    /// <summary>
    /// THỰC THỂ LƯU CẤU HÌNH ALERT BOX (THÔNG BÁO DONATE OBS) VÀO CƠ SỞ DỮ LIỆU
    /// Thư mục riêng: Areas/Widgets/Models/
    /// Cho phép streamer tùy biến ảnh GIF, video WebM/MP4, âm thanh, màu chữ, font chữ và hiệu ứng animation.
    /// </summary>
    [Table("AlertBoxConfigs")]
    public class AlertBoxConfig
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Khóa ngoại liên kết với StreamerProfile
        /// </summary>
        [Required]
        public int StreamerProfileId { get; set; }

        /// <summary>
        /// Token bảo mật cho URL widget OBS
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string WidgetToken { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Loại media hiển thị: "image" (ảnh tĩnh / GIF) hoặc "video" (video WebM / MP4)
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string MediaType { get; set; } = "image";

        /// <summary>
        /// Đường dẫn URL tới ảnh GIF hoặc video hiển thị trên OBS
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string MediaUrl { get; set; } = "https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif";

        /// <summary>
        /// Đường dẫn URL tới file âm thanh hiệu ứng MP3/WAV khi có donate
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string SoundUrl { get; set; } = "https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3";

        /// <summary>
        /// Âm lượng hiệu ứng âm thanh (0% - 100%)
        /// </summary>
        [Range(0, 100)]
        public int SoundVolume { get; set; } = 80;

        /// <summary>
        /// Thời gian hiển thị thông báo trên màn hình livestream (giây)
        /// </summary>
        [Range(3, 30)]
        public int DurationSeconds { get; set; } = 6;

        /// <summary>
        /// Mã màu chữ tiêu đề thông báo (Hex code, ví dụ: #10b981)
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string TextColor { get; set; } = "#10b981";

        /// <summary>
        /// Phông chữ hiển thị (Inter, Montserrat, Roboto, Bangers, Press Start 2P)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string FontFamily { get; set; } = "Inter";

        /// <summary>
        /// Cỡ chữ tiêu đề (px)
        /// </summary>
        [Range(16, 72)]
        public int FontSize { get; set; } = 28;

        /// <summary>
        /// Tên hiệu ứng CSS khi xuất hiện (Animate.css: fadeInDown, bounceIn, zoomIn,...)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string AnimationIn { get; set; } = "fadeInDown";

        /// <summary>
        /// Tên hiệu ứng CSS khi biến mất (Animate.css: fadeOutUp, bounceOut, zoomOut,...)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string AnimationOut { get; set; } = "fadeOutUp";

        /// <summary>
        /// Mẫu câu thông báo: {donor} vừa ủng hộ {amount} VNĐ!
        /// Hỗ trợ các biến giữ chỗ: {donor}, {amount}, {message}
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string MessageTemplate { get; set; } = "{donor} vừa ủng hộ {amount} VNĐ!";

        /// <summary>
        /// Mức donate tối thiểu (VNĐ) để kích hoạt hiển thị Alert Box trên OBS
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MinAmountToAlert { get; set; } = 10000;

        /// <summary>
        /// Bật/Tắt tính năng đọc tin nhắn tự động qua giọng nói (Text-To-Speech)
        /// </summary>
        public bool IsTtsEnabled { get; set; } = true;

        /// <summary>
        /// Thời điểm tạo cấu hình
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Thời điểm cập nhật cấu hình lần cuối
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile? StreamerProfile { get; set; }
    }
}
