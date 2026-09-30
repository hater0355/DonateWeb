using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Lưu lịch sử biến động số dư ví của User (Nạp tiền cho Viewer, Rút tiền cho Streamer)
    /// </summary>
    public class WalletTransaction
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string TransactionCode { get; set; } = string.Empty;

        /// <summary>
        /// Loại giao dịch: "DEPOSIT" (Nạp tiền), "WITHDRAW" (Rút tiền)
        /// </summary>
        [Required]
        [MaxLength(30)]
        public string TransactionType { get; set; } = string.Empty;

        /// <summary>
        /// Số tiền giao dịch (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Số dư trước giao dịch
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceBefore { get; set; }

        /// <summary>
        /// Số dư sau giao dịch (đồng bộ với User.WalletBalance)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceAfter { get; set; }

        /// <summary>
        /// Phương thức thanh toán / nhận tiền (Ví dụ: Chuyển khoản QR, MoMo, VNPay, Ngân hàng)
        /// </summary>
        [MaxLength(100)]
        public string? PaymentMethodName { get; set; }

        /// <summary>
        /// Trạng thái: 0 = Pending (Chờ duyệt), 1 = Success (Thành công), 2 = Failed/Rejected (Thất bại/Từ chối)
        /// </summary>
        public int Status { get; set; } = 1;

        [MaxLength(500)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
