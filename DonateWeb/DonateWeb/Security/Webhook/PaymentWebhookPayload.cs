using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// DỮ LIỆU GIAO DỊCH CHI TIẾT TRONG PAYLOAD WEBHOOK CỦA PAYOS
    /// </summary>
    public class PayOSWebhookData
    {
        [JsonPropertyName("orderCode")]
        public long OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("transactionDateTime")]
        public string? TransactionDateTime { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paymentLinkId")]
        public string? PaymentLinkId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("counterAccountBankId")]
        public string? CounterAccountBankId { get; set; }

        [JsonPropertyName("counterAccountBankName")]
        public string? CounterAccountBankName { get; set; }

        [JsonPropertyName("counterAccountName")]
        public string? CounterAccountName { get; set; }

        [JsonPropertyName("counterAccountNumber")]
        public string? CounterAccountNumber { get; set; }

        [JsonPropertyName("virtualAccountName")]
        public string? VirtualAccountName { get; set; }

        [JsonPropertyName("virtualAccountNumber")]
        public string? VirtualAccountNumber { get; set; }
    }

    /// <summary>
    /// DTO TIẾP NHẬN DỮ LIỆU TỪ WEBHOOK CỦA ĐỐI TÁC THANH TOÁN (PAYOS, VIETQR, SEPAY, MOMO, VNPAY, MBBANK)
    /// Hỗ trợ linh hoạt cả 2 định dạng:
    /// 1. Chuẩn Webhook PayOS lồng nhau (code, desc, data: { orderCode, amount, description, reference, ... })
    /// 2. Định dạng phẳng (transactionCode, amount, status, note, gatewayTransactionId, ...)
    /// </summary>
    public class PaymentWebhookPayload
    {
        // ==========================================
        // CÁC TRƯỜNG DÀNH CHO CẤU TRÚC PHẲNG
        // ==========================================
        [JsonPropertyName("transactionCode")]
        public string? TransactionCode { get; set; }

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "SUCCESS";

        [JsonPropertyName("gateway")]
        public string? Gateway { get; set; } = "PayOS";

        [JsonPropertyName("gatewayTransactionId")]
        public string? GatewayTransactionId { get; set; }

        [JsonPropertyName("paymentTime")]
        public string? PaymentTime { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        // ==========================================
        // CÁC TRƯỜNG DÀNH CHO WEBHOOK PAYOS CHUẨN
        // ==========================================
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("data")]
        public PayOSWebhookData? Data { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }

        // ==========================================
        // HÀM TIỆN ÍCH TRÍCH XUẤT THÔNG TIN DÙ GỬI THEO ĐỊNH DẠNG NÀO
        // ==========================================

        /// <summary>
        /// Lấy số tiền thực nhận được từ Webhook
        /// </summary>
        public decimal GetEffectiveAmount()
        {
            if (Data != null && Data.Amount > 0)
                return Data.Amount;
            return Amount;
        }

        /// <summary>
        /// Lấy nội dung chuyển khoản (chứa cú pháp như NAP12345 hoặc DN12345)
        /// </summary>
        public string GetTransferContent()
        {
            if (Data != null && !string.IsNullOrWhiteSpace(Data.Description))
                return Data.Description.Trim();
            if (!string.IsNullOrWhiteSpace(Note))
                return Note.Trim();
            if (!string.IsNullOrWhiteSpace(TransactionCode))
                return TransactionCode.Trim();
            return string.Empty;
        }

        /// <summary>
        /// Lấy mã tham chiếu từ ngân hàng hoặc OrderCode của PayOS
        /// </summary>
        public string GetReferenceCode()
        {
            if (Data != null && !string.IsNullOrWhiteSpace(Data.Reference))
                return Data.Reference.Trim();
            if (Data != null && Data.OrderCode > 0)
                return Data.OrderCode.ToString();
            if (!string.IsNullOrWhiteSpace(GatewayTransactionId))
                return GatewayTransactionId.Trim();
            return "N/A";
        }

        /// <summary>
        /// Kiểm tra xem cổng thanh toán có xác nhận thành công hay không
        /// PayOS trả về code = "00" hoặc desc = "success"
        /// </summary>
        public bool IsPaymentSuccess()
        {
            if (Code == "00" || string.Equals(Desc, "success", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Data != null && (Data.Code == "00" || string.Equals(Data.Desc, "success", StringComparison.OrdinalIgnoreCase)))
                return true;

            return Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                || Status.Equals("PAID", StringComparison.OrdinalIgnoreCase)
                || Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);
        }
    }
}
