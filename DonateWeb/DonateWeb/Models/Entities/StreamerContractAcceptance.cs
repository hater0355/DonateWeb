using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities;

/// <summary>
/// Bằng chứng chấp nhận một phiên bản hợp đồng streamer trên tài khoản đã đăng nhập.
/// Nội dung snapshot được mã hóa; SHA-256 được tính trên nội dung rõ để kiểm tra toàn vẹn.
/// </summary>
public sealed class StreamerContractAcceptance
{
    public int Id { get; set; }
    public int StreamerProfileId { get; set; }
    public int UserId { get; set; }

    [Required, MaxLength(40)]
    public string ContractVersion { get; set; } = string.Empty;

    [Required]
    public string ContractSnapshotProtected { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string ContractSnapshotSha256 { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SignerFullName { get; set; } = string.Empty;

    public DateTime SignedAtUtc { get; set; }

    [MaxLength(45)]
    public string? ClientIpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }
}
