using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Enums;

namespace DonateWeb.Models.Entities
{
    public class ShopProduct
    {
        [Key]
        public int Id { get; set; }

        public int StreamerProfileId { get; set; }
        [ForeignKey("StreamerProfileId")]
        public virtual StreamerProfile StreamerProfile { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 1000000000)]
        public decimal Price { get; set; }

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public int StockQuantity { get; set; } = 999;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Trạng thái phê duyệt của Quản trị viên (Admin):
        /// 0: Chờ duyệt, 1: Đã duyệt (Được bán), 2: Bị từ chối
        /// </summary>
        public ProductApprovalStatus ApprovalStatus { get; set; } = ProductApprovalStatus.Pending;

        /// <summary>
        /// Lý do từ chối phê duyệt từ Quản trị viên (nếu bị từ chối)
        /// </summary>
        [MaxLength(500)]
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Thời điểm Admin phê duyệt hoặc từ chối
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Tên tài khoản Admin thực hiện duyệt
        /// </summary>
        [MaxLength(100)]
        public string? ApprovedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public virtual ICollection<ShopOrderItem> OrderItems { get; set; } = new List<ShopOrderItem>();
    }
}
