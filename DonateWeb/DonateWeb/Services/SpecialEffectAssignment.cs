using DonateWeb.Data;
using DonateWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Services;

/// <summary>Gắn preset sự kiện đang bật vào donation tại đúng thời điểm thanh toán thành công.</summary>
public static class SpecialEffectAssignment
{
    public static async Task ApplyToDonationAsync(
        AppDbContext context,
        Donation donation,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var campaign = await context.StreamerEffectActivations
            .AsNoTracking()
            .Where(activation => activation.StreamerProfileId == donation.StreamerProfileId && activation.IsEnabled)
            .Where(activation => activation.Campaign.IsEnabled &&
                activation.Campaign.StartsAtUtc <= now && activation.Campaign.EndsAtUtc >= now)
            .Select(activation => new { activation.Campaign.EffectCode, activation.Campaign.Title })
            .FirstOrDefaultAsync(cancellationToken);
        donation.SpecialEffectCode = campaign?.EffectCode;
        donation.SpecialEffectTitle = campaign?.Title;
    }
}
