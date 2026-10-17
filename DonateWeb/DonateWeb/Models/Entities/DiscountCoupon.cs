using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Mã giảm giá toàn hệ thống do quản trị viên phát hành.
    /// </summary>
    public class DiscountCoupon
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required, MaxLength(250)]
        public string Content { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Provider { get; set; } = string.Empty;

        /// <summary>Percent = phần trăm; Fixed = số tiền cố định VNĐ.</summary>
        [Required, MaxLength(20)]
        public string DiscountType { get; set; } = "Percent";

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Số đơn đã thanh toán thành công bằng mã này.</summary>
        public int UsedCount { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public virtual ICollection<ShopOrder> ShopOrders { get; set; } = new List<ShopOrder>();
    }
}
