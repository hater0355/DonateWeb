using System;
using System.Collections.Generic;
using DonateWeb.Models.Enums;

namespace DonateWeb.ViewModels.Orders
{
    public class OrdersHistoryViewModel
    {
        public bool IsStreamer { get; set; }
        public int CurrentUserId { get; set; }
        public string CurrentUserName { get; set; } = string.Empty;
        public string CurrentUserAvatar { get; set; } = "/images/default-avatar.png";
        public string? StreamerDisplayName { get; set; }
        public string? StreamerSlug { get; set; }

        // Bộ lọc tìm kiếm
        public string ActiveTab { get; set; } = "all";
        public string? SearchKeyword { get; set; }
        public string? SelectedType { get; set; } = "ALL";
        public string? SelectedStatus { get; set; } = "ALL";
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }

        // Chỉ số tổng quan (KPIs)
        public decimal TotalDonationSentAmount { get; set; }
        public int TotalDonationSentCount { get; set; }

        public decimal TotalDonationReceivedAmount { get; set; }
        public int TotalDonationReceivedCount { get; set; }

        public decimal TotalShopAmount { get; set; }
        public int TotalShopOrdersCount { get; set; }

        public int TotalEffectsCount { get; set; }

        public string? LatestReceivedDonorName { get; set; }
        public decimal? LatestReceivedAmount { get; set; }
        public string? LatestReceivedMessage { get; set; }

        // Danh sách Fan Donate cho Streamer (AI ĐÃ DONATE CHO MÌNH & LỜI NHẮN LÀ GÌ)
        public List<DonationReceivedItemViewModel> ReceivedDonations { get; set; } = new();

        // Danh sách Viewer đã Donate cho Streamer (ĐÃ DONATE CHO AI)
        public List<DonationSentItemViewModel> SentDonations { get; set; } = new();

        // Danh sách Đơn hàng mua sắm Shop Streamer
        public List<ShopOrderHistoryItemViewModel> ShopOrders { get; set; } = new();

        // Danh sách Hiệu ứng đã kích hoạt / mua
        public List<EffectHistoryItemViewModel> EffectItems { get; set; } = new();
    }

    public class DonationReceivedItemViewModel
    {
        public int Id { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public string DonorName { get; set; } = "Người ủng hộ ẩn danh";
        public string? DonorAvatar { get; set; }
        public string? DonorAccountId { get; set; }
        public decimal Amount { get; set; }
        public string? Message { get; set; } // LỜI NHẮN CỦA VIEWER
        public DateTime CreatedAt { get; set; }
        public string PaymentMethodName { get; set; } = "Ví số dư";
        public DonationStatus Status { get; set; }
        public string StatusBadgeClass { get; set; } = "bg-success";
        public string StatusText { get; set; } = "Thành công";
        public string? EffectTag { get; set; }
    }

    public class DonationSentItemViewModel
    {
        public int Id { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public string StreamerDisplayName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string StreamerAvatar { get; set; } = "/images/default-avatar.png";
        public decimal Amount { get; set; }
        public string? Message { get; set; } // LỜI NHẮN ĐÃ GỬI
        public DateTime CreatedAt { get; set; }
        public string PaymentMethodName { get; set; } = "Ví số dư";
        public DonationStatus Status { get; set; }
        public string StatusBadgeClass { get; set; } = "bg-success";
        public string StatusText { get; set; } = "Thành công";
        public string? EffectTag { get; set; }
    }

    public class ShopOrderHistoryItemViewModel
    {
        public int Id { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public string StreamerDisplayName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string BuyerName { get; set; } = string.Empty;
        public string? BuyerPhone { get; set; }
        public string? BuyerAddress { get; set; }
        public string ItemsSummary { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string PaymentMethod { get; set; } = "Wallet";
        public string PaymentStatus { get; set; } = "Paid";
        public DateTime CreatedAt { get; set; }
        public bool IsStreamerSale { get; set; }
    }

    public class EffectHistoryItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string IconClass { get; set; } = "fa-wand-magic-sparkles";
        public decimal Price { get; set; }
        public string TargetStreamer { get; set; } = string.Empty;
        public DateTime PurchasedAt { get; set; }
        public string Status { get; set; } = "Đang kích hoạt";
    }
}
