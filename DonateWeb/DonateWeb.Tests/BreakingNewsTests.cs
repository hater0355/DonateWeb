using System;
using System.Collections.Generic;
using System.Linq;
using DonateWeb.Models.Entities;
using Xunit;

namespace DonateWeb.Tests;

public class BreakingNewsTests
{
    [Fact]
    public void DefaultBreakingNews_IsActiveByDefault()
    {
        var item = new BreakingNews
        {
            Content = "Thông báo hệ thống bảo trì",
            CreatedBy = "Admin"
        };

        Assert.True(item.IsActive);
        Assert.Equal("Thông báo hệ thống bảo trì", item.Content);
        Assert.Null(item.LinkUrl);
        Assert.Equal("Admin", item.CreatedBy);
    }

    [Fact]
    public void ActiveBreakingNewsQuery_ReturnsNullWhenNoNewsOrAllInactive()
    {
        var list = new List<BreakingNews>
        {
            new BreakingNews { Id = 1, Content = "Tin cũ 1", IsActive = false },
            new BreakingNews { Id = 2, Content = "Tin cũ 2", IsActive = false },
            new BreakingNews { Id = 3, Content = "   ", IsActive = true } // whitespace only
        };

        var activeNews = list
            .Where(b => b.IsActive && !string.IsNullOrWhiteSpace(b.Content))
            .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .FirstOrDefault();

        // Should be null -> Breaking news để trống
        Assert.Null(activeNews);
    }

    [Fact]
    public void ActiveBreakingNewsQuery_ReturnsLatestActiveItem()
    {
        var now = DateTime.UtcNow;
        var list = new List<BreakingNews>
        {
            new BreakingNews { Id = 1, Content = "Tin 1", IsActive = true, CreatedAt = now.AddMinutes(-10) },
            new BreakingNews { Id = 2, Content = "Tin 2 mới nhất", IsActive = true, CreatedAt = now, UpdatedAt = now }
        };

        var activeNews = list
            .Where(b => b.IsActive && !string.IsNullOrWhiteSpace(b.Content))
            .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .FirstOrDefault();

        Assert.NotNull(activeNews);
        Assert.Equal("Tin 2 mới nhất", activeNews.Content);
    }
}
