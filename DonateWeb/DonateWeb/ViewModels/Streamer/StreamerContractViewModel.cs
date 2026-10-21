using DonateWeb.Services;

namespace DonateWeb.ViewModels.Streamer;

public sealed class StreamerContractViewModel
{
    public string Version { get; set; } = StreamerAgreementContentService.CurrentVersion;
    public string Title { get; set; } = string.Empty;
    public IReadOnlyList<StreamerAgreementSection> Sections { get; set; } = [];
    public bool PlatformDetailsComplete { get; set; }
    public string PlatformLegalName { get; set; } = string.Empty;
    public string PlatformCapacity { get; set; } = string.Empty;
    public string PlatformAddress { get; set; } = string.Empty;
    public string PlatformRegistrationNumber { get; set; } = string.Empty;
    public string PlatformEmail { get; set; } = string.Empty;
    public string PlatformWebsite { get; set; } = string.Empty;
    public string StreamerFullName { get; set; } = string.Empty;
    public string StreamerCitizenIdMasked { get; set; } = string.Empty;
    public string StreamerAddress { get; set; } = string.Empty;
    public string StreamerEmail { get; set; } = string.Empty;
    public string StreamerChannelName { get; set; } = string.Empty;
    public string StreamerSlug { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public bool HasAccepted { get; set; }
    public string? AcceptedSnapshot { get; set; }
    public string? AcceptedHash { get; set; }
    public DateTime? SignedAtUtc { get; set; }
}
