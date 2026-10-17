using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    public class ShopOrder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string OrderCode { get; set; } = string.Empty;

        public int StreamerProfileId { get; set; }
        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile StreamerProfile { get; set; } = null!;

        public int? BuyerUserId { get; set; }
        [ForeignKey("BuyerUserId")]
        public virtual User? BuyerUser { get; set; }

        [Required]
        [MaxLength(100)]
        public string BuyerName { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? BuyerPhone { get; set; }

        [MaxLength(300)]
        public string? BuyerAddress { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [MaxLength(50)]
        public string PaymentMethod { get; set; } = "Wallet"; // 'Wallet' | 'VietQR'

        [MaxLength(50)]
        public string PaymentStatus { get; set; } = "Pending"; // 'Pending' | 'Paid' | 'Cancelled'

        [MaxLength(100)]
        public string? TransactionCode { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        public virtual ICollection<ShopOrderItem> Items { get; set; } = new List<ShopOrderItem>();
    }
}
