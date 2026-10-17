using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Mã định danh tài khoản 10 số (Streamer bắt đầu bằng 1, Viewer bắt đầu bằng 9; 4 số tiếp là năm đăng ký)
        /// </summary>
        [MaxLength(10)]
        public string? AccountId { get; set; }

        [NotMapped]
        public string? UserCode
        {
            get => AccountId;
            set => AccountId = value;
        }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Mật khẩu đã hash bằng BCrypt / Argon2. Nullable nếu đăng nhập thuần qua OAuth2.
        /// </summary>
        public string? PasswordHash { get; set; }

        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(500)]
        public string AvatarUrl { get; set; } = "/images/default-avatar.png";

        [Column(TypeName = "decimal(18,2)")]
        public decimal WalletBalance { get; set; } = 0;

        /// <summary>
        /// Alias cho WalletBalance để tương thích theo yêu cầu
        /// </summary>
        [NotMapped]
        public decimal Balance
        {
            get => WalletBalance;
            set => WalletBalance = value;
        }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public virtual ICollection<ExternalLogin> ExternalLogins { get; set; } = new List<ExternalLogin>();
        public virtual StreamerProfile? StreamerProfile { get; set; }
        public virtual ICollection<Donation> DonationsSent { get; set; } = new List<Donation>();
        public virtual ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
        public virtual ICollection<WithdrawalRequest> WithdrawalRequests { get; set; } = new List<WithdrawalRequest>();
        public virtual ICollection<StreamerFollow> FollowedStreamers { get; set; } = new List<StreamerFollow>();
    }
}
