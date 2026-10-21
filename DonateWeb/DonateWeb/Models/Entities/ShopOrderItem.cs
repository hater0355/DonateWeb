using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    public class ShopOrderItem
    {
        [Key]
        public int Id { get; set; }

        public int ShopOrderId { get; set; }
        [ForeignKey("ShopOrderId")]
        public virtual ShopOrder ShopOrder { get; set; } = null!;

        public int ShopProductId { get; set; }
        [ForeignKey("ShopProductId")]
        public virtual ShopProduct ShopProduct { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>Size/biến thể khách đã chọn, được lưu cùng bản chụp đơn hàng.</summary>
        [MaxLength(50)]
        public string? VariantSelection { get; set; }

        /// <summary>Cho biết tồn kho đã được trừ từ một lựa chọn riêng hay từ kho chung cũ.</summary>
        public bool UsesVariantStock { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public int Quantity { get; set; } = 1;
    }
}
