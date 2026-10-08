using System;
using System.Collections.Generic;
using System.Linq;
using DonateWeb.Models.Entities;
using Xunit;

namespace DonateWeb.Tests;

public class StreamerFollowTests
{
    [Fact]
    public void StreamerFollow_PropertiesInitializedCorrectly()
    {
        var follow = new StreamerFollow
        {
            Id = 1,
            UserId = 10,
            StreamerProfileId = 5,
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal(10, follow.UserId);
        Assert.Equal(5, follow.StreamerProfileId);
        Assert.True(follow.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void FollowedStreamersQuery_FiltersOnlyStreamersFollowedByUser()
    {
        var currentUserId = 100;
        var streamerA = new StreamerProfile { Id = 1, DisplayName = "Streamer A", Slug = "streamer-a", IsActive = true };
        var streamerB = new StreamerProfile { Id = 2, DisplayName = "Streamer B", Slug = "streamer-b", IsActive = true };
        var streamerC = new StreamerProfile { Id = 3, DisplayName = "Streamer C", Slug = "streamer-c", IsActive = true };

        var follows = new List<StreamerFollow>
        {
            new StreamerFollow { UserId = currentUserId, StreamerProfileId = streamerA.Id, StreamerProfile = streamerA },
            new StreamerFollow { UserId = currentUserId, StreamerProfileId = streamerC.Id, StreamerProfile = streamerC },
            new StreamerFollow { UserId = 999, StreamerProfileId = streamerB.Id, StreamerProfile = streamerB } // Followed by someone else
        };

        var myFollowedStreamers = follows
            .Where(f => f.UserId == currentUserId && f.StreamerProfile.IsActive)
            .Select(f => f.StreamerProfile)
            .ToList();

        // Should ONLY have Streamer A and Streamer C
        Assert.Equal(2, myFollowedStreamers.Count);
        Assert.Contains(myFollowedStreamers, s => s.Id == streamerA.Id);
        Assert.Contains(myFollowedStreamers, s => s.Id == streamerC.Id);
        Assert.DoesNotContain(myFollowedStreamers, s => s.Id == streamerB.Id);
    }

    [Fact]
    public void FollowedStreamersQuery_ReturnsEmptyWhenUserHasNotFollowedAnyone()
    {
        var currentUserId = 55;
        var streamerA = new StreamerProfile { Id = 1, DisplayName = "Streamer A", IsActive = true };

        var follows = new List<StreamerFollow>
        {
            new StreamerFollow { UserId = 99, StreamerProfileId = streamerA.Id, StreamerProfile = streamerA }
        };

        var myFollowedStreamers = follows
            .Where(f => f.UserId == currentUserId && f.StreamerProfile.IsActive)
            .Select(f => f.StreamerProfile)
            .ToList();

        Assert.Empty(myFollowedStreamers);
    }
}
