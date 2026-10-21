using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities;

/// <summary>Tin nhắn dạng văn bản thuần trong một cuộc trò chuyện hỗ trợ.</summary>
public sealed class SupportChatMessage
{
    public long Id { get; set; }
    public int ConversationId { get; set; }
    public int? SenderUserId { get; set; }

    [Required, MaxLength(100)]
    public string SenderName { get; set; } = string.Empty;

    public bool IsFromSupport { get; set; }

    // Phân biệt câu trả lời tự động của AI với tin nhắn do Hỗ trợ viên gửi.
    public bool IsAi { get; set; }

    [Required, MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public bool IsReadBySupport { get; set; }
    public bool IsReadByVisitor { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SupportConversation Conversation { get; set; } = null!;
}
