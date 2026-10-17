namespace DonateWeb.Models.Entities;

/// <summary>Trạng thái bật/tắt hiệu ứng mà một streamer chọn cho kênh của mình.</summary>
public sealed class StreamerEffectActivation
{
    public int Id { get; set; }
    public int StreamerProfileId { get; set; }
    public int SpecialEffectCampaignId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime ActivatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public StreamerProfile StreamerProfile { get; set; } = null!;
    public SpecialEffectCampaign Campaign { get; set; } = null!;
}
