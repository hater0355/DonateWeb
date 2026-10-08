using System;
using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Models.Entities
{
    /// <summary>
    /// Bản tin khẩn cấp / Tin nóng chạy ngang đầu trang (Breaking News)
    /// do Quản trị viên (Admin) đăng và quản lý
    /// </summary>
    public class BreakingNews
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Nội dung tin tức hiển thị chạy ngang
        /// </summary>
        [Required(ErrorMessage = "Vui lòng nhập nội dung Breaking News")]
        [MaxLength(1000)]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Đường dẫn liên kết khi người dùng nhấn vào tin (tùy chọn)
        /// </summary>
        [MaxLength(500)]
        public string? LinkUrl { get; set; }

        /// <summary>
        /// Trạng thái kích hoạt (true: đang phát trên thanh chạy chữ, false: tạm ẩn)
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Tài khoản Quản trị viên đã đăng
        /// </summary>
        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
