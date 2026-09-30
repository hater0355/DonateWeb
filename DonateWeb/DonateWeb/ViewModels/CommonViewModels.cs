// FILE: ViewModels/CommonViewModels.cs
namespace DonateWeb.ViewModels
{
    public class StreamerViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public int Followers { get; set; }
        public int Rank { get; set; }
    }

    public class StoryViewModel
    {
        public string Id { get; set; }
        public string StreamerName { get; set; }
        public string AvatarUrl { get; set; }
        public int Viewers { get; set; }
    }

    public class TransactionViewModel
    {
        public string TransactionId { get; set; }
        public string Time { get; set; }
        public string Type { get; set; }
        public string TotalAmount { get; set; }
        public string Status { get; set; }
    }

    public class OrderViewModel
    {
        public string OrderId { get; set; }
        public string Date { get; set; }
        public string Type { get; set; }
        public string Total { get; set; }
        public string Status { get; set; }
        public string IdolName { get; set; }
    }

    public class EffectViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconClass { get; set; }
        public string Status { get; set; } // Unactivated, Active, Expired
    }
}