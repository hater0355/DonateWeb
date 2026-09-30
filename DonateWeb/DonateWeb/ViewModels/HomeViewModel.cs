namespace DonateWeb.ViewModels
{
    public class HomeViewModel
    {
        public string? SearchKeyword { get; set; }
        public List<HomeStreamerCardViewModel> FeaturedStreamers { get; set; } = new();
        public List<HomeStoryViewModel> Stories { get; set; } = new();
        public List<HomeActivityStatViewModel> RecentActivities { get; set; } = new();
        public int TotalDonationCount { get; set; }
        public decimal TotalDonatedAmount { get; set; }
        public int TotalStreamerCount { get; set; }
    }

    public class HomeStreamerCardViewModel
    {
        public int Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int FollowerCount { get; set; }
        public int Rank { get; set; }
        public decimal TotalReceived { get; set; }
        public bool IsVerified { get; set; }
    }

    public class HomeStoryViewModel
    {
        public int StreamerId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int FollowerCount { get; set; }
        public int Rank { get; set; }
    }

    public class HomeActivityStatViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string IconClass { get; set; } = "fa-solid fa-medal";
        public string? HighlightColor { get; set; }
    }
}
