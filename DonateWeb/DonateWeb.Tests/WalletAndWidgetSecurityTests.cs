using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DonateWeb.Areas.Widgets.Controllers;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Controllers;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;
using DonateWeb.Security.Uploads;
using DonateWeb.Services;
using DonateWeb.Tests;
using DonateWeb.ViewModels.Streamer;
using Xunit;

namespace DonateWeb.Tests;

public class WalletAndWidgetSecurityTests
{
    [SqlServerFact]
    public async Task BankTransferCannotBeCreditedByDirectDonationSubmission()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var streamerUser = new User { Username = $"streamer-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        var donor = new User { Username = $"donor-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test", WalletBalance = 90000m };
        database.Context.Users.AddRange(streamerUser, donor);
        await database.Context.SaveChangesAsync();
        var profile = new StreamerProfile
        {
            UserId = streamerUser.Id,
            Slug = $"streamer-{Guid.NewGuid():N}",
            DisplayName = "Test streamer",
            ApprovalStatus = StreamerApprovalStatus.Approved,
            MinDonateAmount = 10000m
        };
        database.Context.StreamerProfiles.Add(profile);
        await database.Context.SaveChangesAsync();

        var service = new StreamerService(
            database.Context,
            Mock.Of<IPasswordHasher>(),
            Mock.Of<IContentModerationService>(),
            Mock.Of<IAccountIdService>());

        var result = await service.ProcessDonationAsync(donor.Id, new StreamerDonateViewModel
        {
            StreamerProfileId = profile.Id,
            Amount = 50000m,
            DonorName = "Test donor",
            PaymentMethod = PaymentMethod.BankTransfer
        });

        Assert.False(result.Success);
        Assert.Null(result.Donation);
        await database.Context.Entry(donor).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        Assert.Equal(90000m, donor.WalletBalance);
        Assert.Equal(0m, profile.TotalReceived);
        Assert.Empty(await database.Context.Donations.ToListAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentWalletDonationsCannotSpendTheSameBalanceTwice()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var streamerUser = new User { Username = $"streamer-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        var donor = new User { Username = $"donor-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test", WalletBalance = 90000m };
        database.Context.Users.AddRange(streamerUser, donor);
        await database.Context.SaveChangesAsync();
        var profile = new StreamerProfile
        {
            UserId = streamerUser.Id,
            Slug = $"streamer-{Guid.NewGuid():N}",
            DisplayName = "Concurrent test streamer",
            ApprovalStatus = StreamerApprovalStatus.Approved,
            MinDonateAmount = 10000m
        };
        database.Context.StreamerProfiles.Add(profile);
        await database.Context.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreateStreamerService(firstContext);
        var secondService = CreateStreamerService(secondContext);
        var firstTask = firstService.ProcessDonationAsync(donor.Id, CreateWalletDonation(profile.Id, 60000m));
        var secondTask = secondService.ProcessDonationAsync(donor.Id, CreateWalletDonation(profile.Id, 60000m));

        var results = await Task.WhenAll(firstTask, secondTask);

        await database.Context.Entry(donor).ReloadAsync();
        await database.Context.Entry(streamerUser).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        Assert.Equal(30000m, donor.WalletBalance);
        Assert.Equal(60000m, streamerUser.WalletBalance);
        Assert.Equal(60000m, profile.TotalReceived);
        Assert.Single(await database.Context.Donations.ToListAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentDonationsPreserveStreamerBalanceAndReceivedTotal()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var streamerUser = new User { Username = $"streamer-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        var firstDonor = new User { Username = $"donor1-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test", WalletBalance = 60000m };
        var secondDonor = new User { Username = $"donor2-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test", WalletBalance = 60000m };
        database.Context.Users.AddRange(streamerUser, firstDonor, secondDonor);
        await database.Context.SaveChangesAsync();
        var profile = new StreamerProfile
        {
            UserId = streamerUser.Id,
            Slug = $"streamer-{Guid.NewGuid():N}",
            DisplayName = "Concurrent received total streamer",
            ApprovalStatus = StreamerApprovalStatus.Approved,
            MinDonateAmount = 10000m
        };
        database.Context.StreamerProfiles.Add(profile);
        await database.Context.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstTask = CreateStreamerService(firstContext).ProcessDonationAsync(firstDonor.Id, CreateWalletDonation(profile.Id, 60000m));
        var secondTask = CreateStreamerService(secondContext).ProcessDonationAsync(secondDonor.Id, CreateWalletDonation(profile.Id, 60000m));

        var results = await Task.WhenAll(firstTask, secondTask);

        await database.Context.Entry(streamerUser).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        Assert.All(results, result => Assert.True(result.Success, result.Error));
        Assert.Equal(120000m, streamerUser.WalletBalance);
        Assert.Equal(120000m, profile.TotalReceived);
        Assert.Equal(2, await database.Context.Donations.CountAsync());
    }

    [Fact]
    public async Task AvatarUploadRejectsUnapprovedExtensionsAndMismatchedFileContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"donateweb-avatar-test-{Guid.NewGuid():N}");
        try
        {
            var html = new FormFile(new MemoryStream("<script>alert(1)</script>"u8.ToArray()), 0, 25, "avatar", "avatar.svg");
            var fakePng = new FormFile(new MemoryStream("not an image"u8.ToArray()), 0, 12, "avatar", "avatar.png");

            var unsupported = await AvatarUploadService.SaveAsync(html, root, 1);
            var mismatched = await AvatarUploadService.SaveAsync(fakePng, root, 1);

            Assert.Null(unsupported.Path);
            Assert.NotNull(unsupported.Error);
            Assert.Null(mismatched.Path);
            Assert.NotNull(mismatched.Error);
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task WidgetConfigurationRejectsAnotherStreamersSlug()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var owner = new User { Username = $"owner-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        database.Context.Users.Add(owner);
        await database.Context.SaveChangesAsync();
        var profile = new StreamerProfile
        {
            UserId = owner.Id,
            Slug = $"owner-{Guid.NewGuid():N}",
            DisplayName = "Widget owner"
        };
        database.Context.StreamerProfiles.Add(profile);
        await database.Context.SaveChangesAsync();

        var widgetService = new Mock<IWidgetService>();
        var controller = new WidgetsConfigController(widgetService.Object, NullLogger<WidgetsConfigController>.Instance, database.Context,
            Mock.Of<Microsoft.AspNetCore.SignalR.IHubContext<DonateWeb.Hubs.PaymentHub>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = CreateStreamerPrincipal(owner.Id)
                }
            }
        };

        Assert.IsType<ForbidResult>(await controller.AlertBox("another-streamer"));
        var post = await controller.AlertBox(new AlertBoxConfigViewModel { StreamerSlug = "another-streamer" });
        Assert.IsType<ForbidResult>(post);
        widgetService.Verify(service => service.GetAlertBoxConfigAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        widgetService.Verify(service => service.SaveAlertBoxConfigAsync(It.IsAny<AlertBoxConfigViewModel>()), Times.Never);
    }

    [SqlServerFact]
    public async Task WidgetTestAlertApiRejectsNonOwnerSlug()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var owner = new User { Username = $"owner-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        database.Context.Users.Add(owner);
        await database.Context.SaveChangesAsync();
        database.Context.StreamerProfiles.Add(new StreamerProfile
        {
            UserId = owner.Id,
            Slug = $"owned-{Guid.NewGuid():N}",
            DisplayName = "Widget owner"
        });
        await database.Context.SaveChangesAsync();

        var widgetService = new Mock<IWidgetService>();
        var controller = new WidgetsApiController(
            widgetService.Object,
            NullLogger<WidgetsApiController>.Instance,
            database.Context,
            Mock.Of<IContentModerationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = CreateStreamerPrincipal(owner.Id)
                }
            }
        };

        Assert.IsType<ForbidResult>(await controller.TriggerTestAlert("someone-else"));
        widgetService.Verify(service => service.TriggerTestAlertAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WidgetMutationEndpointsRequireStreamerRoleAndAntiforgery()
    {
        var controllerAuthorization = typeof(WidgetsConfigController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(controllerAuthorization);
        Assert.Equal(UserRoles.Streamer, controllerAuthorization.Roles);
        Assert.NotNull(typeof(WidgetsConfigController).GetMethod(nameof(WidgetsConfigController.TriggerTestAlert))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());

        var apiAction = typeof(WidgetsApiController).GetMethod(nameof(WidgetsApiController.TriggerTestAlert))!;
        Assert.NotNull(apiAction.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(apiAction.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void LogoutIsOnlyAvailableAsAntiforgeryProtectedPost()
    {
        Assert.Null(typeof(AuthController).GetMethod("LogoutGet"));
        Assert.NotNull(typeof(AuthController).GetMethod(nameof(AuthController.Logout))!
            .GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(typeof(AuthController).GetMethod(nameof(AuthController.Logout))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static ClaimsPrincipal CreateStreamerPrincipal(int userId) => new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, UserRoles.Streamer)
        }, "integration-test"));

    private static StreamerService CreateStreamerService(AppDbContext context)
    {
        var moderation = new Mock<IContentModerationService>();
        moderation.Setup(service => service.ModerateContent(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((string? message, string? _) => new ModerationResult { SanitizedText = message ?? string.Empty });
        moderation.Setup(service => service.SanitizeForStream(It.IsAny<string?>()))
            .Returns((string? value) => value ?? string.Empty);

        return new StreamerService(context, Mock.Of<IPasswordHasher>(), moderation.Object, Mock.Of<IAccountIdService>());
    }

    private static StreamerDonateViewModel CreateWalletDonation(int profileId, decimal amount) => new()
    {
        StreamerProfileId = profileId,
        Amount = amount,
        DonorName = "Test donor",
        PaymentMethod = PaymentMethod.Wallet
    };
}
