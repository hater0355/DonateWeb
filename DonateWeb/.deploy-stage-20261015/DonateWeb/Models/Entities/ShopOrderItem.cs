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

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public int Quantity { get; set; } = 1;
    }
}
