using System.ComponentModel.DataAnnotations;
using DonateWeb.Models.Enums;

namespace DonateWeb.ViewModels.Streamer
{
    public class StreamerDonateViewModel
    {
        // Thông tin streamer
        public int StreamerProfileId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = "/images/default-avatar.png";
        public string BannerUrl { get; set; } = "/images/default-banner.jpg";
        public string GreetingMessage { get; set; } = string.Empty;
        public decimal MinDonateAmount { get; set; } = 10000;
        public int FollowerCount { get; set; }
        public int Rank { get; set; }
        public bool IsVerified { get; set; }
        public bool IsActive { get; set; } = true;
        public string? LockReason { get; set; }

        /// <summary>
        /// Nội dung giới thiệu bản thân (Bio / About Me) của Streamer, dạng HTML.
        /// Hiển thị trên trang donate công khai để Viewer xem.
        /// </summary>
        public string? Bio { get; set; }

        // Thông tin ngân hàng / thanh toán của Streamer
        public string? BankName { get; set; }
        public string? BankAccountNumber { get; set; }
        public string? BankAccountName { get; set; }
        public string? PaymentQrUrl { get; set; }

        // Mạng xã hội
        public string? YoutubeUrl { get; set; }
        public string? TwitchUrl { get; set; }
        public string? DiscordUrl { get; set; }
        public string? FacebookUrl { get; set; }
        public string? TiktokUrl { get; set; }

        // Form donate input
        [Required(ErrorMessage = "Vui lòng nhập tên người ủng hộ")]
        [MaxLength(50, ErrorMessage = "Tên không được quá 50 ký tự")]
        [Display(Name = "Tên của bạn")]
        public string DonorName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số tiền donate")]
        [Range(1000, 100000000, ErrorMessage = "Số tiền donate không hợp lệ")]
        [Display(Name = "Số tiền donate (VNĐ)")]
        public decimal Amount { get; set; } = 20000;

        [MaxLength(500, ErrorMessage = "Lời nhắn tối đa 500 ký tự")]
        [Display(Name = "Lời nhắn gửi đến streamer")]
        public string? Message { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Wallet;

        // =========================================================================
        // [MỚI THÊM] Các thuộc tính phục vụ phương thức thanh toán & sinh mã QR động
        // =========================================================================

        /// <summary>
        /// Số dư ví hiện tại của Viewer đang đăng nhập (hiển thị trên phương thức thanh toán từ số dư)
        /// </summary>
        public decimal CurrentWalletBalance { get; set; } = 0;

        /// <summary>
        /// Nội dung chuyển khoản ngân hàng tự động sinh theo cú pháp (chứa Slug + Tên người gửi)
        /// </summary>
        public string TransferContent { get; set; } = string.Empty;

        /// <summary>
        /// Chuỗi ảnh mã QR (Data URI Base64 PNG) được sinh từ thư viện QRCoder chứa Số tiền & Nội dung chuyển khoản
        /// </summary>
        public string? GeneratedQrBase64 { get; set; }

        // Trạng thái / Thông báo kết quả gửi donate
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
