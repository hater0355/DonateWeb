using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DonateWeb.Models.Entities;
using DonateWeb.Security.Configuration;
using DonateWeb.Security.ContentModeration;
using DonateWeb.Security.Controllers;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Security.Webhook;
using DonateWeb.Services;
using Xunit;

namespace DonateWeb.Tests;

public class SecretConfigurationTests
{
    [Fact]
    public void JwtTokenServiceRejectsMissingOrShortSigningKey()
    {
        var missingKey = new JwtTokenService(new ConfigurationBuilder().Build());
        var shortKey = new JwtTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SecretKey"] = "too-short" })
            .Build());

        Assert.Throws<InvalidOperationException>(() => missingKey.GenerateToken(new User(), Array.Empty<string>()));
        Assert.Throws<InvalidOperationException>(() => shortKey.GenerateToken(new User(), Array.Empty<string>()));
    }

    [Fact]
    public void WebhookHmacFailsClosedWhenSigningKeyIsMissing()
    {
        var service = CreateWebhookSecurityService(string.Empty);

        Assert.False(service.ValidateHmacSignature("payload", "any-signature"));
        Assert.Throws<InvalidOperationException>(() => service.ComputeHmacSha256("payload"));
    }

    [Fact]
    public void WebhookHmacCanBeVerifiedWithConfiguredSecret()
    {
        var service = CreateWebhookSecurityService("test-only-secret-with-adequate-entropy");
        var signature = service.ComputeHmacSha256("payload");

        Assert.True(service.ValidateHmacSignature("payload", signature));
        Assert.False(service.ValidateHmacSignature("changed", signature));
    }

    [Fact]
    public void HmacGenerationEndpointIsUnavailableOutsideDevelopment()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(Environments.Production);
        var controller = new SecurityTestApiController(
            Mock.Of<IContentModerationService>(),
            Mock.Of<IIpRateLimiterService>(),
            Mock.Of<IWebhookSecurityService>(),
            environment.Object);

        Assert.IsType<NotFoundResult>(controller.GenerateHmac(new TestHmacRequest { Payload = "forged payment" }));
    }

    private static WebhookSecurityService CreateWebhookSecurityService(string secret) => new(
        Options.Create(new SecuritySettings
        {
            Webhook = new WebhookSecuritySettings { SecretKey = secret }
        }),
        Mock.Of<IIpRateLimiterService>(),
        NullLogger<WebhookSecurityService>.Instance);
}
