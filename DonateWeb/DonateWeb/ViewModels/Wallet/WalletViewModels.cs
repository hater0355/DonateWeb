using System.ComponentModel.DataAnnotations;
using DonateWeb.Models.Entities;

namespace DonateWeb.ViewModels.Wallet
{
    /// <summary>
    /// ViewModel nhận dữ liệu yêu cầu tạo mã nạp tiền từ người dùng
    /// </summary>
    public class DepositRequestViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập số tiền muốn nạp.")]
        [Range(10000, 500000000, ErrorMessage = "Số tiền nạp tối thiểu là 10.000 VNĐ và tối đa là 500.000.000 VNĐ.")]
        public decimal Amount { get; set; } = 50000;
    }

    /// <summary>
    /// ViewModel chứa thông tin chi tiết thanh toán mã VietQR động chuẩn Napas247
    /// </summary>
    public class DepositPaymentViewModel
    {
        public int TransactionId { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Memo { get; set; } = string.Empty;
        public string BankId { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string AccountNo { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string QrUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpireAt { get; set; }
        public int ExpireSeconds { get; set; } = 600; // 10 phút đếm ngược
    }

    /// <summary>
    /// ViewModel cho chức năng Nạp tiền (Deposit) dành cho Role Viewer
    /// </summary>
    public class DepositViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập số tiền cần nạp.")]
        [Range(10000, 500000000, ErrorMessage = "Số tiền nạp phải từ 10.000 VNĐ đến 500.000.000 VNĐ.")]
        public decimal Amount { get; set; } = 50000;

        [Required(ErrorMessage = "Vui lòng chọn phương thức nạp tiền.")]
        public string PaymentMethod { get; set; } = "Chuyển khoản ngân hàng (VietQR)";

        [MaxLength(250, ErrorMessage = "Ghi chú không quá 250 ký tự.")]
        public string? Note { get; set; }

        // Thông tin hiển thị đồng bộ trên View
        public decimal CurrentBalance { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? TransferContent { get; set; }
        public string? GeneratedQrBase64 { get; set; }

        // Thông tin tài khoản thụ hưởng Admin
        public string BankId { get; set; } = "MB";
        public string BankName { get; set; } = "MB Bank (Ngân Hàng Quân Đội)";
        public string AccountNo { get; set; } = "0987654321";
        public string AccountName { get; set; } = "NGUYEN ANH HIEU";

        // Thông tin chi tiết thanh toán khi đã tạo mã QR
        public DepositPaymentViewModel? PaymentInfo { get; set; }

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public List<WalletTransaction> RecentTransactions { get; set; } = new();
    }

    /// <summary>
    /// ViewModel cho chức năng Rút tiền (Withdraw) dành cho Role Streamer
    /// </summary>
    public class WithdrawViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập số tiền muốn rút.")]
        [Range(50000, 500000000, ErrorMessage = "Số tiền rút tối thiểu là 50.000 VNĐ và tối đa 500.000.000 VNĐ.")]
        public decimal Amount { get; set; } = 100000;

        [Required(ErrorMessage = "Vui lòng nhập tên ngân hàng nhận tiền.")]
        [MaxLength(100)]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập số tài khoản ngân hàng.")]
        [MaxLength(50)]
        public string BankAccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập tên chủ tài khoản (viết hoa không dấu).")]
        [MaxLength(100)]
        public string BankAccountName { get; set; } = string.Empty;

        [MaxLength(300, ErrorMessage = "Ghi chú không quá 300 ký tự.")]
        public string? Note { get; set; }

        // Thông tin số dư & thống kê đồng bộ trên View
        public decimal CurrentBalance { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal TotalWithdrawn { get; set; }
        public decimal PendingWithdrawalAmount { get; set; }
        public string StreamerDisplayName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;

        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }

        public List<WithdrawalRequest> WithdrawalHistory { get; set; } = new();
    }
}
