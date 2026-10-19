namespace DonateWeb.Areas.Admin.ViewModels;

public sealed class SupportAgentManagementViewModel
{
    public string Search { get; set; } = string.Empty;
    public IReadOnlyList<SupportAgentUserViewModel> Agents { get; set; } = Array.Empty<SupportAgentUserViewModel>();
    public IReadOnlyList<SupportAgentUserViewModel> Candidates { get; set; } = Array.Empty<SupportAgentUserViewModel>();
    public int MaximumAgents { get; set; } = 2;
}

public sealed class SupportAgentUserViewModel
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
