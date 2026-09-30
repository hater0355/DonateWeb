// FILE: ViewModels/UserProfileViewModel.cs
using Microsoft.AspNetCore.Http;

namespace DonateWeb.ViewModels
{
    public class UserProfileViewModel
    {
        public string? AccountId { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public IFormFile? AvatarFile { get; set; }
        public string? CoverUrl { get; set; }
        public string Category { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string PaymentQrUrl { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
    }
}