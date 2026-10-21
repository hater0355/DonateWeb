using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
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

        [Required]
        [MaxLength(80)]
        public string Category { get; set; } = "Khác";

        /// <summary>Danh sách size đã chọn, lưu JSON để tương thích danh mục linh hoạt.</summary>
        [Required]
        [MaxLength(1000)]
        public string AvailableSizesJson { get; set; } = "[]";

        /// <summary>Danh sách ảnh phụ của sản phẩm được lưu dưới dạng JSON.</summary>
        public string? AdditionalImageUrlsJson { get; set; }

        /// <summary>Giá, lựa chọn và tồn kho riêng cho từng biến thể của sản phẩm.</summary>
        public string VariantsJson { get; set; } = "[]";

        [NotMapped]
        public List<string> AvailableSizes
        {
            get => DeserializeStringList(AvailableSizesJson);
            set => AvailableSizesJson = JsonSerializer.Serialize(value ?? []);
        }

        [NotMapped]
        public List<string> AdditionalImageUrls
        {
            get => DeserializeStringList(AdditionalImageUrlsJson);
            set => AdditionalImageUrlsJson = JsonSerializer.Serialize(value ?? []);
        }

        [NotMapped]
        public List<ShopProductVariant> Variants
        {
            get
            {
                try
                {
                    return string.IsNullOrWhiteSpace(VariantsJson)
                        ? []
                        : JsonSerializer.Deserialize<List<ShopProductVariant>>(VariantsJson) ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            }
            set => VariantsJson = JsonSerializer.Serialize(value ?? []);
        }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 1000000000)]
        public decimal Price { get; set; }

        /// <summary>Khối lượng một sản phẩm đã đóng gói, tính bằng gram.</summary>
        [Range(1, 100000)]
        public int? WeightGrams { get; set; }

        /// <summary>Kích thước kiện của một sản phẩm đã đóng gói, tính bằng centimet.</summary>
        [Range(1, 200)]
        public int? LengthCm { get; set; }

        [Range(1, 200)]
        public int? WidthCm { get; set; }

        [Range(1, 200)]
        public int? HeightCm { get; set; }

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

        private static List<string> DeserializeStringList(string? json)
        {
            try
            {
                return string.IsNullOrWhiteSpace(json)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    /// <summary>Một lựa chọn bán được, ví dụ 1 hộp, 2 hộp hoặc một size áo.</summary>
    public class ShopProductVariant
    {
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public int StockQuantity { get; set; }

        /// <summary>Số sản phẩm vật lý trong lựa chọn, dùng để ước lượng phí vận chuyển.</summary>
        public int ShippingUnits { get; set; } = 1;
    }
}
