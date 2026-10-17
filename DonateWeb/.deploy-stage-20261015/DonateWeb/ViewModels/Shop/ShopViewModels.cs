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
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; }
        public int StockQuantity { get; set; }
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
    }

    public class CartItemInputModel
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
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

        [Required]
        public string PaymentMethod { get; set; } = "Wallet"; // "Wallet" | "VietQR"
    }

    public class ProductInputModel
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm")]
        [MaxLength(200, ErrorMessage = "Tên sản phẩm tối đa 200 ký tự")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập giá sản phẩm")]
        [Range(1000, 1000000000, ErrorMessage = "Giá sản phẩm từ 1,000 đến 1,000,000,000 VNĐ")]
        public decimal Price { get; set; }

        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public IFormFile? ImageFile { get; set; }
        public int StockQuantity { get; set; } = 999;
        public bool IsActive { get; set; } = true;
    }

    public class ProductManageViewModel
    {
        public int StreamerProfileId { get; set; }
        public string StreamerName { get; set; } = string.Empty;
        public List<ShopProduct> Products { get; set; } = new();
        public ProductInputModel NewProduct { get; set; } = new();
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public string StatusFilter { get; set; } = "all";
    }
}
