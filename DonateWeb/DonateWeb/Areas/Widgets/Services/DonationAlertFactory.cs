using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;

namespace DonateWeb.Areas.Widgets.Services;

public static class DonationAlertFactory
{
    public static AlertPollResultDto? Create(
        Donation donation,
        AlertBoxConfigViewModel config,
        IContentModerationService moderationService)
    {
        if (donation.Status != DonationStatus.Success || donation.Amount < config.MinAmountToAlert)
        {
            return null;
        }

        var donorName = moderationService.SanitizeForStream(donation.DonorName);
        var message = moderationService.SanitizeForStream(donation.Message);
        var template = string.IsNullOrWhiteSpace(config.MessageTemplate)
            ? "{donor} vừa ủng hộ {amount} VNĐ!"
            : config.MessageTemplate;

        return new AlertPollResultDto
        {
            DonationId = donation.Id,
            TtsToken = config.WidgetToken,
            DonorName = donorName,
            Amount = donation.Amount,
            FormattedAmount = $"{donation.Amount:N0}đ",
            Message = message,
            DisplayText = template
                .Replace("{donor}", donorName)
                .Replace("{amount}", donation.Amount.ToString("N0"))
                .Replace("{message}", message),
            MediaType = config.MediaType ?? "image",
            ImageUrl = config.ImageUrl,
            SoundUrl = config.SoundUrl,
            SoundVolume = config.SoundVolume,
            DurationSeconds = config.DurationSeconds,
            TextColor = config.TextColor,
            FontFamily = config.FontFamily,
            FontSize = config.FontSize,
            AnimationIn = config.AnimationIn,
            AnimationOut = config.AnimationOut,
            IsTtsEnabled = config.IsTtsEnabled
        };
    }
}
