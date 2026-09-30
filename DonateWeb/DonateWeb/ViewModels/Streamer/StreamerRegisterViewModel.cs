using System.ComponentModel.DataAnnotations;

namespace DonateWeb.ViewModels.Streamer
{
    public class StreamerRegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Họ tên")]
        [Display(Name = "Họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Ngày sinh")]
        public string? DateOfBirth { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Số Căn cước / CCCD")]
        [Display(Name = "Số Căn cước/CCCD")]
        public string CitizenId { get; set; } = string.Empty;

        [Display(Name = "Địa chỉ thường trú")]
        public string? PermanentAddress { get; set; }

        [Display(Name = "Địa chỉ hiện tại")]
        public string? CurrentAddress { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [Display(Name = "Số điện thoại")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Slug kênh streamer của bạn")]
        [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "Slug chỉ gồm chữ thường không dấu, chữ số và gạch ngang (ví dụ: mychannel)")]
        [Display(Name = "Slug cá nhân (domain.com/slug)")]
        public string Slug { get; set; } = string.Empty;

        [Display(Name = "Tên hiển thị kênh")]
        public string? DisplayName { get; set; }

        // Mạng xã hội
        [Display(Name = "Facebook")]
        public string? FacebookUrl { get; set; }

        [Display(Name = "Youtube")]
        public string? YoutubeUrl { get; set; }

        [Display(Name = "Tiktok")]
        public string? TiktokUrl { get; set; }

        // Ngân hàng
        [Required(ErrorMessage = "Vui lòng chọn hoặc nhập Ngân hàng")]
        [Display(Name = "Ngân hàng")]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Số tài khoản")]
        [Display(Name = "Số tài khoản ngân hàng")]
        public string BankAccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Tên chủ tài khoản")]
        [Display(Name = "Tên chủ tài khoản")]
        public string BankAccountName { get; set; } = string.Empty;

        // Nếu chưa đăng nhập, cho phép nhập email & mật khẩu để tạo luôn tài khoản
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Display(Name = "Mật khẩu")]
        public string? Password { get; set; }
    }
}
