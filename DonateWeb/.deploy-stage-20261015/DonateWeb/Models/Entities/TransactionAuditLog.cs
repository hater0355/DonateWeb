using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Enums;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Thực thể lưu trữ toàn bộ nhật ký kiểm tra (Audit Trail) của giao dịch.
    /// Dùng khi có khiếu nại, tranh chấp (dispute), kiểm tra lỗi hoặc hoàn tiền.
    /// </summary>
    public class TransactionAuditLog
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Khóa ngoại liên kết với giao dịch Donate được kiểm tra
        /// </summary>
        public int DonationId { get; set; }
        [ForeignKey("DonationId")]
        public virtual Donation Donation { get; set; } = null!;

        /// <summary>
        /// Mã giao dịch tại thời điểm ghi log (ví dụ: DON_SAMPLE_001, DON_20260917_...)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string TransactionCode { get; set; } = string.Empty;

        /// <summary>
        /// Loại hành động: 'DISPUTE_OPENED', 'REFUNDED', 'VERIFIED_SUCCESS', 'CANCELLED', 'ADMIN_NOTE'
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Trạng thái trước khi thao tác
        /// </summary>
        public DonationStatus OldStatus { get; set; }

        /// <summary>
        /// Trạng thái sau khi thao tác
        /// </summary>
        public DonationStatus NewStatus { get; set; }

        /// <summary>
        /// Nội dung ghi chú, biên bản xử lý hoặc kết luận khiếu nại của Quản trị viên
        /// </summary>
        [MaxLength(1000)]
        public string? Note { get; set; }

        /// <summary>
        /// Người thực hiện kiểm tra/thao tác (Username của Admin hoặc "System")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string PerformedBy { get; set; } = "Admin";

        /// <summary>
        /// Thời điểm ghi nhận log kiểm tra
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
