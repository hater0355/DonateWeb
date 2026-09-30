using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Entities;

namespace DonateWeb.Areas.Widgets.Models
{
    /// <summary>
    /// THỰC THỂ LƯU MỤC TIÊU QUYÊN GÓP (GOAL) VÀO CƠ SỞ DỮ LIỆU
    /// Thư mục riêng: Areas/Widgets/Models/
    /// Quản lý chiến dịch gây quỹ, tính toán tiến độ thanh chạy tự động hiển thị trên OBS.
    /// Ví dụ: "Mua PC mới: 5.000.000đ / 10.000.000đ"
    /// </summary>
    [Table("StreamerGoals")]
    public class StreamerGoal
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Khóa ngoại liên kết với StreamerProfile
        /// </summary>
        [Required]
        public int StreamerProfileId { get; set; }

        /// <summary>
        /// Tiêu đề mục tiêu quyên góp (Ví dụ: "Mua PC mới", "Nâng cấp Microphone")
        /// </summary>
        [Required]
        [MaxLength(150)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Số tiền mục tiêu cần đạt (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal TargetAmount { get; set; } = 10000000;

        /// <summary>
        /// Số tiền khởi điểm ban đầu trước khi tính donate (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal StartingAmount { get; set; } = 0;

        /// <summary>
        /// Số tiền thủ công ghi đè hoặc cộng dồn lưu trữ (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal ManualAmount { get; set; } = 0;

        /// <summary>
        /// Màu sắc thanh chạy tiến độ (Hex code, ví dụ: #10b981)
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string ProgressBarColor { get; set; } = "#10b981";

        /// <summary>
        /// Thời điểm bắt đầu mục tiêu quyên góp
        /// </summary>
        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Thời điểm kết thúc mục tiêu quyên góp (nếu có)
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Đang kích hoạt hiển thị trên màn hình livestream OBS
        /// Chỉ có 1 mục tiêu được IsActive = true tại một thời điểm cho mỗi streamer
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Thời điểm tạo bản ghi
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Thời điểm cập nhật bản ghi
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile? StreamerProfile { get; set; }
    }
}
