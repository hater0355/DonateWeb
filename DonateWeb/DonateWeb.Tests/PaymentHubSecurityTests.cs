using System.Security.Claims;
using DonateWeb.Data;
using DonateWeb.Hubs;
using DonateWeb.Models.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DonateWeb.Tests;

public sealed class PaymentHubSecurityTests
{
    [Fact]
    public async Task UserCannotJoinAnotherUsersWalletNotificationGroup()
    {
        await using var context = CreateContext();
        var groups = new Mock<IGroupManager>();
        var hub = CreateHub(context, new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "17")], "test")), groups.Object);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinUserGroup("18"));
        groups.Verify(manager => manager.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnonymousConnectionCannotJoinWalletNotificationGroup()
    {
        await using var context = CreateContext();
        var groups = new Mock<IGroupManager>();
        var hub = CreateHub(context, new ClaimsPrincipal(new ClaimsIdentity()), groups.Object);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinUserGroup("17"));
        groups.Verify(manager => manager.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;User Id=unused;Password=unused;TrustServerCertificate=True")
            .Options;
        return new AppDbContext(options);
    }

    private static PaymentHub CreateHub(AppDbContext context, ClaimsPrincipal user, IGroupManager groups)
    {
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(c => c.ConnectionId).Returns("test-connection");
        caller.SetupGet(c => c.User).Returns(user);
        return new PaymentHub(context, NullLogger<PaymentHub>.Instance)
        {
            Context = caller.Object,
            Groups = groups
        };
    }
}
