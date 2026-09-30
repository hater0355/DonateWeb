namespace DonateWeb.Services
{
    public interface IAccountIdService
    {
        /// <summary>
        /// Tạo ngẫu nhiên ID 10 số cho tài khoản:
        /// - Streamer bắt đầu bằng số 1
        /// - Viewer bắt đầu bằng số 9
        /// - 4 số tiếp theo là năm đăng ký tài khoản (mặc định năm hiện tại)
        /// - 5 số cuối ngẫu nhiên (tổng cộng 1 + 4 + 5 = 10 số)
        /// </summary>
        Task<string> GenerateAccountIdAsync(bool isStreamer, int? registrationYear = null);

        /// <summary>
        /// Chuyển đổi ID tài khoản khi Viewer nâng cấp / đăng ký thành Streamer:
        /// - Đổi chữ số đầu tiên từ 9 thành 1 (giữ nguyên năm và các số còn lại nếu không trùng)
        /// </summary>
        Task<string> UpgradeToStreamerIdAsync(string? currentAccountId, int? registrationYear = null);

        /// <summary>
        /// Chuyển đổi ID tài khoản khi Streamer hủy vai trò / quay lại làm Viewer:
        /// - Đổi chữ số đầu tiên từ 1 thành 9 (giữ nguyên năm và các số còn lại nếu không trùng)
        /// </summary>
        Task<string> DowngradeToViewerIdAsync(string? currentAccountId, int? registrationYear = null);
    }
}
