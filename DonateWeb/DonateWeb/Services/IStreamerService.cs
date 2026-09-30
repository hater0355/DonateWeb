using DonateWeb.Models.Entities;
using DonateWeb.ViewModels.Streamer;

namespace DonateWeb.Services
{
    public interface IStreamerService
    {
        Task<StreamerProfile?> GetBySlugAsync(string slug);
        Task<StreamerProfile?> GetByUserIdAsync(int userId);
        Task<List<StreamerProfile>> GetFeaturedStreamersAsync(int count = 8);
        Task<(bool Success, string Error)> UpdateProfileConfigAsync(int userId, StreamerProfileConfigViewModel model);
        Task<(bool Success, string Error, Donation? Donation)> ProcessDonationAsync(int? donorUserId, StreamerDonateViewModel model);
        Task<(bool Success, string Error, StreamerProfile? Profile)> RegisterStreamerAsync(int? currentUserId, StreamerRegisterViewModel model);
        Task<(bool Success, string Message, User? User)> CancelStreamerAsync(int userId);
    }
}
