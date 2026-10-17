using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Areas.Widgets.Services;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Data;
using DonateWeb.Hubs;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;
using DonateWeb.Services;

namespace DonateWeb.Security.Webhook;

/// <summary>Nhận biến động tài khoản trung tâm từ SePay và phân bổ donate cho streamer theo mã chuyển khoản.</summary>
[ApiController]
[Route("api/webhook/sepay")]
public sealed class SePayWebhookController : ControllerBase
{
    private const string AnonymousDonorName = "Người ủng hộ ẩn danh";
    private readonly AppDbContext _context;
    private readonly IHubContext<PaymentHub> _hubContext;
    private readonly IWidgetService _widgetService;
    private readonly IContentModerationService _contentModeration;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(
        AppDbContext context,
        IHubContext<PaymentHub> hubContext,
        IWidgetService widgetService,
        IContentModerationService contentModeration,
        IConfiguration configuration,
        ILogger<SePayWebhookController> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _widgetService = widgetService;
        _contentModeration = contentModeration;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] SePayTransaction? payload, CancellationToken cancellationToken)
    {
        var expectedApiKey = _configuration["SePay:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            _logger.LogError("Webhook SePay chưa được bật vì thiếu SePay:ApiKey.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "SePay webhook is not configured." });
        }

        var authorization = Request.Headers["Authorization"].ToString();
        const string apiKeyScheme = "Apikey ";
        if (!authorization.StartsWith(apiKeyScheme, StringComparison.OrdinalIgnoreCase) ||
            !FixedTimeEquals(authorization[apiKeyScheme.Length..].Trim(), expectedApiKey))
        {
            return Unauthorized(new { success = false, message = "Invalid webhook credentials." });
        }

        if (payload == null || payload.Id <= 0 ||
            !string.Equals(payload.TransferType, "in", StringComparison.OrdinalIgnoreCase) ||
            payload.TransferAmount <= 0 || payload.TransferAmount != decimal.Truncate(payload.TransferAmount))
        {
            return Ok(new { success = true, ignored = true, message = "Not a valid incoming bank transfer." });
        }

        if (!TryReadDonationMemo(payload.Content, out var streamerId, out var rawMessage))
        {
            // Tài khoản có thể nhận giao dịch khác; chỉ mã DW{id} trong nội dung mới là donate.
            return Ok(new { success = true, ignored = true, message = "No DonateWeb streamer code in transfer content." });
        }

        var externalTransactionId = $"SEPAY-{payload.Id}";
        var centralAccount = NormalizeAccount(_configuration["SePay:AccountNumber"]);
        if (string.IsNullOrEmpty(centralAccount))
        {
            _logger.LogError("Webhook SePay chưa được bật vì thiếu SePay:AccountNumber (tài khoản trung tâm).");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Central SePay account is not configured." });
        }

        var receivedAccounts = new[] { payload.AccountNumber, payload.SubAccount }
            .Select(NormalizeAccount)
            .Where(account => !string.IsNullOrEmpty(account));
        if (!receivedAccounts.Contains(centralAccount, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Bỏ qua giao dịch SePay {TransactionId}: tài khoản nhận không khớp tài khoản trung tâm đã cấu hình.", payload.Id);
            return Ok(new { success = true, ignored = true, message = "Receiving bank account does not match the central account." });
        }

        Donation donation;
        try
        {
            // Ràng buộc unique trên ExternalTransactionId là lớp chống trùng cuối cùng nếu SePay retry đồng thời.
            await using (var dbTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
            {
                var alreadyProcessed = await _context.Donations
                    .AsNoTracking()
                    .AnyAsync(item => item.ExternalTransactionId == externalTransactionId, cancellationToken);
                if (alreadyProcessed)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    return Ok(new { success = true, duplicate = true });
                }

                // Đọc hồ sơ trong transaction để các khoản donate đồng thời không ghi đè tổng doanh thu.
                var streamer = await _context.StreamerProfiles
                    .FirstOrDefaultAsync(profile => profile.Id == streamerId, cancellationToken);

                if (streamer is null || !streamer.IsActive || streamer.ApprovalStatus != StreamerApprovalStatus.Approved)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    _logger.LogWarning("Bỏ qua giao dịch SePay {TransactionId}: streamer {StreamerId} không tồn tại, chưa duyệt hoặc bị khóa.", payload.Id, streamerId);
                    return Ok(new { success = true, ignored = true, message = "Streamer is not eligible for donations." });
                }

                // Doanh thu được ghi vào số dư streamer chờ quyết toán; ví donate tiêu dùng vẫn tách riêng.
                var streamerUser = await _context.Users
                    .FirstOrDefaultAsync(user => user.Id == streamer.UserId, cancellationToken);
                if (streamerUser is null)
                {
                    await dbTransaction.CommitAsync(cancellationToken);
                    _logger.LogError("Không ghi nhận donate SePay {TransactionId}: thiếu tài khoản người dùng của streamer {StreamerId}.", payload.Id, streamerId);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Streamer account is missing." });
                }

                // Không lấy tên chủ tài khoản chuyển tiền từ payload; khoản donate qua ngân hàng luôn ẩn danh.
                var cleanMessage = _contentModeration
                    .ModerateContent(rawMessage, AnonymousDonorName)
                    .SanitizedText
                    .Trim();
                if (string.IsNullOrWhiteSpace(cleanMessage))
                    cleanMessage = "Đã gửi ủng hộ";
                if (cleanMessage.Length > 1000)
                    cleanMessage = cleanMessage[..1000];

                donation = new Donation
                {
                    StreamerProfileId = streamer.Id,
                    StreamerProfile = streamer,
                    DonorUserId = null,
                    DonorName = AnonymousDonorName,
                    Amount = payload.TransferAmount,
                    Message = cleanMessage,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    Status = DonationStatus.Success,
                    TransactionCode = externalTransactionId,
                    ExternalTransactionId = externalTransactionId,
                    CreatedAt = DateTime.UtcNow
                };

                await SpecialEffectAssignment.ApplyToDonationAsync(_context, donation, cancellationToken);
                streamer.TotalReceived += donation.Amount;
                var streamerNet = DonationCommission.StreamerNet(donation.Amount);
                var platformFee = DonationCommission.PlatformFee(donation.Amount);
                streamerUser.StreamerEarningsBalance += streamerNet;
                streamerUser.UpdatedAt = DateTime.UtcNow;

                _context.Donations.Add(donation);
                _context.TransactionAuditLogs.Add(new TransactionAuditLog
                {
                    Donation = donation,
                    TransactionCode = externalTransactionId,
                    ActionType = "SEPAY_CENTRAL_DONATION_COMPLETED",
                    OldStatus = DonationStatus.Pending,
                    NewStatus = DonationStatus.Success,
                    Note = $"SePay received {payload.TransferAmount:N0} VND in the central account. Platform commission: {platformFee:N0} VND; streamer earnings credited for manual settlement: {streamerNet:N0} VND. Streamer: {streamer.Slug}. Reference: {payload.ReferenceCode}.",
                    PerformedBy = "SePay:Webhook",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);
                await dbTransaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _logger.LogInformation("Webhook SePay gửi lặp giao dịch {TransactionId}; bản ghi đã có.", payload.Id);
            return Ok(new { success = true, duplicate = true });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Không thể ghi nhận giao dịch SePay {TransactionId}.", payload.Id);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "Could not process bank transfer; retry later." });
        }

        try
        {
            await PublishDonationAlertAsync(donation);
        }
        catch (Exception exception)
        {
            // Tiền đã được ghi nhận trong DB; không yêu cầu SePay gửi lại để tránh retry vô ích.
            // AlertBox vẫn có thể bắt kịp khoản donate qua polling.
            _logger.LogError(exception, "Đã lưu donate SePay {TransactionId} nhưng không gửi được SignalR alert.", payload.Id);
        }
        _logger.LogInformation(
            "Đã nhận {Amount:N0}đ vào tài khoản trung tâm qua SePay và ghi nhận doanh thu cho streamer {StreamerId}; tên người gửi được ẩn danh.",
            donation.Amount, streamerId);
        return Ok(new { success = true, transactionCode = externalTransactionId });
    }

    private async Task PublishDonationAlertAsync(Donation donation)
    {
        var slug = donation.StreamerProfile.Slug.Trim().ToLowerInvariant();
        var alert = await _widgetService.CreateDonationAlertAsync(donation, slug);
        if (alert == null)
            return;

        await _hubContext.Clients.Group($"streamer_{donation.StreamerProfileId}")
            .SendAsync("OnNewDonation", alert);
        await _hubContext.Clients.Group("Streamer_" + slug)
            .SendAsync("ReceiveAlert", alert);

        if (alert.IsTtsEnabled)
        {
            await _hubContext.Clients.Group("Tts_" + slug)
                .SendAsync("ReceiveTtsAlert", new TtsDonationAlertDto
                {
                    DonationId = alert.DonationId,
                    DonorName = alert.DonorName,
                    Amount = alert.Amount,
                    Message = alert.Message ?? string.Empty
                });
        }
    }

    private static bool TryReadDonationMemo(string? content, out int streamerId, out string message)
    {
        streamerId = 0;
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(content)) return false;

        var match = Regex.Match(content.Trim(), "^DW(?<id>[1-9][0-9]{0,9})(?<tail>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["id"].Value, out streamerId))
            return false;

        var tail = match.Groups["tail"].Value;
        if (tail.Length > 0 && !char.IsWhiteSpace(tail[0]) && !":-|".Contains(tail[0]))
            return false;

        message = tail.TrimStart(' ', '\t', '\r', '\n', ':', '-', '|');
        return true;
    }

    private static string NormalizeAccount(string? account) =>
        new((account ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;

    public sealed class SePayTransaction
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("subAccount")]
        public string? SubAccount { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("transferType")]
        public string? TransferType { get; set; }

        [JsonPropertyName("transferAmount")]
        public decimal TransferAmount { get; set; }

        [JsonPropertyName("referenceCode")]
        public string? ReferenceCode { get; set; }
    }
}
