using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DonateWeb.Models.Enums;

namespace DonateWeb.Models.Entities
{
    public class StreamerProfile
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        /// <summary>
        /// Slug định danh cá nhân duy nhất cho streamer (ví dụ: domain.com/tenstreamer)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Lời chào / thông điệp hiển thị trên trang nhận donate
        /// </summary>
        [MaxLength(500)]
        public string GreetingMessage { get; set; } = "Chào mừng các bạn đến với kênh donate chính thức của mình! Cảm ơn sự ủng hộ của các bạn ❤️";

        /// <summary>
        /// Giới thiệu bản thân (Bio / About Me) - hỗ trợ HTML từ rich text editor.
        /// Hiển thị trên trang cá nhân công khai (Public Profile) của Streamer.
        /// </summary>
        public string? Bio { get; set; }

        [MaxLength(500)]
        public string AvatarUrl { get; set; } = "/images/default-avatar.png";

        [MaxLength(500)]
        public string BannerUrl { get; set; } = "/images/default-banner.jpg";

        /// <summary>
        /// Mức donate tối thiểu (VNĐ)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MinDonateAmount { get; set; } = 10000;

        // Thông tin thanh toán & nhận tiền
        [MaxLength(100)]
        public string? BankName { get; set; }

        [MaxLength(50)]
        public string? BankAccountNumber { get; set; }

        [MaxLength(100)]
        public string? BankAccountName { get; set; }

        [MaxLength(500)]
        public string? PaymentQrUrl { get; set; }

        // Mạng xã hội liên kết
        [MaxLength(255)]
        public string? YoutubeUrl { get; set; }

        [MaxLength(255)]
        public string? TwitchUrl { get; set; }

        [MaxLength(255)]
        public string? DiscordUrl { get; set; }

        [MaxLength(255)]
        public string? FacebookUrl { get; set; }

        [MaxLength(255)]
        public string? TiktokUrl { get; set; }

        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// Trạng thái kênh streamer: true = Đang hoạt động bình thường, false = Bị khóa do vi phạm chính sách
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Lý do khóa kênh khi streamer vi phạm tiêu chuẩn cộng đồng (ví dụ: nội dung độc hại, gian lận donate)
        /// </summary>
        [MaxLength(500)]
        public string? LockReason { get; set; }

        /// <summary>
        /// Thời điểm kênh bị quản trị viên khóa
        /// </summary>
        public DateTime? LockedAt { get; set; }

        public int FollowerCount { get; set; } = 0;

        public int Rank { get; set; } = 1;

        /// <summary>
        /// Trạng thái phê duyệt hồ sơ streamer: Pending (0: Chờ duyệt), Approved (1: Đã duyệt), Rejected (2: Bị từ chối)
        /// </summary>
        public StreamerApprovalStatus ApprovalStatus { get; set; } = StreamerApprovalStatus.Approved;

        /// <summary>
        /// Lý do từ chối phê duyệt hồ sơ (nếu có)
        /// </summary>
        [MaxLength(500)]
        public string? RejectionReason { get; set; }

        /// <summary>
        /// Thời điểm hồ sơ được Quản trị viên duyệt
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalReceived { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual ICollection<Donation> DonationsReceived { get; set; } = new List<Donation>();
    }
}
