using System.Text.RegularExpressions;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Data;
using DonateWeb.Hubs;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Services;

namespace DonateWeb.Security.Webhook
{
    /// <summary>
    /// CONTROLLER TIẾP NHẬN VÀ XỬ LÝ WEBHOOK TỪ PAYOS VÀ CÁC ĐỐI TÁC THANH TOÁN (VIETQR, SEPAY, MOMO, VNPAY, MBBANK)
    /// Thư mục riêng: Security/Webhook/
    /// </summary>
    [ApiController]
    [Route("api/webhook/payment")]
    public class PaymentWebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<PaymentHub> _hubContext;
        private readonly IWidgetService _widgetService;
        private readonly IIpRateLimiterService _ipResolver;
        private readonly ILogger<PaymentWebhookController> _logger;
        private readonly IPayOSService _payOS;

        public PaymentWebhookController(
            AppDbContext context,
            IHubContext<PaymentHub> hubContext,
            IWidgetService widgetService,
            IIpRateLimiterService ipResolver,
            ILogger<PaymentWebhookController> logger,
            IPayOSService payOS)
        {
            _context = context;
            _hubContext = hubContext;
            _widgetService = widgetService;
            _ipResolver = ipResolver;
            _logger = logger;
            _payOS = payOS;
        }

        /// <summary>
        /// Endpoint tiếp nhận Webhook chính thức từ PayOS
        /// POST /api/payment/webhook
        /// Xác thực chữ ký số bằng ChecksumKey qua _payOS.verifyPaymentWebhookData(body)
        /// Tự động cập nhật số dư cho User và trạng thái WalletTransaction sang Completed
        /// </summary>
        [HttpPost("/api/payment/webhook")]
        public async Task<IActionResult> HandlePayOSWebhook([FromBody] WebhookType body)
        {
            if (body == null)
            {
                return BadRequest(new { message = "Webhook payload is required." });
            }

            WebhookData data;
            try
            {
                data = _payOS.verifyPaymentWebhookData(body);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PAYOS WEBHOOK] Từ chối payload có chữ ký không hợp lệ.");
                return Unauthorized(new { message = "Invalid PayOS signature." });
            }

            if (!string.Equals(body.code, "00", StringComparison.Ordinal))
            {
                return Ok(new { message = "Payment was not successful; no wallet change was made." });
            }

            return await ProcessVerifiedPayOSWebhookAsync(data);
        }

        private async Task<IActionResult> ProcessVerifiedPayOSWebhookAsync(WebhookData data)
        {
            try
            {
                var orderCode = data.orderCode;
                var amount = data.amount;
                _logger.LogInformation("[PAYOS WEBHOOK] Đã xác thực thanh toán: OrderCode={OrderCode}, Amount={Amount}", orderCode, amount);

                WalletTransaction? walletTx;
                await using (var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    walletTx = await _context.WalletTransactions
                        .Include(wt => wt.User)
                        .FirstOrDefaultAsync(wt => wt.OrderCode == orderCode &&
                            wt.TransactionType == "DEPOSIT" && wt.Status == WalletTransaction.StatusPending);

                    if (walletTx != null)
                    {
                        if (walletTx.Amount != amount)
                        {
                            var mismatchAlreadyRecorded = await _context.WalletTransactionAuditLogs.AnyAsync(log =>
                                log.WalletTransactionId == walletTx.Id &&
                                log.ActionType == "PAYOS_AMOUNT_MISMATCH" &&
                                log.ReferenceCode == data.reference &&
                                log.ReceivedAmount == amount);
                            if (!mismatchAlreadyRecorded)
                            {
                                _context.WalletTransactionAuditLogs.Add(new WalletTransactionAuditLog
                                {
                                    WalletTransactionId = walletTx.Id,
                                    ActionType = "PAYOS_AMOUNT_MISMATCH",
                                    ExpectedAmount = walletTx.Amount,
                                    ReceivedAmount = amount,
                                    ReferenceCode = data.reference,
                                    Note = $"PayOS reported {amount} VND for order {orderCode}; expected {walletTx.Amount} VND. Wallet credit withheld for reconciliation.",
                                    CreatedAt = DateTime.UtcNow
                                });
                                await _context.SaveChangesAsync();
                            }
                            await dbTransaction.CommitAsync();
                            _logger.LogWarning("[PAYOS WEBHOOK] Amount mismatch for order {OrderCode}: expected {Expected}, received {Received}; held for reconciliation.",
                                orderCode, walletTx.Amount, amount);
                            return Ok(new { message = "Amount mismatch recorded for reconciliation; wallet was not credited." });
                        }

                        walletTx.Status = WalletTransaction.StatusCompleted;
                        walletTx.BalanceBefore = walletTx.User.WalletBalance;
                        walletTx.User.WalletBalance += walletTx.Amount;
                        walletTx.BalanceAfter = walletTx.User.WalletBalance;
                        walletTx.Note = (walletTx.Note ?? "") + $" [PayOS Webhook #{orderCode} - Ref: {data.reference}]";
                        walletTx.User.UpdatedAt = DateTime.UtcNow;
                        _context.WalletTransactionAuditLogs.Add(new WalletTransactionAuditLog
                        {
                            WalletTransactionId = walletTx.Id,
                            ActionType = "PAYOS_DEPOSIT_COMPLETED",
                            ExpectedAmount = walletTx.Amount,
                            ReceivedAmount = amount,
                            ReferenceCode = data.reference,
                            Note = $"PayOS deposit credited. OrderCode: {orderCode}.",
                            CreatedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                        await dbTransaction.CommitAsync();
                    }
                    else
                    {
                        await dbTransaction.RollbackAsync();
                    }
                }

                if (walletTx != null)
                {
                    var payload = new
                    {
                        transactionId = walletTx.Id,
                        transactionCode = walletTx.TransactionCode,
                        orderCode,
                        amount,
                        newBalance = walletTx.User.WalletBalance,
                        isPaid = true,
                        message = "Nạp tiền thành công!"
                    };
                    await _hubContext.Clients.Group("Tx_" + orderCode).SendAsync("PaymentSuccess", payload);
                    await _hubContext.Clients.Group("Tx_" + walletTx.TransactionCode.ToUpperInvariant()).SendAsync("PaymentSuccess", payload);
                    await _hubContext.Clients.Group("User_" + walletTx.UserId).SendAsync("WalletBalanceUpdated", new { newBalance = walletTx.User.WalletBalance });
                    return Ok(new { message = "Success" });
                }

                Donation? donation;
                await using (var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
                {
                    var description = data.description?.Trim();
                    var donationId = ExtractTransactionFromContent(description).NumericId;
                    donation = await _context.Donations
                        .Include(d => d.StreamerProfile).ThenInclude(sp => sp.User)
                        .FirstOrDefaultAsync(d => d.Status == DonationStatus.Pending &&
                            (d.TransactionCode == $"DON_{orderCode}" ||
                             d.TransactionCode == orderCode.ToString() ||
                             (description != null && d.TransactionCode == description) ||
                             (donationId.HasValue && d.Id == donationId.Value)));
                    if (donation == null)
                    {
                        await dbTransaction.RollbackAsync();
                    }
                    else if (donation.Amount != amount)
                    {
                        donation.IsDisputed = true;
                        donation.DisputeReason = $"PayOS amount mismatch: received {amount:N0} VND; expected {donation.Amount:N0} VND.";
                        donation.AdminNote = "Payment held for manual reconciliation; streamer balance was not credited.";
                        var mismatchAlreadyRecorded = await _context.TransactionAuditLogs.AnyAsync(log =>
                            log.DonationId == donation.Id &&
                            log.ActionType == "PAYOS_AMOUNT_MISMATCH" &&
                            log.Note == $"Order {orderCode}: expected {donation.Amount:N0} VND, received {amount:N0} VND. Ref: {data.reference}.");
                        if (!mismatchAlreadyRecorded)
                        {
                            _context.TransactionAuditLogs.Add(new TransactionAuditLog
                            {
                                DonationId = donation.Id,
                                TransactionCode = donation.TransactionCode ?? $"DON_{donation.Id}",
                                ActionType = "PAYOS_AMOUNT_MISMATCH",
                                OldStatus = donation.Status,
                                NewStatus = donation.Status,
                                Note = $"Order {orderCode}: expected {donation.Amount:N0} VND, received {amount:N0} VND. Ref: {data.reference}.",
                                PerformedBy = "PayOS:Webhook",
                                CreatedAt = DateTime.UtcNow
                            });
                            await _context.SaveChangesAsync();
                        }
                        await dbTransaction.CommitAsync();
                        _logger.LogWarning("[PAYOS WEBHOOK] Donation amount mismatch for order {OrderCode}; held for reconciliation.", orderCode);
                        return Ok(new { message = "Amount mismatch recorded for reconciliation; donation was not credited." });
                    }
                    else
                    {
                        donation.Status = DonationStatus.Success;
                        if (donation.StreamerProfile != null)
                        {
                            donation.StreamerProfile.TotalReceived += donation.Amount;
                            if (donation.StreamerProfile.User != null)
                            {
                                donation.StreamerProfile.User.WalletBalance += donation.Amount;
                            }
                        }
                        _context.TransactionAuditLogs.Add(new TransactionAuditLog
                        {
                            DonationId = donation.Id,
                            TransactionCode = donation.TransactionCode ?? $"DON_{donation.Id}",
                            ActionType = "PAYOS_DONATION_COMPLETED",
                            OldStatus = DonationStatus.Pending,
                            NewStatus = DonationStatus.Success,
                            Note = $"PayOS donation credited. OrderCode: {orderCode}; Amount: {amount:N0} VND; Ref: {data.reference}.",
                            PerformedBy = "PayOS:Webhook",
                            CreatedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                        await dbTransaction.CommitAsync();
                    }
                }

                if (donation == null)
                {
                    _logger.LogWarning("[PAYOS WEBHOOK] No pending wallet transaction or donation found for order {OrderCode}.", orderCode);
                    return Ok(new { message = "No matching pending transaction." });
                }

                if (donation.StreamerProfile != null && !string.IsNullOrWhiteSpace(donation.StreamerProfile.Slug))
                {
                    var streamerSlug = donation.StreamerProfile.Slug.Trim().ToLowerInvariant();
                    var alertDto = await _widgetService.CreateDonationAlertAsync(donation, streamerSlug);
                    if (alertDto != null)
                    {
                        await _hubContext.Clients.Group("Streamer_" + streamerSlug).SendAsync("ReceiveAlert", alertDto);
                    }
                }
                await _hubContext.Clients.Group("Tx_" + orderCode).SendAsync("DonationSuccess", new { orderCode, amount = donation.Amount, isPaid = true });
                return Ok(new { message = "Success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PAYOS WEBHOOK ERROR] Xử lý đơn thanh toán thất bại.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Webhook processing failed; retry later." });
            }
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
                message = "Hệ thống Webhook bảo mật PayOS/VietQR của DonateWeb đang hoạt động hoàn hảo.",
                clientIp,
                timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Endpoint API Polling kiểm tra trạng thái giao dịch (Dành cho Client Fallback khi WebSocket không khả dụng)
        /// GET /api/webhook/payment/check-status?code=NAP12345 hoặc ?code=DN12345 hoặc ?code=DEP_...
        /// </summary>
        [HttpGet("check-status")]
        public async Task<IActionResult> CheckStatus([FromQuery] string? code)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return Unauthorized(new { success = false, message = "Bạn cần đăng nhập để tra cứu giao dịch." });
            }

            Response.Headers.CacheControl = "no-store";

            if (string.IsNullOrWhiteSpace(code))
            {
                return BadRequest(new { success = false, message = "Thiếu mã giao dịch (code)." });
            }

            var cleanCode = code.Trim();
            var extracted = ExtractTransactionFromContent(cleanCode);

            // Kiểm tra trong bảng WalletTransaction (Nạp tiền)
            if (extracted.Type == TransactionTargetType.Deposit || cleanCode.StartsWith("DEP_", StringComparison.OrdinalIgnoreCase) || cleanCode.StartsWith("NAP", StringComparison.OrdinalIgnoreCase))
            {
                var tx = await FindWalletTransactionAsync(extracted, cleanCode);
                if (tx != null && tx.UserId == userId)
                {
                    return Ok(new
                    {
                        success = true,
                        type = "DEPOSIT",
                        isPaid = tx.Status == 1,
                        status = tx.Status,
                        amount = tx.Amount,
                        transactionCode = tx.TransactionCode,
                        newBalance = tx.User?.WalletBalance,
                        message = tx.Status == 1 ? "Giao dịch nạp tiền đã thành công!" : "Đang chờ thanh toán..."
                    });
                }
            }

            // Kiểm tra trong bảng Donation (Ủng hộ Streamer)
            var donation = await FindDonationAsync(extracted, cleanCode);
            if (donation != null && donation.DonorUserId == userId)
            {
                return Ok(new
                {
                    success = true,
                    type = "DONATION",
                    isPaid = donation.Status == DonationStatus.Success,
                    status = donation.Status.ToString(),
                    amount = donation.Amount,
                    transactionCode = donation.TransactionCode,
                    streamerName = donation.StreamerProfile?.DisplayName,
                    message = donation.Status == DonationStatus.Success ? "Ủng hộ streamer thành công!" : "Đang chờ thanh toán..."
                });
            }

            return NotFound(new { success = false, isPaid = false, message = "Không tìm thấy giao dịch với mã này." });
        }

        /// <summary>
        /// Endpoint chính tiếp nhận thông báo thanh toán (Webhook callback) từ PayOS và các cổng thanh toán
        /// Hỗ trợ cả:
        /// - POST /api/webhook/payment
        /// - POST /api/webhook/payment/payos
        /// - POST /api/webhook/payment/{gateway}
        /// </summary>
        [HttpPost]
        [HttpPost("{gateway}")]
        public async Task<IActionResult> ProcessPaymentWebhook([FromRoute] string? gateway, [FromBody] PaymentWebhookPayload payload)
        {
            if (payload == null)
            {
                return BadRequest(new { success = false, message = "Payload không được để trống." });
            }

            if (gateway != null && !string.Equals(gateway, "payos", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status410Gone, new
                {
                    success = false,
                    message = "Gateway này chưa được cấu hình xác minh chữ ký. Chỉ PayOS webhook được hỗ trợ tại endpoint này."
                });
            }

            if (payload.Data == null || string.IsNullOrWhiteSpace(payload.Signature))
            {
                return Unauthorized(new { success = false, message = "PayOS webhook phải có data và chữ ký hợp lệ." });
            }

            if (payload.Data.Amount < 0 || payload.Data.Amount > int.MaxValue ||
                decimal.Truncate(payload.Data.Amount) != payload.Data.Amount)
            {
                return BadRequest(new { success = false, message = "Số tiền PayOS không hợp lệ." });
            }

            var payOSBody = new WebhookType
            {
                code = payload.Code ?? string.Empty,
                desc = payload.Desc ?? string.Empty,
                success = !string.IsNullOrWhiteSpace(payload.Status) && payload.IsPaymentSuccess(),
                signature = payload.Signature,
                data = new WebhookData
                {
                    orderCode = payload.Data.OrderCode,
                    amount = (int)payload.Data.Amount,
                    description = payload.Data.Description ?? string.Empty,
                    accountNumber = payload.Data.AccountNumber ?? string.Empty,
                    reference = payload.Data.Reference ?? string.Empty,
                    transactionDateTime = payload.Data.TransactionDateTime ?? string.Empty,
                    currency = payload.Data.Currency ?? "VND",
                    paymentLinkId = payload.Data.PaymentLinkId ?? string.Empty,
                    code = payload.Data.Code ?? string.Empty,
                    desc = payload.Data.Desc ?? string.Empty,
                    counterAccountBankId = payload.Data.CounterAccountBankId,
                    counterAccountBankName = payload.Data.CounterAccountBankName,
                    counterAccountName = payload.Data.CounterAccountName,
                    counterAccountNumber = payload.Data.CounterAccountNumber,
                    virtualAccountName = payload.Data.VirtualAccountName,
                    virtualAccountNumber = payload.Data.VirtualAccountNumber
                }
            };

            WebhookData verifiedData;
            try
            {
                verifiedData = _payOS.verifyPaymentWebhookData(payOSBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PAYOS WEBHOOK] Từ chối payload legacy có chữ ký không hợp lệ.");
                return Unauthorized(new { success = false, message = "Invalid PayOS signature." });
            }

            return await ProcessVerifiedPayOSWebhookAsync(verifiedData);
        }

        // =========================================================================================
        // XỬ LÝ NẠP TIỀN VÀO VÍ CÁ NHÂN (WALLET TRANSACTION)
        // =========================================================================================
        private async Task<IActionResult> HandleDepositWebhookAsync(
            ExtractedTransactionInfo extracted,
            string transferContent,
            decimal receivedAmount,
            string referenceCode,
            string gateway,
            string clientIp,
            bool isPaymentSuccessful,
            WalletTransaction? existingTx = null)
        {
            var walletTx = existingTx ?? await FindWalletTransactionAsync(extracted, transferContent);

            if (walletTx == null)
            {
                _logger.LogWarning("[WEBHOOK NẠP TIỀN] Không tìm thấy giao dịch ví cho nội dung '{Content}'", transferContent);
                return Ok(new
                {
                    success = false,
                    error = "WALLET_TRANSACTION_NOT_FOUND",
                    message = $"Không tìm thấy giao dịch Nạp tiền tương ứng với nội dung '{transferContent}'.",
                    transferContent
                });
            }

            // Cơ chế Chống lặp giao dịch (Idempotency): Nếu đơn này đã thành công từ trước, trả về 200 OK ngay mà không cộng thêm tiền
            if (walletTx.Status == 1)
            {
                _logger.LogInformation(
                    "[WEBHOOK IDEMPOTENT] Giao dịch nạp tiền {TxCode} (ID: {Id}) đã thành công từ trước. Bỏ qua cộng tiền lặp lại.",
                    walletTx.TransactionCode, walletTx.Id);

                return Ok(new
                {
                    success = true,
                    isDuplicate = true,
                    message = "Giao dịch nạp tiền này đã được ghi nhận thành công từ trước.",
                    transactionCode = walletTx.TransactionCode,
                    amount = walletTx.Amount,
                    status = "Success"
                });
            }

            if (!isPaymentSuccessful)
            {
                walletTx.Status = 2; // Failed
                walletTx.Note = (walletTx.Note ?? "") + $" [Cổng thanh toán báo thất bại từ IP {clientIp}]";
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Ghi nhận trạng thái thanh toán thất bại từ đối tác.",
                    transactionCode = walletTx.TransactionCode
                });
            }

            // Kiểm tra số tiền nhận được có khớp với đơn đã tạo không
            if (receivedAmount < walletTx.Amount)
            {
                _logger.LogWarning(
                    "[WEBHOOK SAI LỆCH SỐ TIỀN NẠP] Đơn {Code}: Yêu cầu {Expected:N0}đ nhưng đối tác báo nhận {Received:N0}đ.",
                    walletTx.TransactionCode, walletTx.Amount, receivedAmount);

                walletTx.Note = (walletTx.Note ?? "") + $" [Cảnh báo: Nhận {receivedAmount:N0}đ, thiếu {walletTx.Amount - receivedAmount:N0}đ]";
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = false,
                    error = "AMOUNT_MISMATCH",
                    message = $"Số tiền nạp không đủ (Yêu cầu {walletTx.Amount:N0}đ, thực nhận {receivedAmount:N0}đ). Đã lưu lại ghi chú để Admin kiểm tra.",
                    transactionCode = walletTx.TransactionCode
                });
            }

            // 1. Chuyển trạng thái giao dịch nạp tiền sang Thành công (Status = 1)
            var oldStatus = walletTx.Status;
            walletTx.Status = 1;

            // 2. Lấy User tương ứng và cộng tiền vào ví
            var user = walletTx.User ?? await _context.Users.FindAsync(walletTx.UserId);
            if (user != null)
            {
                walletTx.BalanceBefore = user.WalletBalance;
                user.WalletBalance += walletTx.Amount;
                walletTx.BalanceAfter = user.WalletBalance;
                user.UpdatedAt = DateTime.UtcNow;
            }

            walletTx.Note = (walletTx.Note ?? "") + $" [Thanh toán thành công qua PayOS - Ref: {referenceCode} - Nhận: {receivedAmount:N0}đ]";

            // 3. Ghi vết kiểm toán (Audit Trail)
            _context.TransactionAuditLogs.Add(new TransactionAuditLog
            {
                DonationId = 0,
                TransactionCode = walletTx.TransactionCode,
                ActionType = "WEBHOOK_DEPOSIT_SUCCESS",
                OldStatus = (DonationStatus)oldStatus,
                NewStatus = DonationStatus.Success,
                Note = $"Nạp tiền ví thành công từ Webhook {gateway}. Mã GD: {walletTx.TransactionCode}, Số tiền: +{walletTx.Amount:N0}đ, Số dư mới: {user?.WalletBalance:N0}đ, Mã tham chiếu ngân hàng: {referenceCode}, IP: {clientIp}",
                PerformedBy = $"Webhook:{gateway}",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "[WEBHOOK NẠP TIỀN THÀNH CÔNG] Đã cộng {Amount:N0}đ cho User ID {UserId} ({Username}). Số dư mới: {NewBalance:N0}đ",
                walletTx.Amount, user?.Id, user?.Username, user?.WalletBalance);

            // 4. BẮN SỰ KIỆN SIGNALR REAL-TIME ĐẾN VIEW NẠP TIỀN CỦA VIEWER
            // Tự động nhận diện trên màn hình VietQR để chuyển sang màn hình chúc mừng mà không cần F5
            var paymentResultPayload = new
            {
                transactionId = walletTx.Id,
                transactionCode = walletTx.TransactionCode,
                amount = walletTx.Amount,
                newBalance = user?.WalletBalance,
                isPaid = true,
                message = "Nạp tiền vào ví thành công!",
                timestamp = DateTime.UtcNow
            };

            // Bắn theo mã giao dịch duy nhất
            await _hubContext.Clients.Group("Tx_" + walletTx.TransactionCode.ToUpperInvariant())
                .SendAsync("PaymentSuccess", paymentResultPayload);

            // Bắn theo ID số của giao dịch
            await _hubContext.Clients.Group("Tx_" + walletTx.Id)
                .SendAsync("PaymentSuccess", paymentResultPayload);

            // Bắn theo User ID để cập nhật số dư hiển thị trên toàn bộ các tab của người dùng
            if (user != null)
            {
                await _hubContext.Clients.Group("User_" + user.Id)
                    .SendAsync("WalletBalanceUpdated", new { newBalance = user.WalletBalance });
            }

            return Ok(new
            {
                success = true,
                type = "DEPOSIT",
                message = "Xác nhận nạp tiền vào ví thành công!",
                transactionCode = walletTx.TransactionCode,
                amount = walletTx.Amount,
                newBalance = user?.WalletBalance,
                status = "Success"
            });
        }

        // =========================================================================================
        // XỬ LÝ QUYÊN GÓP CHO STREAMER (DONATION)
        // =========================================================================================
        private async Task<IActionResult> HandleDonationWebhookAsync(
            ExtractedTransactionInfo extracted,
            string transferContent,
            decimal receivedAmount,
            string referenceCode,
            string gateway,
            string clientIp,
            bool isPaymentSuccessful,
            Donation? existingDonation = null)
        {
            var donation = existingDonation ?? await FindDonationAsync(extracted, transferContent);

            if (donation == null)
            {
                _logger.LogWarning("[WEBHOOK DONATE] Không tìm thấy đơn donate cho nội dung '{Content}'", transferContent);
                return Ok(new
                {
                    success = false,
                    error = "DONATION_NOT_FOUND",
                    message = $"Không tìm thấy đơn Donate tương ứng với nội dung '{transferContent}'.",
                    transferContent
                });
            }

            // Cơ chế Chống lặp giao dịch (Idempotency): Nếu đơn này đã thành công từ trước, trả về 200 OK ngay mà không cộng thêm tiền
            if (donation.Status == DonationStatus.Success)
            {
                _logger.LogInformation(
                    "[WEBHOOK IDEMPOTENT] Đơn donate {TxCode} (ID: {Id}) đã thành công từ trước. Trả về 200 OK.",
                    donation.TransactionCode, donation.Id);

                return Ok(new
                {
                    success = true,
                    isDuplicate = true,
                    message = "Đơn donate này đã được xử lý thành công từ trước.",
                    transactionCode = donation.TransactionCode,
                    amount = donation.Amount,
                    status = donation.Status.ToString()
                });
            }

            var oldStatus = donation.Status;

            if (!isPaymentSuccessful)
            {
                donation.Status = DonationStatus.Failed;
                _context.TransactionAuditLogs.Add(new TransactionAuditLog
                {
                    DonationId = donation.Id,
                    TransactionCode = donation.TransactionCode ?? $"DN{donation.Id}",
                    ActionType = "WEBHOOK_DONATE_FAILED",
                    OldStatus = oldStatus,
                    NewStatus = DonationStatus.Failed,
                    Note = $"Cổng thanh toán {gateway} báo thanh toán thất bại từ IP {clientIp}",
                    PerformedBy = $"Webhook:{gateway}",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Ghi nhận trạng thái thanh toán donate thất bại từ đối tác.",
                    transactionCode = donation.TransactionCode
                });
            }

            // Kiểm tra số tiền nhận được
            if (receivedAmount < donation.Amount)
            {
                _logger.LogWarning(
                    "[WEBHOOK LỆCH TIỀN DONATE] Đơn {Code}: Yêu cầu {Expected:N0}đ nhưng đối tác báo nhận {Received:N0}đ.",
                    donation.TransactionCode, donation.Amount, receivedAmount);

                donation.Status = DonationStatus.Disputed;
                donation.IsDisputed = true;
                donation.DisputeReason = $"Sai lệch số tiền: Nhận {receivedAmount:N0}đ nhưng đơn là {donation.Amount:N0}đ.";
                donation.AdminNote = "Chờ Admin đối soát sao kê.";

                _context.TransactionAuditLogs.Add(new TransactionAuditLog
                {
                    DonationId = donation.Id,
                    TransactionCode = donation.TransactionCode ?? $"DN{donation.Id}",
                    ActionType = "DISPUTE_AMOUNT_MISMATCH",
                    OldStatus = oldStatus,
                    NewStatus = DonationStatus.Disputed,
                    Note = $"Lệch tiền donate từ {gateway}: Nhận {receivedAmount:N0}đ, Mong đợi: {donation.Amount:N0}đ. IP: {clientIp}",
                    PerformedBy = $"Webhook:{gateway}",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = false,
                    error = "AMOUNT_MISMATCH",
                    message = "Số tiền thanh toán không đủ. Đã chuyển đơn sang trạng thái tranh chấp chờ đối soát.",
                    transactionCode = donation.TransactionCode
                });
            }

            // 1. Cập nhật trạng thái đơn Donate thành Thành công (Success)
            donation.Status = DonationStatus.Success;

            // 2. Cập nhật doanh thu cho Streamer
            if (donation.StreamerProfile != null)
            {
                donation.StreamerProfile.TotalReceived += donation.Amount;

                // Cộng số dư ví tài khoản Streamer nếu Streamer có tài khoản User liên kết
                if (donation.StreamerProfile.User != null)
                {
                    donation.StreamerProfile.User.WalletBalance += donation.Amount;
                }
            }

            // 3. Ghi vết kiểm toán (Audit Trail)
            _context.TransactionAuditLogs.Add(new TransactionAuditLog
            {
                DonationId = donation.Id,
                TransactionCode = donation.TransactionCode ?? $"DN{donation.Id}",
                ActionType = "WEBHOOK_DONATE_SUCCESS",
                OldStatus = oldStatus,
                NewStatus = DonationStatus.Success,
                Note = $"Xác thực Webhook Donate thành công từ IP {clientIp}. Cổng: {gateway}. Mã tham chiếu: {referenceCode}. Nhận: {receivedAmount:N0}đ.",
                PerformedBy = $"Webhook:{gateway}",
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "[WEBHOOK DONATE THÀNH CÔNG] Đã xác nhận đơn donate {Code} ({Amount:N0}đ) cho streamer {StreamerSlug}",
                donation.TransactionCode, donation.Amount, donation.StreamerProfile?.Slug);

            // 4. BẮN SỰ KIỆN SIGNALR REAL-TIME ĐẾN OBS STUDIO ALERT BOX CỦA STREAMER
            if (donation.StreamerProfile != null && !string.IsNullOrWhiteSpace(donation.StreamerProfile.Slug))
            {
                var streamerSlug = donation.StreamerProfile.Slug.Trim().ToLowerInvariant();
                var alertDto = await _widgetService.CreateDonationAlertAsync(donation, streamerSlug);
                if (alertDto != null)
                {
                    // Bắn đến toàn bộ Browser Source OBS đang cắm đường dẫn widget của streamer này
                    await _hubContext.Clients.Group("Streamer_" + streamerSlug)
                        .SendAsync("ReceiveAlert", alertDto);

                    _logger.LogInformation(
                        "[SIGNALR OBS ALERT] Đã bắn sự kiện ReceiveAlert lên màn hình OBS cho Streamer {StreamerSlug}",
                        streamerSlug);
                }
            }

            // 5. BẮN SỰ KIỆN SIGNALR REAL-TIME ĐẾN MÀN HÌNH VIETQR CỦA VIEWER ĐANG CHỜ
            var donationResultPayload = new
            {
                donationId = donation.Id,
                transactionCode = donation.TransactionCode,
                amount = donation.Amount,
                donorName = donation.DonorName,
                streamerName = donation.StreamerProfile?.DisplayName,
                isPaid = true,
                message = "Ủng hộ thành công!",
                timestamp = DateTime.UtcNow
            };

            if (!string.IsNullOrWhiteSpace(donation.TransactionCode))
            {
                await _hubContext.Clients.Group("Tx_" + donation.TransactionCode.ToUpperInvariant())
                    .SendAsync("DonationSuccess", donationResultPayload);
            }
            await _hubContext.Clients.Group("Tx_" + donation.Id)
                .SendAsync("DonationSuccess", donationResultPayload);

            return Ok(new
            {
                success = true,
                type = "DONATION",
                message = "Xác nhận và xử lý Webhook Donate thành công!",
                transactionCode = donation.TransactionCode,
                amount = donation.Amount,
                streamer = donation.StreamerProfile?.DisplayName,
                status = donation.Status.ToString()
            });
        }

        // =========================================================================================
        // HÀM HỖ TRỢ TRÍCH XUẤT MÃ GIAO DỊCH TỪ NỘI DUNG CHUYỂN KHOẢN (REGEX & TEXT MATCHING)
        // =========================================================================================
        private static ExtractedTransactionInfo ExtractTransactionFromContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new ExtractedTransactionInfo();

            var normalized = content.Trim();

            // 1. Kiểm tra mã nạp tiền (Deposit): NAP12345, NAP 12345, DEP12345, DEP 12345
            var napMatch = Regex.Match(normalized, @"(?i)\b(?:NAP|DEP)\s*([0-9]+)\b");
            if (napMatch.Success && int.TryParse(napMatch.Groups[1].Value, out var napId))
            {
                return new ExtractedTransactionInfo
                {
                    Type = TransactionTargetType.Deposit,
                    NumericId = napId,
                    RawCode = napMatch.Value.Replace(" ", "").ToUpperInvariant()
                };
            }

            // Mã nạp tiền dạng chuỗi dài: DEP_20261002_ABC123
            var depCodeMatch = Regex.Match(normalized, @"(?i)\b(DEP_[A-Za-z0-9_]+)\b");
            if (depCodeMatch.Success)
            {
                return new ExtractedTransactionInfo
                {
                    Type = TransactionTargetType.Deposit,
                    RawCode = depCodeMatch.Groups[1].Value.ToUpperInvariant()
                };
            }

            // 2. Kiểm tra mã quyên góp (Donation): DN12345, DN 12345, DON12345, DON 12345
            var dnMatch = Regex.Match(normalized, @"(?i)\b(?:DN|DON)\s*([0-9]+)\b");
            if (dnMatch.Success && int.TryParse(dnMatch.Groups[1].Value, out var dnId))
            {
                return new ExtractedTransactionInfo
                {
                    Type = TransactionTargetType.Donation,
                    NumericId = dnId,
                    RawCode = dnMatch.Value.Replace(" ", "").ToUpperInvariant()
                };
            }

            // Mã donate dạng chuỗi dài: DON_20261002_XYZ789
            var donCodeMatch = Regex.Match(normalized, @"(?i)\b(DON_[A-Za-z0-9_]+)\b");
            if (donCodeMatch.Success)
            {
                return new ExtractedTransactionInfo
                {
                    Type = TransactionTargetType.Donation,
                    RawCode = donCodeMatch.Groups[1].Value.ToUpperInvariant()
                };
            }

            return new ExtractedTransactionInfo
            {
                Type = TransactionTargetType.Unknown,
                RawCode = normalized
            };
        }

        // =========================================================================================
        // HÀM TRUY VẤN CSDL CHO WALLET TRANSACTION
        // =========================================================================================
        private async Task<WalletTransaction?> FindWalletTransactionAsync(ExtractedTransactionInfo extracted, string searchContent)
        {
            // 1. Tìm theo ID số trích xuất được (ví dụ từ NAP 12345 -> Id = 12345)
            if (extracted.NumericId.HasValue && extracted.NumericId.Value > 0)
            {
                var txById = await _context.WalletTransactions
                    .Include(wt => wt.User)
                    .FirstOrDefaultAsync(wt => wt.Id == extracted.NumericId.Value);

                if (txById != null) return txById;
            }

            // 2. Tìm theo TransactionCode
            if (!string.IsNullOrWhiteSpace(extracted.RawCode))
            {
                var txByCode = await _context.WalletTransactions
                    .Include(wt => wt.User)
                    .FirstOrDefaultAsync(wt => wt.TransactionCode == extracted.RawCode || wt.Note == extracted.RawCode);

                if (txByCode != null) return txByCode;
            }

            // 3. Tìm theo Memo Note chứa trong nội dung chuyển khoản
            if (!string.IsNullOrWhiteSpace(searchContent))
            {
                var txByNote = await _context.WalletTransactions
                    .Include(wt => wt.User)
                    .Where(wt => wt.Status == 0) // Ưu tiên các đơn đang Pending
                    .OrderByDescending(wt => wt.Id)
                    .FirstOrDefaultAsync(wt => (!string.IsNullOrEmpty(wt.Note) && searchContent.Contains(wt.Note))
                                            || searchContent.Contains(wt.TransactionCode));

                if (txByNote != null) return txByNote;
            }

            return null;
        }

        // =========================================================================================
        // HÀM TRUY VẤN CSDL CHO DONATION
        // =========================================================================================
        private async Task<Donation?> FindDonationAsync(ExtractedTransactionInfo extracted, string searchContent)
        {
            // 1. Tìm theo ID số trích xuất được (ví dụ từ DN12345 -> Id = 12345)
            if (extracted.NumericId.HasValue && extracted.NumericId.Value > 0)
            {
                var donationById = await _context.Donations
                    .Include(d => d.StreamerProfile).ThenInclude(sp => sp.User)
                    .FirstOrDefaultAsync(d => d.Id == extracted.NumericId.Value);

                if (donationById != null) return donationById;
            }

            // 2. Tìm theo TransactionCode
            if (!string.IsNullOrWhiteSpace(extracted.RawCode))
            {
                var donationByCode = await _context.Donations
                    .Include(d => d.StreamerProfile).ThenInclude(sp => sp.User)
                    .FirstOrDefaultAsync(d => d.TransactionCode == extracted.RawCode);

                if (donationByCode != null) return donationByCode;
            }

            // 3. Tìm theo chuỗi nội dung khớp với TransactionCode
            if (!string.IsNullOrWhiteSpace(searchContent))
            {
                var donationByContent = await _context.Donations
                    .Include(d => d.StreamerProfile).ThenInclude(sp => sp.User)
                    .Where(d => d.Status == DonationStatus.Pending) // Ưu tiên đơn đang Pending
                    .OrderByDescending(d => d.Id)
                    .FirstOrDefaultAsync(d => !string.IsNullOrEmpty(d.TransactionCode) && searchContent.Contains(d.TransactionCode));

                if (donationByContent != null) return donationByContent;
            }

            return null;
        }
    }

    /// <summary>
    /// Phân loại đối tượng giao dịch
    /// </summary>
    public enum TransactionTargetType
    {
        Unknown,
        Deposit,   // Nạp tiền vào ví cá nhân (WalletTransaction)
        Donation   // Quyên góp cho Streamer (Donation)
    }

    /// <summary>
    /// Kết quả bóc tách mã giao dịch từ nội dung chuyển khoản
    /// </summary>
    public class ExtractedTransactionInfo
    {
        public TransactionTargetType Type { get; set; } = TransactionTargetType.Unknown;
        public int? NumericId { get; set; }
        public string? RawCode { get; set; }
    }
}
