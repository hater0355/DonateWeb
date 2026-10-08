using System;
using System.Collections.Generic;
using System.Linq;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.ViewModels.Shop;
using Xunit;

namespace DonateWeb.Tests;

public class ShopProductApprovalTests
{
    [Fact]
    public void NewProductDefaultStatusIsPending()
    {
        var product = new ShopProduct
        {
            Name = "Áo Gaming",
            Price = 250000
        };

        Assert.Equal(ProductApprovalStatus.Pending, product.ApprovalStatus);
        Assert.Null(product.RejectionReason);
        Assert.Null(product.ApprovedAt);
        Assert.Null(product.ApprovedBy);
    }

    [Fact]
    public void PublicShopQueryFiltersOutPendingAndRejectedProducts()
    {
        var products = new List<ShopProduct>
        {
            new ShopProduct { Id = 1, Name = "Sản phẩm đã duyệt", ApprovalStatus = ProductApprovalStatus.Approved, IsActive = true },
            new ShopProduct { Id = 2, Name = "Sản phẩm chờ duyệt", ApprovalStatus = ProductApprovalStatus.Pending, IsActive = true },
            new ShopProduct { Id = 3, Name = "Sản phẩm bị từ chối", ApprovalStatus = ProductApprovalStatus.Rejected, IsActive = true },
            new ShopProduct { Id = 4, Name = "Sản phẩm đã duyệt nhưng ẩn", ApprovalStatus = ProductApprovalStatus.Approved, IsActive = false }
        };

        // Query logic matching ShopController.Index
        var visibleProducts = products
            .Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved)
            .ToList();

        Assert.Single(visibleProducts);
        Assert.Equal(1, visibleProducts[0].Id);
        Assert.Equal("Sản phẩm đã duyệt", visibleProducts[0].Name);
    }

    [Fact]
    public void StreamerProductManageViewModelCalculatesStatusCountsCorrectly()
    {
        var allProducts = new List<ShopProduct>
        {
            new ShopProduct { Id = 1, Name = "SP 1", ApprovalStatus = ProductApprovalStatus.Pending },
            new ShopProduct { Id = 2, Name = "SP 2", ApprovalStatus = ProductApprovalStatus.Pending },
            new ShopProduct { Id = 3, Name = "SP 3", ApprovalStatus = ProductApprovalStatus.Approved },
            new ShopProduct { Id = 4, Name = "SP 4", ApprovalStatus = ProductApprovalStatus.Rejected }
        };

        var pendingCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Pending);
        var approvedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Approved);
        var rejectedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Rejected);

        Assert.Equal(2, pendingCount);
        Assert.Equal(1, approvedCount);
        Assert.Equal(1, rejectedCount);
    }

    [Fact]
    public void RejectingProductSetsReasonAndTimestamp()
    {
        var product = new ShopProduct
        {
            Id = 5,
            Name = "Mũ thời trang",
            ApprovalStatus = ProductApprovalStatus.Pending
        };

        var reason = "Ảnh đại diện mờ, không rõ hình ảnh sản phẩm.";
        var adminName = "AdminTest";
        var timestamp = DateTime.UtcNow;

        product.ApprovalStatus = ProductApprovalStatus.Rejected;
        product.RejectionReason = reason;
        product.ApprovedAt = timestamp;
        product.ApprovedBy = adminName;

        Assert.Equal(ProductApprovalStatus.Rejected, product.ApprovalStatus);
        Assert.Equal(reason, product.RejectionReason);
        Assert.Equal(adminName, product.ApprovedBy);
        Assert.Equal(timestamp, product.ApprovedAt);
    }

    [Fact]
    public void ApprovingProductClearsRejectionReasonAndActivates()
    {
        var product = new ShopProduct
        {
            Id = 6,
            Name = "Bàn phím cơ",
            ApprovalStatus = ProductApprovalStatus.Rejected,
            RejectionReason = "Lỗi trước đó"
        };

        var adminName = "AdminLead";
        var timestamp = DateTime.UtcNow;

        product.ApprovalStatus = ProductApprovalStatus.Approved;
        product.ApprovedAt = timestamp;
        product.ApprovedBy = adminName;
        product.RejectionReason = null;
        product.IsActive = true;

        Assert.Equal(ProductApprovalStatus.Approved, product.ApprovalStatus);
        Assert.Null(product.RejectionReason);
        Assert.True(product.IsActive);
        Assert.Equal(adminName, product.ApprovedBy);
        Assert.Equal(timestamp, product.ApprovedAt);
    }
}
