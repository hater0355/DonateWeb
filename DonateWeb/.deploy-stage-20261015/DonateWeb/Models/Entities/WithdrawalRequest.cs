using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Yêu cầu rút tiền của Streamer
    /// </summary>
    public class WithdrawalRequest
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        public int? StreamerProfileId { get; set; }

        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile? StreamerProfile { get; set; }

        [Required]
        [MaxLength(100)]
        public string TransactionCode { get; set; } = string.Empty;

        /// <summary>
        /// Số tiền yêu cầu rút (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Số dư trước khi trừ tiền rút
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceBefore { get; set; }

        /// <summary>
        /// Số dư còn lại sau khi tạo yêu cầu rút
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceAfter { get; set; }

        [Required]
        [MaxLength(100)]
        public string BankName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string BankAccountNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string BankAccountName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Note { get; set; }

        /// <summary>
        /// Trạng thái yêu cầu rút tiền: 0 = Đang chờ duyệt (Pending), 1 = Đã duyệt / Hoàn tất (Approved), 2 = Từ chối / Hoàn tiền (Rejected)
        /// </summary>
        public int Status { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }
    }
}
