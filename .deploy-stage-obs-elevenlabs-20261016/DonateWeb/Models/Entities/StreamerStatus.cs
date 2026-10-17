using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Thực thể lưu bài viết / status của Streamer (gồm caption, ảnh đính kèm, lượt thả tim).
    /// </summary>
    [Table("StreamerStatuses")]
    public class StreamerStatus
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StreamerProfileId { get; set; }

        [Required]
        [MaxLength(4000)]
        public string Content { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        public int LikeCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey(nameof(StreamerProfileId))]
        public virtual StreamerProfile StreamerProfile { get; set; } = null!;
    }
}
