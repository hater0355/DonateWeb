using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DonateWeb.ViewModels.Streamer
{
    public class StreamerRegisterViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Họ tên")]
        [MaxLength(100)]
        [Display(Name = "Họ tên")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập ngày sinh đúng theo CCCD")]
        [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Ngày sinh không hợp lệ")]
        [Display(Name = "Ngày sinh")]
        public string? DateOfBirth { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Số Căn cước / CCCD")]
        [RegularExpression(@"^[0-9]{12}$", ErrorMessage = "Số CCCD phải gồm đúng 12 chữ số")]
        [Display(Name = "Số Căn cước/CCCD")]
        public string CitizenId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập giới tính ghi trên CCCD")]
        [MaxLength(20)]
        [Display(Name = "Giới tính")]
        public string Gender { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập quốc tịch")]
        [MaxLength(80)]
        [Display(Name = "Quốc tịch")]
        public string Nationality { get; set; } = "Việt Nam";

        [Required(ErrorMessage = "Vui lòng nhập quê quán theo CCCD")]
        [MaxLength(250)]
        [Display(Name = "Quê quán")]
        public string PlaceOfOrigin { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập nơi sinh theo CCCD")]
        [MaxLength(250)]
        [Display(Name = "Nơi sinh")]
        public string PlaceOfBirth { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ thường trú theo CCCD")]
        [MaxLength(300)]
        [Display(Name = "Nơi thường trú")]
        public string? PermanentAddress { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập ngày cấp CCCD")]
        [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Ngày cấp không hợp lệ")]
        [Display(Name = "Ngày cấp")]
        public string? IdentityIssuedDate { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập nơi cấp CCCD")]
        [MaxLength(200)]
        [Display(Name = "Nơi cấp")]
        public string IdentityIssuedBy { get; set; } = string.Empty;

        [Display(Name = "Địa chỉ hiện tại")]
        [MaxLength(300)]
        public string? CurrentAddress { get; set; }

        [Required(ErrorMessage = "Vui lòng tải ảnh mặt trước CCCD")]
        [Display(Name = "Ảnh mặt trước CCCD")]
        public IFormFile? CitizenIdFrontImage { get; set; }

        [Required(ErrorMessage = "Vui lòng tải ảnh mặt sau CCCD")]
        [Display(Name = "Ảnh mặt sau CCCD")]
        public IFormFile? CitizenIdBackImage { get; set; }

        public bool IdentityDataConsent { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập Số điện thoại")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [RegularExpression(@"^[0-9]{7,15}$", ErrorMessage = "Nhập số điện thoại quốc tế gồm 7–15 chữ số, có mã quốc gia ở đầu")]
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

    }
}
