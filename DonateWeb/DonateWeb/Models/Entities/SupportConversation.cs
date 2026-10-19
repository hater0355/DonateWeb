using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities;

/// <summary>Cuộc trò chuyện giữa khách truy cập và đội hỗ trợ DonateWeb.</summary>
public sealed class SupportConversation
{
    public int Id { get; set; }

    // Tài khoản đăng nhập được liên kết trực tiếp; khách vãng lai dùng token cookie đã băm.
    public int? UserId { get; set; }

    [MaxLength(64)]
    public string? GuestTokenHash { get; set; }

    [Required, MaxLength(100)]
    public string VisitorName { get; set; } = "Khách";

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Open";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ICollection<SupportChatMessage> Messages { get; set; } = new List<SupportChatMessage>();
}
