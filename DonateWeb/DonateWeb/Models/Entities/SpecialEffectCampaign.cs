using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities;

/// <summary>Một sự kiện hiệu ứng có khoảng thời gian được Admin quản lý.</summary>
public sealed class SpecialEffectCampaign
{
    public int Id { get; set; }

    /// <summary>Mã preset đã được cho phép, ví dụ vietnam-champions.</summary>
    [MaxLength(50)]
    public string EffectCode { get; set; } = "vietnam-champions";

    [Required, MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<StreamerEffectActivation> Activations { get; set; } = new List<StreamerEffectActivation>();
}
