using System.ComponentModel.DataAnnotations;
using DonateWeb.Models.Enums;

namespace DonateWeb.ViewModels.Auth
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Tên đăng nhập")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải từ 3 đến 50 ký tự")]
        [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Tên đăng nhập chỉ chứa chữ cái, chữ số, gạch dưới và gạch nối")]
        [Display(Name = "Tên đăng nhập")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Email")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Mật khẩu")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có độ dài từ 6 đến 100 ký tự")]
        [DataType(DataType.Password)]
        [Display(Name = "Mật khẩu")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập lại Mật khẩu")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        [Display(Name = "Xác nhận mật khẩu")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "Họ và tên")]
        public string? FullName { get; set; }

        [Display(Name = "Số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        public string? PhoneNumber { get; set; }

        /// <summary>
        /// Vai trò khi đăng ký: Viewer hoặc Streamer
        /// </summary>
        [Display(Name = "Loại tài khoản")]
        public string RoleType { get; set; } = UserRoles.Viewer;

        /// <summary>
        /// Slug cá nhân nếu đăng ký làm Streamer (ví dụ: domixi, pewpew)
        /// </summary>
        [Display(Name = "Slug cá nhân (dành cho Streamer)")]
        [RegularExpression(@"^[a-zA-Z0-9_-]+$", ErrorMessage = "Slug chỉ chứa chữ cái không dấu, chữ số, gạch dưới và gạch ngang (ví dụ: ten-streamer)")]
        public string? StreamerSlug { get; set; }

        [Display(Name = "Tên hiển thị kênh (dành cho Streamer)")]
        public string? StreamerDisplayName { get; set; }
    }
}
