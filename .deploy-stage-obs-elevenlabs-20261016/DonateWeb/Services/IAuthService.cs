using DonateWeb.Models.Entities;
using DonateWeb.ViewModels.Auth;

namespace DonateWeb.Services
{
    public interface IAuthService
    {
        Task<(bool Success, string Error, User? User, List<string> Roles)> LoginAsync(string usernameOrEmail, string password);
        Task<(bool Success, string Error, User? User)> RegisterAsync(RegisterViewModel model);
        Task<(bool Success, string Error, User? User, List<string> Roles)> ProcessExternalLoginAsync(string provider, string providerKey, string? email, string? displayName, string? avatarUrl = null);
        Task<User?> GetUserByIdAsync(int userId);
        Task<List<string>> GetUserRolesAsync(int userId);
    }
}
