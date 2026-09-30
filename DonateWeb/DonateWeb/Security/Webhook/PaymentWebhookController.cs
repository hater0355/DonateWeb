using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// CONTROLLER TIẾP NHẬN VÀ XỬ LÝ WEBHOOK TỪ CÁC ĐỐI TÁC THANH TOÁN (MOMO, VNPAY, SEPAY, MBBANK, VIETQR)
    /// Thư mục riêng: Security/Webhook/
    /// Điểm nổi bật:
    /// 1. Khóa chặt bằng [ValidateWebhookSecurity]: Kiểm tra cả Whitelist IP và Secret Key/HMAC trước khi vào controller.
    /// 2. Chống lặp giao dịch (Idempotency): Tránh việc đối tác retry webhook dẫn đến cộng tiền 2 lần cho streamer.
    /// 3. Đối soát số tiền chính xác giữa đơn hệ thống và số tiền thực nhận từ cổng.
    /// 4. Ghi nhận toàn bộ vết kiểm toán (Audit Trail) vào bảng TransactionAuditLogs để đối soát khi có khiếu nại.
    /// </summary>
    [ApiController]
    [Route("api/webhook/payment")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IIpRateLimiterService _ipResolver;
        private readonly ILogger<PaymentWebhookController> _logger;

        public PaymentWebhookController(
            AppDbContext context,
            IIpRateLimiterService ipResolver,
            ILogger<PaymentWebhookController> logger)
        {
            _context = context;
            _ipResolver = ipResolver;
            _logger = logger;
        }

        /// <summary>
        /// Endpoint kiểm tra tình trạng hoạt động của hệ thống Webhook (Health Check)
        /// GET /api/webhook/payment/health
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            var clientIp = _ipResolver.ResolveClientIp(HttpContext);
            return Ok(new
            {
                status = "OK",
                message = "Hệ thống Webhook bảo mật của DonateWeb đang hoạt động bình thường.",
                clientIp,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Endpoint tiếp nhận thông báo thanh toán (Webhook callback)
        /// Hỗ trợ cả POST /api/webhook/payment và POST /api/webhook/payment/{gateway}
        /// Được bảo vệ chặt chẽ bằng thuộc tính [ValidateWebhookSecurity].
        /// </summary>
        [HttpPost]
        [HttpPost("{gateway}")]
        [ValidateWebhookSecurity]
        public async Task<IActionResult> ProcessPaymentWebhook([FromRoute] string? gateway, [FromBody] PaymentWebhookPayload payload)
        {
            var clientIp = _ipResolver.ResolveClientIp(HttpContext);
            var effectiveGateway = !string.IsNullOrWhiteSpace(gateway) ? gateway : (payload.Gateway ?? "PaymentGateway");

            _logger.LogInformation(
                "[WEBHOOK XÁC THỰC] Bắt đầu xử lý đơn {TransactionCode} từ cổng {Gateway} (IP: {ClientIp})",
                payload.TransactionCode, effectiveGateway, clientIp);

            // BƯỚC 1: KIỂM TRA DỮ LIỆU ĐẦU VÀO
            if (!ModelState.IsValid)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "INVALID_PAYLOAD",
                    message = "Dữ liệu Webhook gửi lên không hợp lệ.",
                    details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            // BƯỚC 2: TÌM KIẾM ĐƠN QUYÊN GÓP (DONATION) THEO TRANSACTION CODE
            var donation = await _context.Donations
                .Include(d => d.StreamerProfile)
                .FirstOrDefaultAsync(d => d.TransactionCode == payload.TransactionCode);

            if (donation == null)
            {
                _logger.LogWarning(
                    "[WEBHOOK CẢNH BÁO] Không tìm thấy đơn donate với mã {TransactionCode}",
                    payload.TransactionCode);

                return NotFound(new
                {
                    success = false,
                    error = "TRANSACTION_NOT_FOUND",
                    message = $"Không tìm thấy đơn Donate với mã giao dịch '{payload.TransactionCode}' trong hệ thống."
                });
            }

            // BƯỚC 3: CƠ CHẾ CHỐNG TRÙNG LẶP GIAO DỊCH (IDEMPOTENCY)
            // Nếu đơn này đã được xác nhận thành công trước đó (ví dụ do cổng retry webhook), trả về 200 OK ngay mà không cộng thêm tiền
            if (donation.Status == DonationStatus.Success)
            {
                _logger.LogInformation(
                    "[WEBHOOK IDEMPOTENT] Giao dịch {TransactionCode} đã thành công từ trước. Trả về 200 OK để xác nhận.",
                    donation.TransactionCode);

                return Ok(new
                {
                    success = true,
                    isDuplicate = true,
                    message = "Giao dịch này đã được xử lý và ghi nhận thành công từ trước.",
                    transactionCode = donation.TransactionCode,
                    amount = donation.Amount,
                    status = donation.Status.ToString()
                });
            }

            var oldStatus = donation.Status;
            var isPaymentSuccessful = payload.Status.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                || payload.Status.Equals("PAID", StringComparison.OrdinalIgnoreCase)
                || payload.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);

            // BƯỚC 4: XỬ LÝ THEO KẾT QUẢ THANH TOÁN TỪ ĐỐI TÁC
            if (isPaymentSuccessful)
            {
                // Kiểm tra số tiền nhận được có khớp với đơn đã tạo không
                if (payload.Amount < donation.Amount)
                {
                    _logger.LogWarning(
                        "[WEBHOOK SAI LỆCH SỐ TIỀN] Đơn {Code}: Yêu cầu {Expected}đ nhưng đối tác báo nhận {Received}đ.",
                        donation.TransactionCode, donation.Amount, payload.Amount);

                    donation.Status = DonationStatus.Disputed;
                    donation.IsDisputed = true;
                    donation.DisputeReason = $"Sai lệch số tiền: Cổng thanh toán báo nhận {payload.Amount:N0}đ nhưng đơn tạo là {donation.Amount:N0}đ.";
                    donation.AdminNote = "Chờ Admin đối soát lại sao kê ngân hàng do số tiền thanh toán không khớp.";

                    // Ghi nhận Audit Log
                    _context.TransactionAuditLogs.Add(new TransactionAuditLog
                    {
                        DonationId = donation.Id,
                        TransactionCode = donation.TransactionCode ?? string.Empty,
                        ActionType = "DISPUTE_AMOUNT_MISMATCH",
                        OldStatus = oldStatus,
                        NewStatus = DonationStatus.Disputed,
                        Note = $"Cảnh báo lệch tiền từ Webhook {effectiveGateway}. Nhận: {payload.Amount:N0}đ, Mong đợi: {donation.Amount:N0}đ. IP: {clientIp}",
                        PerformedBy = $"Webhook:{effectiveGateway}",
                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        success = false,
                        error = "AMOUNT_MISMATCH",
                        message = "Số tiền thanh toán không khớp với đơn hàng. Đơn đã được chuyển sang trạng thái tranh chấp chờ xác minh.",
                        transactionCode = donation.TransactionCode
                    });
                }

                // Cập nhật trạng thái giao dịch thành CÔNG
                donation.Status = DonationStatus.Success;

                // Cộng tiền nhận được vào tài khoản của Streamer
                if (donation.StreamerProfile != null)
                {
                    donation.StreamerProfile.TotalReceived += donation.Amount;
                }

                // BƯỚC 5: GHI NHẬN LỊCH SỬ KIỂM TOÁN (AUDIT TRAIL) VÀO BẢNG TransactionAuditLogs
                var auditNote = $"Xác thực Webhook thành công từ IP {clientIp}. Cổng: {effectiveGateway}. Mã GD đối tác: {payload.GatewayTransactionId ?? "N/A"}. Số tiền: {payload.Amount:N0} VNĐ.";
                _context.TransactionAuditLogs.Add(new TransactionAuditLog
                {
                    DonationId = donation.Id,
                    TransactionCode = donation.TransactionCode ?? string.Empty,
                    ActionType = "WEBHOOK_VERIFIED_SUCCESS",
                    OldStatus = oldStatus,
                    NewStatus = DonationStatus.Success,
                    Note = auditNote,
                    PerformedBy = $"Webhook:{effectiveGateway}",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[WEBHOOK THÀNH CÔNG] Đã xác nhận đơn donate {TransactionCode}, cộng {Amount}đ cho streamer {StreamerSlug}",
                    donation.TransactionCode, donation.Amount, donation.StreamerProfile?.Slug);

                return Ok(new
                {
                    success = true,
                    message = "Xác thực và xử lý Webhook thanh toán thành công!",
                    transactionCode = donation.TransactionCode,
                    amount = donation.Amount,
                    status = donation.Status.ToString(),
                    gateway = effectiveGateway,
                    processedAt = DateTime.UtcNow
                });
            }
            else
            {
                // Thanh toán thất bại từ phía đối tác
                donation.Status = DonationStatus.Failed;

                _context.TransactionAuditLogs.Add(new TransactionAuditLog
                {
                    DonationId = donation.Id,
                    TransactionCode = donation.TransactionCode ?? string.Empty,
                    ActionType = "WEBHOOK_PAYMENT_FAILED",
                    OldStatus = oldStatus,
                    NewStatus = DonationStatus.Failed,
                    Note = $"Cổng thanh toán {effectiveGateway} thông báo thanh toán thất bại (Trạng thái: {payload.Status}). IP: {clientIp}",
                    PerformedBy = $"Webhook:{effectiveGateway}",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Ghi nhận trạng thái thanh toán thất bại từ cổng thanh toán.",
                    transactionCode = donation.TransactionCode,
                    status = donation.Status.ToString()
                });
            }
        }
    }
}
