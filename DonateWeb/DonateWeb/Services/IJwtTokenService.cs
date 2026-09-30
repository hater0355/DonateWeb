using DonateWeb.Models.Entities;

namespace DonateWeb.Services
{
    public interface IJwtTokenService
    {
        string GenerateToken(User user, IEnumerable<string> roles, string? streamerSlug = null);
    }
}
