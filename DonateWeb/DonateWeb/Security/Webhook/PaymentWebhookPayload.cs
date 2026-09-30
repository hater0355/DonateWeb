using System.ComponentModel.DataAnnotations;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// DTO TIẾP NHẬN DỮ LIỆU TỪ WEBHOOK CỦA ĐỐI TÁC THANH TOÁN (MOMO, VNPAY, SEPAY, VIETQR, NGÂN HÀNG)
    /// Thư mục riêng: Security/Webhook/
    /// </summary>
    public class PaymentWebhookPayload
    {
        /// <summary>
        /// Mã giao dịch nội bộ của hệ thống DonateWeb (ví dụ: DON_20260918120000_AB12CD hoặc DON_SAMPLE_001)
        /// </summary>
        [Required(ErrorMessage = "Mã giao dịch (TransactionCode) là bắt buộc.")]
        public string TransactionCode { get; set; } = string.Empty;

        /// <summary>
        /// Số tiền thanh toán thực tế nhận được (VNĐ)
        /// </summary>
        [Range(1000, 500000000, ErrorMessage = "Số tiền thanh toán không hợp lệ.")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Trạng thái thanh toán từ đối tác: 'SUCCESS', 'PAID', 'COMPLETED', 'FAILED', 'CANCELLED'
        /// </summary>
        [Required]
        public string Status { get; set; } = "SUCCESS";

        /// <summary>
        /// Tên cổng thanh toán gửi webhook (ví dụ: MoMo, VNPay, SePay, MBBank, VietQR)
        /// </summary>
        public string? Gateway { get; set; } = "PaymentGateway";

        /// <summary>
        /// Mã giao dịch phía đối tác thanh toán trả về để đối soát
        /// </summary>
        public string? GatewayTransactionId { get; set; }

        /// <summary>
        /// Chữ ký số HMAC-SHA256 (nếu đối tác truyền trong body thay vì header)
        /// </summary>
        public string? Signature { get; set; }

        /// <summary>
        /// Thời điểm thanh toán tại cổng (Unix Timestamp hoặc chuỗi ISO 8601)
        /// </summary>
        public string? PaymentTime { get; set; }

        /// <summary>
        /// Dữ liệu bổ sung / Ghi chú chuyển khoản
        /// </summary>
        public string? Note { get; set; }
    }
}
