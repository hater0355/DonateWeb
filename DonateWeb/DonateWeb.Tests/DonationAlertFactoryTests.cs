using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Models.Entities;
using DonateWeb.Security.ContentModeration;
using Moq;
using Xunit;

namespace DonateWeb.Tests;

public class DonationAlertFactoryTests
{
    [Fact]
    public void DonationBelowConfiguredThresholdDoesNotCreateAlert()
    {
        var moderation = new Mock<IContentModerationService>(MockBehavior.Strict);
        var donation = new Donation { Id = 17, Amount = 9999, DonorName = "Viewer", Status = DonateWeb.Models.Enums.DonationStatus.Success };
        var config = new AlertBoxConfigViewModel { MinAmountToAlert = 10000 };

        var alert = DonationAlertFactory.Create(donation, config, moderation.Object);

        Assert.Null(alert);
        moderation.VerifyNoOtherCalls();
    }

    [Fact]
    public void PendingDonationDoesNotCreateAlert()
    {
        var moderation = new Mock<IContentModerationService>(MockBehavior.Strict);
        var donation = new Donation { Id = 17, Amount = 50000, DonorName = "Viewer" };
        var config = new AlertBoxConfigViewModel { MinAmountToAlert = 10000 };

        Assert.Null(DonationAlertFactory.Create(donation, config, moderation.Object));
        moderation.VerifyNoOtherCalls();
    }

    [Fact]
    public void DonationAtConfiguredThresholdCreatesAlert()
    {
        var moderation = new Mock<IContentModerationService>();
        moderation.Setup(service => service.SanitizeForStream(It.IsAny<string?>()))
            .Returns((string? value) => value ?? string.Empty);
        var donation = new Donation
        {
            Amount = 10000,
            DonorName = "Viewer",
            Status = DonateWeb.Models.Enums.DonationStatus.Success
        };
        var config = new AlertBoxConfigViewModel { MinAmountToAlert = 10000 };

        Assert.NotNull(DonationAlertFactory.Create(donation, config, moderation.Object));
    }

    [Fact]
    public void EligibleDonationUsesSanitizedContentAndCurrentAlertSettings()
    {
        var moderation = new Mock<IContentModerationService>();
        moderation.Setup(service => service.SanitizeForStream("<name>"))
            .Returns("Clean name");
        moderation.Setup(service => service.SanitizeForStream("bad message"))
            .Returns("clean message");
        var donation = new Donation
        {
            Id = 42,
            DonorName = "<name>",
            Amount = 25000,
            Message = "bad message",
            Status = DonateWeb.Models.Enums.DonationStatus.Success
        };
        var config = new AlertBoxConfigViewModel
        {
            MinAmountToAlert = 10000,
            WidgetToken = "widget-token",
            MessageTemplate = "{donor} donated {amount}: {message}",
            IsTtsEnabled = true,
            SoundVolume = 35
        };

        var alert = DonationAlertFactory.Create(donation, config, moderation.Object);

        Assert.NotNull(alert);
        Assert.Equal(42, alert.DonationId);
        Assert.Equal("Clean name", alert.DonorName);
        Assert.Equal("clean message", alert.Message);
        Assert.Equal("Clean name donated 25,000: clean message", alert.DisplayText);
        Assert.Equal("25,000đ", alert.FormattedAmount);
        Assert.True(alert.IsTtsEnabled);
        Assert.Equal("widget-token", alert.TtsToken);
        Assert.Equal(35, alert.SoundVolume);
    }
}
