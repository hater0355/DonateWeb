using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DonateWeb.Services
{
    public interface IPayOSService
    {
        Task<CreatePaymentResult> createPaymentLink(PaymentData paymentData);
        WebhookData verifyPaymentWebhookData(WebhookType body);
    }

    /// <summary>
    /// Dữ liệu đầu vào để tạo link thanh toán PayOS
    /// </summary>
    public class PaymentData
    {
        public long orderCode { get; set; }
        public int amount { get; set; }
        public string description { get; set; } = string.Empty;
        public string cancelUrl { get; set; } = string.Empty;
        public string returnUrl { get; set; } = string.Empty;
        public List<ItemData>? items { get; set; }
        public string? buyerName { get; set; }
        public string? buyerEmail { get; set; }
        public string? buyerPhone { get; set; }
        public string? buyerAddress { get; set; }
        public int? expiredAt { get; set; }
    }

    /// <summary>
    /// Thông tin mặt hàng trong đơn thanh toán PayOS
    /// </summary>
    public class ItemData
    {
        public string name { get; set; } = string.Empty;
        public int quantity { get; set; } = 1;
        public int price { get; set; }
    }

    /// <summary>
    /// Kết quả trả về sau khi tạo link thanh toán PayOS
    /// </summary>
    public class CreatePaymentResult
    {
        public string checkoutUrl { get; set; } = string.Empty;
        public string qrCode { get; set; } = string.Empty;
        public long orderCode { get; set; }
        public string paymentLinkId { get; set; } = string.Empty;
        public string status { get; set; } = "PENDING";
        public string bin { get; set; } = string.Empty;
        public string accountNumber { get; set; } = string.Empty;
        public string accountName { get; set; } = string.Empty;
        public int amount { get; set; }
        public string description { get; set; } = string.Empty;
        public string currency { get; set; } = "VND";
    }

    /// <summary>
    /// Model dữ liệu Webhook từ PayOS gửi về website
    /// </summary>
    public class WebhookType
    {
        public string code { get; set; } = string.Empty;
        public string desc { get; set; } = string.Empty;
        public WebhookData? data { get; set; }
        public string signature { get; set; } = string.Empty;
        public bool success { get; set; } = true;
    }

    /// <summary>
    /// Chi tiết dữ liệu giao dịch trong Webhook PayOS
    /// </summary>
    public class WebhookData
    {
        public long orderCode { get; set; }
        public int amount { get; set; }
        public string description { get; set; } = string.Empty;
        public string accountNumber { get; set; } = string.Empty;
        public string reference { get; set; } = string.Empty;
        public string transactionDateTime { get; set; } = string.Empty;
        public string currency { get; set; } = "VND";
        public string paymentLinkId { get; set; } = string.Empty;
        public string code { get; set; } = string.Empty;
        public string desc { get; set; } = string.Empty;
        public string? counterAccountBankId { get; set; }
        public string? counterAccountBankName { get; set; }
        public string? counterAccountName { get; set; }
        public string? counterAccountNumber { get; set; }
        public string? virtualAccountName { get; set; }
        public string? virtualAccountNumber { get; set; }
    }

    /// <summary>
    /// Lớp bọc SDK PayOS, cung cấp các phương thức chuẩn createPaymentLink và verifyPaymentWebhookData
    /// Đảm bảo tính tương thích tuyệt đối cho ứng dụng và DI Singleton
    /// </summary>
    public class PayOS : IPayOSService
    {
        private readonly global::PayOS.PayOSClient _client;
        private readonly string _clientId;
        private readonly string _apiKey;
        private readonly string _checksumKey;

        public PayOS(string clientId, string apiKey, string checksumKey)
        {
            _clientId = clientId ?? string.Empty;
            _apiKey = apiKey ?? string.Empty;
            _checksumKey = checksumKey ?? string.Empty;

            _client = new global::PayOS.PayOSClient(_clientId, _apiKey, _checksumKey);
        }

        /// <summary>
        /// Tạo link thanh toán VietQR / Checkout qua cổng PayOS
        /// </summary>
        public async Task<CreatePaymentResult> createPaymentLink(PaymentData paymentData)
        {
            if (paymentData == null)
                throw new ArgumentNullException(nameof(paymentData));

            var req = new global::PayOS.Models.V2.PaymentRequests.CreatePaymentLinkRequest
            {
                OrderCode = paymentData.orderCode,
                Amount = paymentData.amount,
                Description = paymentData.description,
                CancelUrl = paymentData.cancelUrl,
                ReturnUrl = paymentData.returnUrl,
                BuyerName = paymentData.buyerName,
                BuyerEmail = paymentData.buyerEmail,
                BuyerPhone = paymentData.buyerPhone,
                BuyerAddress = paymentData.buyerAddress,
                ExpiredAt = paymentData.expiredAt
            };

            if (paymentData.items != null && paymentData.items.Count > 0)
            {
                req.Items = paymentData.items.Select(i => new global::PayOS.Models.V2.PaymentRequests.PaymentLinkItem
                {
                    Name = i.name,
                    Price = i.price,
                    Quantity = i.quantity
                }).ToList();
            }

            var response = await _client.PaymentRequests.CreateAsync(req);

            return new CreatePaymentResult
            {
                checkoutUrl = response.CheckoutUrl ?? string.Empty,
                qrCode = response.QrCode ?? string.Empty,
                orderCode = response.OrderCode,
                paymentLinkId = response.PaymentLinkId ?? string.Empty,
                status = response.Status.ToString(),
                bin = response.Bin ?? string.Empty,
                accountNumber = response.AccountNumber ?? string.Empty,
                accountName = response.AccountName ?? string.Empty,
                amount = (int)response.Amount,
                description = response.Description ?? string.Empty,
                currency = response.Currency ?? "VND"
            };
        }

        /// <summary>
        /// Xác thực dữ liệu Webhook từ PayOS bằng ChecksumKey (tránh giả mạo dữ liệu)
        /// </summary>
        public WebhookData verifyPaymentWebhookData(WebhookType body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            if (body.data == null)
                throw new ArgumentException("Dữ liệu webhook trống.", nameof(body));

            var webhook = new global::PayOS.Models.Webhooks.Webhook
            {
                Code = body.code,
                Description = body.desc,
                Success = body.success,
                Signature = body.signature,
                Data = new global::PayOS.Models.Webhooks.WebhookData
                {
                    OrderCode = body.data.orderCode,
                    Amount = body.data.amount,
                    Description = body.data.description,
                    AccountNumber = body.data.accountNumber,
                    Reference = body.data.reference,
                    TransactionDateTime = body.data.transactionDateTime,
                    Currency = body.data.currency,
                    PaymentLinkId = body.data.paymentLinkId,
                    Code = body.data.code,
                    Description2 = body.data.desc,
                    CounterAccountBankId = body.data.counterAccountBankId,
                    CounterAccountBankName = body.data.counterAccountBankName,
                    CounterAccountName = body.data.counterAccountName,
                    CounterAccountNumber = body.data.counterAccountNumber,
                    VirtualAccountName = body.data.virtualAccountName,
                    VirtualAccountNumber = body.data.virtualAccountNumber
                }
            };

            try
            {
                // Thử xác thực qua SDK PayOS chính thức
                var verified = _client.Webhooks.VerifyAsync(webhook).GetAwaiter().GetResult();
                return body.data;
            }
            catch (Exception)
            {
                // Nếu signature khớp với tính toán HMAC SHA-256 nội bộ thì vẫn chấp nhận
                if (VerifyHmacSignature(body.data, body.signature, _checksumKey))
                {
                    return body.data;
                }
                throw;
            }
        }

        /// <summary>
        /// Hỗ trợ kiểm tra chữ ký HMAC SHA-256 theo chuẩn sắp xếp thuộc tính của PayOS
        /// </summary>
        private static bool VerifyHmacSignature(WebhookData data, string signature, string checksumKey)
        {
            if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(checksumKey))
                return false;

            try
            {
                // Sắp xếp các trường dữ liệu theo thứ tự a-z theo chuẩn PayOS
                var sortedDict = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    { "amount", data.amount.ToString() },
                    { "cancel", "false" },
                    { "description", data.description ?? "" },
                    { "orderCode", data.orderCode.ToString() }
                };

                if (!string.IsNullOrEmpty(data.accountNumber))
                    sortedDict["accountNumber"] = data.accountNumber;
                if (!string.IsNullOrEmpty(data.reference))
                    sortedDict["reference"] = data.reference;
                if (!string.IsNullOrEmpty(data.transactionDateTime))
                    sortedDict["transactionDateTime"] = data.transactionDateTime;
                if (!string.IsNullOrEmpty(data.currency))
                    sortedDict["currency"] = data.currency;
                if (!string.IsNullOrEmpty(data.paymentLinkId))
                    sortedDict["paymentLinkId"] = data.paymentLinkId;
                if (!string.IsNullOrEmpty(data.code))
                    sortedDict["code"] = data.code;
                if (!string.IsNullOrEmpty(data.desc))
                    sortedDict["desc"] = data.desc;

                var dataSign = string.Join("&", sortedDict.Select(kv => $"{kv.Key}={kv.Value}"));

                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(checksumKey));
                var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataSign));
                var computedSignature = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                return string.Equals(computedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
