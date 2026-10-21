using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities;

/// <summary>Lưu hạn mức token AI theo tháng UTC, bền vững qua lần khởi động lại ứng dụng.</summary>
public sealed class SupportAiMonthlyUsage
{
    [Key, MaxLength(7)]
    public string Period { get; set; } = string.Empty; // Định dạng yyyy-MM

    public long TotalTokens { get; set; }
    public long ReservedTokens { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
