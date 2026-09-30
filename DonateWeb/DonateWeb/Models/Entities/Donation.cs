using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Enums;

namespace DonateWeb.Models.Entities
{
    public class Donation
    {
        [Key]
        public int Id { get; set; }

        public int StreamerProfileId { get; set; }
        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile StreamerProfile { get; set; } = null!;

        public int? DonorUserId { get; set; }
        [ForeignKey("DonorUserId")]
        public virtual User? DonorUser { get; set; }

        [Required]
        [MaxLength(100)]
        public string DonorName { get; set; } = "Thằng lìn giấu tên";

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [MaxLength(1000)]
        public string? Message { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;

        public DonationStatus Status { get; set; } = DonationStatus.Pending;

        [MaxLength(100)]
        public string? TransactionCode { get; set; }

        /// <summary>
        /// Cờ đánh dấu giao dịch đang có khiếu nại (Dispute / Nghi ngờ gian lận / Lỗi trừ tiền nhưng không nhận được quà)
        /// </summary>
        public bool IsDisputed { get; set; } = false;

        /// <summary>
        /// Lý do người dùng khiếu nại
        /// </summary>
        [MaxLength(500)]
        public string? DisputeReason { get; set; }

        /// <summary>
        /// Ghi chú xử lý nghiệp vụ của Quản trị viên (Admin)
        /// </summary>
        [MaxLength(1000)]
        public string? AdminNote { get; set; }

        /// <summary>
        /// Thời điểm hoàn tất giải quyết khiếu nại
        /// </summary>
        public DateTime? ResolvedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Lịch sử kiểm tra và xử lý log giao dịch (Audit Trail)
        /// </summary>
        public virtual ICollection<TransactionAuditLog> AuditLogs { get; set; } = new List<TransactionAuditLog>();
    }
}
