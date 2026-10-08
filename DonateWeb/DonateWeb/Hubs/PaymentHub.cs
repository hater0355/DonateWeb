using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;

namespace DonateWeb.Hubs
{
    /// <summary>
    /// SIGNALR HUB: XỬ LÝ SỰ KIỆN THANH TOÁN & THÔNG BÁO THỜI GIAN THỰC (REAL-TIME)
    /// Hỗ trợ:
    /// 1. Màn hình Nạp tiền / Donate của Viewer: Tự động chuyển trang "Thanh toán thành công" khi Webhook nhận được tiền.
    /// 2. Màn hình OBS Studio (Alert Box): Bắn thông báo donate mới, nảy animation, phát âm thanh và Text-to-Speech không có độ trễ.
    /// 3. Cập nhật biến động số dư ví tài khoản thời gian thực.
    /// </summary>
    public class PaymentHub : Hub
    {
        private readonly ILogger<PaymentHub> _logger;
        private readonly AppDbContext _context;

        public PaymentHub(ILogger<PaymentHub> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// Tham gia vào nhóm theo dõi trạng thái của một giao dịch cụ thể
        /// Client truyền mã giao dịch (ví dụ: "DEP_20261002_ABC123", "NAP12345", "DON_20261002_XYZ789", "DN12345")
        /// </summary>
        public async Task JoinTransactionGroup(string transactionCode)
        {
            if (!string.IsNullOrWhiteSpace(transactionCode))
            {
                var cleanCode = transactionCode.Trim().ToUpperInvariant();
                await Groups.AddToGroupAsync(Context.ConnectionId, "Tx_" + cleanCode);
                _logger.LogDebug("[SignalR] Connection {ConnectionId} đã tham gia nhóm Tx_{CleanCode}", Context.ConnectionId, cleanCode);
            }
        }

        /// <summary>
        /// Rời khỏi nhóm theo dõi giao dịch
        /// </summary>
        public async Task LeaveTransactionGroup(string transactionCode)
        {
            if (!string.IsNullOrWhiteSpace(transactionCode))
            {
                var cleanCode = transactionCode.Trim().ToUpperInvariant();
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Tx_" + cleanCode);
                _logger.LogDebug("[SignalR] Connection {ConnectionId} đã rời nhóm Tx_{CleanCode}", Context.ConnectionId, cleanCode);
            }
        }

        /// <summary>
        /// Tham gia vào nhóm của Streamer (dành cho OBS Studio Browser Source và trang Streamer)
        /// </summary>
        public async Task JoinStreamerGroup(string streamerSlug)
        {
            if (!string.IsNullOrWhiteSpace(streamerSlug))
            {
                var cleanSlug = streamerSlug.Trim().ToLowerInvariant();
                await Groups.AddToGroupAsync(Context.ConnectionId, "Streamer_" + cleanSlug);
                _logger.LogInformation("[SignalR OBS] Connection {ConnectionId} đã kết nối vào nhóm Streamer_{CleanSlug}", Context.ConnectionId, cleanSlug);
            }
        }

        public async Task JoinTtsGroup(string streamerSlug, string widgetToken)
        {
            var cleanSlug = (streamerSlug ?? string.Empty).Trim().ToLowerInvariant();
            if (cleanSlug.Length is 0 or > 100 || string.IsNullOrWhiteSpace(widgetToken))
                throw new HubException("Thông tin kết nối trợ lý không hợp lệ.");

            var storedToken = await (
                from config in _context.AlertBoxConfigs
                join profile in _context.StreamerProfiles on config.StreamerProfileId equals profile.Id
                where profile.Slug.ToLower() == cleanSlug
                select config.WidgetToken)
                .FirstOrDefaultAsync(Context.ConnectionAborted);

            if (!FixedTimeEquals(widgetToken, storedToken))
                throw new HubException("Widget Token không hợp lệ.");

            await Groups.AddToGroupAsync(Context.ConnectionId, "Tts_" + cleanSlug, Context.ConnectionAborted);
        }

        private static bool FixedTimeEquals(string provided, string? expected)
        {
            if (string.IsNullOrEmpty(expected)) return false;
            var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
            return providedBytes.Length == expectedBytes.Length &&
                   System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }

        /// <summary>
        /// Tham gia vào nhóm Người dùng (dành cho Header hiển thị số dư ví)
        /// </summary>
        public async Task JoinUserGroup(string userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "User_" + userId.Trim());
            }
        }

        public override Task OnConnectedAsync()
        {
            _logger.LogDebug("[SignalR] Client kết nối: {ConnectionId}", Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug("[SignalR] Client ngắt kết nối: {ConnectionId}", Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }
    }
}
