using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities;

public class WalletTransactionAuditLog
{
    [Key]
    public int Id { get; set; }

    public int WalletTransactionId { get; set; }

    [ForeignKey(nameof(WalletTransactionId))]
    public WalletTransaction WalletTransaction { get; set; } = null!;

    [Required, MaxLength(50)]
    public string ActionType { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal ExpectedAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ReceivedAmount { get; set; }

    [MaxLength(100)]
    public string? ReferenceCode { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    [MaxLength(100)]
    public string PerformedBy { get; set; } = "PayOS:Webhook";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
