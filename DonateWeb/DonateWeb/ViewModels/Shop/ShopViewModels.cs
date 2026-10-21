using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using DonateWeb.Models.Entities;

namespace DonateWeb.ViewModels.Shop
{
    public class ShopProductCardViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = "Khác";
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public int StockQuantity { get; set; }
        public bool HasVariants { get; set; }
        public int StreamerProfileId { get; set; }
        public string StreamerName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string StreamerAvatar { get; set; } = "/images/default-avatar.png";
        public int StreamerRank { get; set; } = 1;
        public bool IsVerified { get; set; }
    }

    public class StreamerSelectOptionViewModel
    {
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ShopIndexViewModel
    {
        public List<ShopProductCardViewModel> Products { get; set; } = new();
        public string? SearchKeyword { get; set; }
        public string? SelectedStreamerSlug { get; set; }
        public string? SelectedCategory { get; set; }
        public string? SortBy { get; set; } // "newest", "price_asc", "price_desc"
        public List<StreamerSelectOptionViewModel> Streamers { get; set; } = new();
        public int TotalProducts { get; set; }
    }

    public class StreamerShopViewModel
    {
        public int StreamerId { get; set; }
        public string StreamerName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string StreamerAvatar { get; set; } = "/images/default-avatar.png";
        public string StreamerBanner { get; set; } = "/images/default-banner.jpg";
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? BankAccountName { get; set; }
        public bool IsOwner { get; set; }
        public List<ShopProduct> Products { get; set; } = new();
        public decimal ViewerWalletBalance { get; set; }
        public bool IsViewerAuthenticated { get; set; }
        public string? ViewerName { get; set; }
        public string? ViewerPhone { get; set; }
        public bool GhnShippingConfigured { get; set; }
    }

    public class CartItemInputModel
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;

        [MaxLength(50)]
        public string? Size { get; set; }
    }

    public class CheckoutRequestViewModel
    {
        [Required]
        public int StreamerProfileId { get; set; }

        [Required]
        public List<CartItemInputModel> Items { get; set; } = new();

        [Required(ErrorMessage = "Vui lòng nhập họ tên người nhận hàng")]
        public string BuyerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại liên hệ")]
        public string BuyerPhone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ nhận hàng")]
        public string BuyerAddress { get; set; } = string.Empty;

        public string? Note { get; set; }

        [MaxLength(50)]
        public string? CouponCode { get; set; }

        public int? GhnProvinceId { get; set; }
        public int? GhnWardId { get; set; }

        /// <summary>Phí đã hiển thị ở bước xem trước, chỉ dùng để phát hiện báo giá thay đổi.</summary>
        public long? QuotedShippingFee { get; set; }

        [Required]
        public string PaymentMethod { get; set; } = "Wallet"; // "Wallet" | "VietQR"
    }

    public class CouponPreviewRequestViewModel
    {
        [Required]
        public int StreamerProfileId { get; set; }

        [Required]
        public List<CartItemInputModel> Items { get; set; } = new();

        [Required, MaxLength(50)]
        public string CouponCode { get; set; } = string.Empty;
    }

    public class GhnFeePreviewRequestViewModel
    {
        [Required]
        public int StreamerProfileId { get; set; }

        [Required]
        public List<CartItemInputModel> Items { get; set; } = new();

        [Required, MaxLength(200)]
        public string BuyerName { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string BuyerPhone { get; set; } = string.Empty;

        [Required, MaxLength(500)]
        public string BuyerStreet { get; set; } = string.Empty;

        [Range(1, int.MaxValue)]
        public int ProvinceId { get; set; }

        [Range(1, int.MaxValue)]
        public int WardId { get; set; }
    }

    public class ProductInputModel
    {
        public static IReadOnlyList<string> Categories => ShopProductCategories.All;
        public static IReadOnlyList<string> ClothingSizes => ShopProductCategories.ClothingSizes;
        public static IReadOnlyList<string> FootwearSizes => ShopProductCategories.FootwearSizes;

        public int? Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm")]
        [MaxLength(200, ErrorMessage = "Tên sản phẩm tối đa 200 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn danh mục sản phẩm")]
        [MaxLength(80)]
        public string Category { get; set; } = "Khác";

        public List<string> AvailableSizes { get; set; } = new();

        [Range(0, 1000000000, ErrorMessage = "Giá sản phẩm không được vượt quá 1.000.000.000 VNĐ")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập cân nặng sau khi đóng gói")]
        [Range(1, 100000, ErrorMessage = "Cân nặng phải từ 1 đến 100.000 gram")]
        public int? WeightGrams { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập chiều dài kiện hàng")]
        [Range(1, 200, ErrorMessage = "Chiều dài phải từ 1 đến 200 cm")]
        public int? LengthCm { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập chiều rộng kiện hàng")]
        [Range(1, 200, ErrorMessage = "Chiều rộng phải từ 1 đến 200 cm")]
        public int? WidthCm { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập chiều cao kiện hàng")]
        [Range(1, 200, ErrorMessage = "Chiều cao phải từ 1 đến 200 cm")]
        public int? HeightCm { get; set; }

        [MaxLength(2000, ErrorMessage = "Mô tả tối đa 2.000 ký tự")]
        public string? Description { get; set; }

        [MaxLength(500, ErrorMessage = "Đường dẫn ảnh tối đa 500 ký tự")]
        public string? ImageUrl { get; set; }
        public IFormFile? ImageFile { get; set; }

        public List<IFormFile> AdditionalImageFiles { get; set; } = new();
        public List<string> ExistingAdditionalImageUrls { get; set; } = new();
        public List<string> RemoveAdditionalImageUrls { get; set; } = new();
        [Range(0, 999999, ErrorMessage = "Tồn kho phải nằm trong khoảng 0 đến 999.999 sản phẩm")]
        public int StockQuantity { get; set; } = 999;
        public bool IsActive { get; set; } = true;
        public List<ProductVariantInputModel> Variants { get; set; } = new();
    }

    public class ProductVariantInputModel
    {
        [Required(ErrorMessage = "Vui lòng nhập tên lựa chọn")]
        [MaxLength(50, ErrorMessage = "Tên lựa chọn tối đa 50 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Range(1000, 1000000000, ErrorMessage = "Giá lựa chọn từ 1.000 đến 1.000.000.000 VNĐ")]
        public decimal Price { get; set; }

        [Range(0, 999999, ErrorMessage = "Tồn kho lựa chọn từ 0 đến 999.999")]
        public int StockQuantity { get; set; }

        [Range(1, 100, ErrorMessage = "Số lượng gói tính vận chuyển từ 1 đến 100")]
        public int ShippingUnits { get; set; } = 1;
    }

    public class ProductManageViewModel
    {
        public int StreamerProfileId { get; set; }
        public string StreamerName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public List<ShopProduct> Products { get; set; } = new();
        public ProductInputModel NewProduct { get; set; } = new();
        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public int DeletedCount { get; set; }
        public string StatusFilter { get; set; } = "all";
    }
}
