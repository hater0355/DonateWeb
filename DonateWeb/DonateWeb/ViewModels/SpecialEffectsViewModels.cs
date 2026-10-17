namespace DonateWeb.ViewModels;

public sealed class SpecialEffectsViewModel
{
    public bool CanManage { get; set; }
    public string? StreamerName { get; set; }
    public int? ActiveCampaignId { get; set; }
    public List<SpecialEffectCampaignCardViewModel> Campaigns { get; set; } = new();
}

public sealed class SpecialEffectCampaignCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAtLocal { get; set; }
    public DateTime EndsAtLocal { get; set; }
    public bool IsAvailable { get; set; }
    public bool CanActivate { get; set; }
    public bool IsStreamerEnabled { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class AdminSpecialEffectsViewModel
{
    public string Title { get; set; } = "Việt Nam vô địch";
    public string? Description { get; set; } = "Cờ Việt Nam phất lên cùng pháo hoa mỗi khi có donate.";
    public string StartsAtLocal { get; set; } = string.Empty;
    public string EndsAtLocal { get; set; } = string.Empty;
    public List<AdminSpecialEffectCampaignRowViewModel> Campaigns { get; set; } = new();
}

public sealed class UpdateSpecialEffectCampaignViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string StartsAtLocal { get; set; } = string.Empty;
    public string EndsAtLocal { get; set; } = string.Empty;
}

public sealed class AdminSpecialEffectCampaignRowViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime StartsAtLocal { get; set; }
    public DateTime EndsAtLocal { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsAvailableNow { get; set; }
    public int EnabledStreamerCount { get; set; }
}
