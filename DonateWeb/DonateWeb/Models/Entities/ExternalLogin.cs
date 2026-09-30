using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    public class ExternalLogin
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [Required]
        [MaxLength(50)]
        public string Provider { get; set; } = string.Empty; // Google, Discord, Twitch, YouTube

        [Required]
        [MaxLength(200)]
        public string ProviderKey { get; set; } = string.Empty; // Unique ID from OAuth Provider

        [MaxLength(200)]
        public string? ProviderDisplayName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
