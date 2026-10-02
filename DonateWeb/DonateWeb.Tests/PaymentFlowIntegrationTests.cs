using System.Security.Claims;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using DonateWeb.Controllers;
using DonateWeb.Data;
using DonateWeb.Hubs;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.Webhook;
using DonateWeb.Services;
using Xunit;

namespace DonateWeb.Tests;

public class PaymentFlowIntegrationTests
{
    [SqlServerFact]
    public async Task ValidWebhookCreditsExactAmountOnlyOnce()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        var controller = CreateWebhookController(database.Context, transaction.OrderCode!.Value, 50000);

        await controller.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 50000));
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(transaction).ReloadAsync();

        Assert.Equal(50000m, user.WalletBalance);
        Assert.Equal(WalletTransaction.StatusCompleted, transaction.Status);

        await controller.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 50000));
        await database.Context.Entry(user).ReloadAsync();
        Assert.Equal(50000m, user.WalletBalance);
        Assert.Single(await database.Context.WalletTransactionAuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentDuplicateWebhooksCreditWalletOnlyOnce()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = CreateWebhookController(firstContext, transaction.OrderCode!.Value, 50000);
        var second = CreateWebhookController(secondContext, transaction.OrderCode.Value, 50000);

        await Task.WhenAll(
            first.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 50000)),
            second.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 50000)));

        await database.Context.Entry(user).ReloadAsync();
        Assert.Equal(50000m, user.WalletBalance);
        Assert.Single(await database.Context.WalletTransactionAuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task AmountMismatchIsAuditedAndDoesNotCreditWallet()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        var controller = CreateWebhookController(database.Context, transaction.OrderCode!.Value, 40000);

        var result = await controller.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 40000));
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(transaction).ReloadAsync();
        var audit = await database.Context.WalletTransactionAuditLogs.SingleAsync();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(0m, user.WalletBalance);
        Assert.Equal(WalletTransaction.StatusPending, transaction.Status);
        Assert.Equal("PAYOS_AMOUNT_MISMATCH", audit.ActionType);
        Assert.Equal(50000m, audit.ExpectedAmount);
        Assert.Equal(40000m, audit.ReceivedAmount);
    }

    [SqlServerFact]
    public async Task DatabaseFailureReturnsRetryableServerErrorWithoutPartialCredit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        await database.Context.Database.ExecuteSqlRawAsync("DROP TABLE dbo.WalletTransactionAuditLogs");
        var controller = CreateWebhookController(database.Context, transaction.OrderCode!.Value, 50000);

        var result = Assert.IsType<ObjectResult>(await controller.HandlePayOSWebhook(CreateWebhook(transaction.OrderCode.Value, 50000)));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(transaction).ReloadAsync();
        Assert.Equal(0m, user.WalletBalance);
        Assert.Equal(WalletTransaction.StatusPending, transaction.Status);
    }

    [SqlServerFact]
    public async Task DonationAmountMismatchIsHeldAndRepeatedCallbackDoesNotDuplicateAudit()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, profile, donation, orderCode) = await SeedPendingDonationAsync(database.Context, 30000m);
        var controller = CreateWebhookController(database.Context, orderCode, 25000);

        await controller.HandlePayOSWebhook(CreateWebhook(orderCode, 25000));
        await controller.HandlePayOSWebhook(CreateWebhook(orderCode, 25000));
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        await database.Context.Entry(donation).ReloadAsync();

        Assert.Equal(0m, user.WalletBalance);
        Assert.Equal(0m, profile.TotalReceived);
        Assert.Equal(DonationStatus.Pending, donation.Status);
        Assert.True(donation.IsDisputed);
        Assert.Single(await database.Context.TransactionAuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task SuccessfulDonationCallbackCreditsExactlyOnce()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, profile, donation, orderCode) = await SeedPendingDonationAsync(database.Context, 30000m);
        var controller = CreateWebhookController(database.Context, orderCode, 30000);

        await controller.HandlePayOSWebhook(CreateWebhook(orderCode, 30000));
        await controller.HandlePayOSWebhook(CreateWebhook(orderCode, 30000));
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        await database.Context.Entry(donation).ReloadAsync();

        Assert.Equal(30000m, user.WalletBalance);
        Assert.Equal(30000m, profile.TotalReceived);
        Assert.Equal(DonationStatus.Success, donation.Status);
        Assert.Single(await database.Context.TransactionAuditLogs.ToListAsync());
    }

    [SqlServerFact]
    public async Task LegacyWebhookRejectsUnsignedPaymentPayloadWithoutCreditingDonation()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, profile, donation, orderCode) = await SeedPendingDonationAsync(database.Context, 30000m);
        var controller = CreateWebhookController(database.Context, orderCode, 30000);
        var forgedPayload = new PaymentWebhookPayload
        {
            Code = "00",
            Status = "SUCCESS",
            Data = new PayOSWebhookData
            {
                OrderCode = orderCode,
                Amount = 30000,
                Description = donation.TransactionCode!
            }
        };

        var result = await controller.ProcessPaymentWebhook("payos", forgedPayload);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await database.Context.Entry(user).ReloadAsync();
        await database.Context.Entry(profile).ReloadAsync();
        await database.Context.Entry(donation).ReloadAsync();
        Assert.Equal(0m, user.WalletBalance);
        Assert.Equal(0m, profile.TotalReceived);
        Assert.Equal(DonationStatus.Pending, donation.Status);
    }

    [SqlServerFact]
    public async Task PaymentStatusEndpointsHideOtherUsersTransactions()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (_, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        var controller = CreateWalletController(database.Context, transaction.UserId + 1, "Development");

        Assert.IsType<NotFoundObjectResult>(await controller.CheckStatus(transaction.OrderCode!.Value));
        Assert.IsType<NotFoundObjectResult>(await controller.CheckDepositStatus(transaction.Id, null));
    }

    [SqlServerFact]
    public async Task WebhookCheckStatusRequiresAuthenticationAndScopesDepositToOwner()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 50000m);
        var unauthenticated = CreateWebhookController(database.Context, transaction.OrderCode!.Value, 50000);
        unauthenticated.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var owner = CreateWebhookController(database.Context, transaction.OrderCode.Value, 50000);
        owner.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(user.Id) };
        var otherUser = CreateWebhookController(database.Context, transaction.OrderCode.Value, 50000);
        otherUser.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(user.Id + 1) };

        Assert.IsType<UnauthorizedObjectResult>(await unauthenticated.CheckStatus(transaction.TransactionCode));
        Assert.IsType<OkObjectResult>(await owner.CheckStatus(transaction.TransactionCode));
        Assert.IsType<NotFoundObjectResult>(await otherUser.CheckStatus(transaction.TransactionCode));
    }

    [SqlServerFact]
    public async Task PaymentSimulationIsDevelopmentOnlyAndOwnerScoped()
    {
        await using var database = await SqlServerDatabase.CreateAsync();
        var (user, transaction) = await SeedPendingDepositAsync(database.Context, 25000m);

        var production = CreateWalletController(database.Context, user.Id, "Production");
        Assert.IsType<NotFoundResult>(await production.SimulateSuccess(transaction.OrderCode, null));

        var development = CreateWalletController(database.Context, user.Id, "Development");
        var result = await development.SimulateSuccess(transaction.OrderCode, null);

        Assert.IsType<JsonResult>(result);
        await database.Context.Entry(user).ReloadAsync();
        Assert.Equal(25000m, user.WalletBalance);

        var otherUser = CreateWalletController(database.Context, user.Id + 1, "Development");
        Assert.IsType<NotFoundObjectResult>(await otherUser.SimulateSuccess(transaction.OrderCode, null));

        var withdrawal = new WalletTransaction
        {
            UserId = user.Id,
            TransactionCode = $"WDR_{Guid.NewGuid():N}",
            TransactionType = "WITHDRAW",
            Amount = 10000m,
            Status = WalletTransaction.StatusPending
        };
        database.Context.WalletTransactions.Add(withdrawal);
        await database.Context.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(await development.SimulateSuccess(null, withdrawal.Id));
    }

    [Fact]
    public void MutatingWalletActionsRequireAntiforgeryValidation()
    {
        Assert.NotNull(typeof(WalletController).GetMethod(nameof(WalletController.CreateDeposit))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(WalletController).GetMethod(nameof(WalletController.SimulateSuccess))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static async Task<(User User, WalletTransaction Transaction)> SeedPendingDepositAsync(AppDbContext context, decimal amount)
    {
        var user = new User { Username = $"viewer-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var transaction = new WalletTransaction
        {
            UserId = user.Id,
            OrderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TransactionCode = $"DEP_{Guid.NewGuid():N}",
            TransactionType = "DEPOSIT",
            Amount = amount,
            Status = WalletTransaction.StatusPending
        };
        context.WalletTransactions.Add(transaction);
        await context.SaveChangesAsync();
        return (user, transaction);
    }

    private static async Task<(User User, StreamerProfile Profile, Donation Donation, long OrderCode)> SeedPendingDonationAsync(AppDbContext context, decimal amount)
    {
        var user = new User { Username = $"streamer-{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var profile = new StreamerProfile { UserId = user.Id, Slug = "", DisplayName = "Integration Streamer" };
        context.StreamerProfiles.Add(profile);
        await context.SaveChangesAsync();

        var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var donation = new Donation
        {
            StreamerProfileId = profile.Id,
            DonorName = "Integration Donor",
            Amount = amount,
            Status = DonationStatus.Pending,
            TransactionCode = $"DON_{orderCode}"
        };
        context.Donations.Add(donation);
        await context.SaveChangesAsync();
        return (user, profile, donation, orderCode);
    }

    private static WalletController CreateWalletController(AppDbContext context, int userId, string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        var controller = new WalletController(
            context,
            CreateAuthService(),
            NullLogger<WalletController>.Instance,
            Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>(),
            Mock.Of<IPayOSService>(),
            environment.Object);
        var httpContext = CreateHttpContext(userId);
        var authentication = new Mock<IAuthenticationService>();
        authentication.Setup(service => service.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton(authentication.Object)
            .BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static IAuthService CreateAuthService()
    {
        var authService = new Mock<IAuthService>();
        authService.Setup(service => service.GetUserRolesAsync(It.IsAny<int>())).ReturnsAsync(new List<string>());
        return authService.Object;
    }

    private static PaymentWebhookController CreateWebhookController(AppDbContext context, long orderCode, int amount)
    {
        var payOs = new Mock<IPayOSService>();
        payOs.Setup(service => service.verifyPaymentWebhookData(It.IsAny<WebhookType>()))
            .Returns(() => new WebhookData { orderCode = orderCode, amount = amount, reference = "test-ref" });

        var hubClient = new Mock<IClientProxy>();
        hubClient.Setup(client => client.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(clients => clients.Group(It.IsAny<string>())).Returns(hubClient.Object);
        var hubContext = new Mock<IHubContext<PaymentHub>>();
        hubContext.SetupGet(value => value.Clients).Returns(hubClients.Object);

        return new PaymentWebhookController(
            context,
            hubContext.Object,
            Mock.Of<DonateWeb.Areas.Widgets.Services.IWidgetService>(),
            Mock.Of<DonateWeb.Security.RateLimiting.IIpRateLimiterService>(),
            NullLogger<PaymentWebhookController>.Instance,
            payOs.Object);
    }

    private static WebhookType CreateWebhook(long orderCode, int amount) => new()
    {
        code = "00",
        success = true,
        data = new WebhookData { orderCode = orderCode, amount = amount }
    };

    private static DefaultHttpContext CreateHttpContext(int userId) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "integration-test"))
    };
}
