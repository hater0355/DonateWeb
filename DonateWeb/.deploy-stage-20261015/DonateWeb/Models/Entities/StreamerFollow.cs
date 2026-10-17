using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Thực thể lưu trữ mối quan hệ theo dõi giữa Người dùng (User / Viewer) và Kênh Streamer (StreamerProfile).
    /// Phục vụ cho tính năng "Streamer Yêu Thích" (Icon trái tim).
    /// </summary>
    [Table("StreamerFollows")]
    public class StreamerFollow
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int StreamerProfileId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey(nameof(UserId))]
        public virtual User User { get; set; } = null!;

        [ForeignKey(nameof(StreamerProfileId))]
        public virtual StreamerProfile StreamerProfile { get; set; } = null!;
    }
}
