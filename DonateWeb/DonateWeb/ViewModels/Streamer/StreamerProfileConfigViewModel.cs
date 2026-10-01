using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DonateWeb.ViewModels.Streamer
{
    public class StreamerProfileConfigViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Tên hiển thị")]
        [StringLength(100, ErrorMessage = "Tên hiển thị tối đa 100 ký tự")]
        [Display(Name = "Tên hiển thị Streamer")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Slug cá nhân")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Slug cá nhân phải từ 3 đến 50 ký tự")]
        [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "Slug chỉ chứa chữ thường không dấu, chữ số và gạch ngang (ví dụ: ten-streamer)")]
        [Display(Name = "Slug cá nhân (URL: domain.com/slug)")]
        public string Slug { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập lời chào người xem")]
        [StringLength(500, ErrorMessage = "Lời chào tối đa 500 ký tự")]
        [Display(Name = "Lời chào trang donate")]
        public string GreetingMessage { get; set; } = string.Empty;

        /// <summary>
        /// Giới thiệu bản thân (Bio / About Me) - nội dung HTML từ rich text editor.
        /// Không giới hạn ký tự, được lưu dạng HTML và hiển thị trên Public Profile.
        /// </summary>
        [Display(Name = "Giới thiệu bản thân (Bio)")]
        public string? Bio { get; set; }

        [Display(Name = "Ảnh đại diện (Avatar)")]
        public string? AvatarUrl { get; set; } = "/images/default-avatar.png";

        [Display(Name = "Tải ảnh đại diện từ máy tính")]
        public IFormFile? AvatarFile { get; set; }

        [Display(Name = "URL Ảnh bìa (Banner / Cover)")]
        public string? BannerUrl { get; set; } = "/images/default-banner.jpg";

        [Required(ErrorMessage = "Vui lòng nhập mức donate tối thiểu")]
        [Range(1000, 100000000, ErrorMessage = "Mức donate tối thiểu từ 1,000 VNĐ đến 100,000,000 VNĐ")]
        [Display(Name = "Mức donate tối thiểu (VNĐ)")]
        public decimal MinDonateAmount { get; set; } = 10000;

        // Thông tin thanh toán & nhận tiền
        [Display(Name = "Tên Ngân hàng")]
        public string? BankName { get; set; }

        [Display(Name = "Số tài khoản")]
        public string? BankAccountNumber { get; set; }

        [Display(Name = "Tên chủ tài khoản")]
        public string? BankAccountName { get; set; }

        [Display(Name = "Mã QR nhận donate")]
        public string? PaymentQrUrl { get; set; }

        // Mạng xã hội
        [Display(Name = "Kênh Youtube")]
        public string? YoutubeUrl { get; set; }

        [Display(Name = "Kênh Twitch")]
        public string? TwitchUrl { get; set; }

        [Display(Name = "Kênh Discord")]
        public string? DiscordUrl { get; set; }

        [Display(Name = "Trang Facebook")]
        public string? FacebookUrl { get; set; }

        [Display(Name = "Kênh Tiktok")]
        public string? TiktokUrl { get; set; }

        // =========================================================================
        // Thuộc tính phục vụ hiển thị & tải Mã QR trang cá nhân / donate của Streamer
        // =========================================================================

        /// <summary>
        /// Đường dẫn trang donate của streamer (ví dụ: https://localhost:7111/tenstreamer hoặc https://localhost:7111/123456)
        /// </summary>
        [Display(Name = "Đường dẫn trang Donate")]
        public string? DonatePageUrl { get; set; }

        /// <summary>
        /// Chuỗi Data URI Base64 PNG của mã QR trỏ đến trang Donate (để nhúng trực tiếp vào thẻ <img> và tải về)
        /// </summary>
        public string? QrCodeImageBase64 { get; set; }
    }
}
