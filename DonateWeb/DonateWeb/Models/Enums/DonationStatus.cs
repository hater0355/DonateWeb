namespace DonateWeb.Models.Enums
{
    /// <summary>
    /// Trạng thái của giao dịch quyên góp (Donation)
    /// </summary>
    public enum DonationStatus
    {
        /// <summary>Đang chờ xử lý thanh toán</summary>
        Pending = 0,

        /// <summary>Thanh toán thành công, tiền đã chuyển vào tài khoản streamer</summary>
        Success = 1,

        /// <summary>Thanh toán thất bại (lỗi cổng thanh toán hoặc không đủ số dư ví)</summary>
        Failed = 2,

        /// <summary>Giao dịch bị hủy bởi người dùng hoặc hệ thống</summary>
        Cancelled = 3,

        /// <summary>Giao dịch đang có khiếu nại / tranh chấp (Dispute) chờ Quản trị viên xử lý</summary>
        Disputed = 4,

        /// <summary>Giao dịch đã được Quản trị viên hoàn tiền cho người gửi (Refunded)</summary>
        Refunded = 5
    }
}
