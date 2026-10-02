using Microsoft.AspNetCore.SignalR;

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

        public PaymentHub(ILogger<PaymentHub> logger)
        {
            _logger = logger;
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
