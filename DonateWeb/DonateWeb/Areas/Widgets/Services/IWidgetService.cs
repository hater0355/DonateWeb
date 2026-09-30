using DonateWeb.Areas.Widgets.ViewModels;

namespace DonateWeb.Areas.Widgets.Services
{
    /// <summary>
    /// GIAO DIỆN QUẢN LÝ TIỆN ÍCH WIDGET CHO STREAMER (OBS STUDIO / LIVE STREAM)
    /// Thư mục riêng: Areas/Widgets/Services/
    /// Cung cấp nghiệp vụ cho 3 tính năng cốt lõi:
    /// 1. Cài đặt Alert Box (GIF/Video, âm thanh MP3, màu/font chữ, animation, TTS).
    /// 2. Quản lý Mục tiêu quyên góp (Goal): Tính toán tiến độ thanh chạy thực tế từ CSDL.
    /// 3. Thống kê & Báo cáo: Bảng xếp hạng Top Donate (ngày/tuần/tháng/toàn thời gian) & tin nhắn gần nhất.
    /// </summary>
    public interface IWidgetService
    {
        // --------------------------------------------------------------------
        // 1. CẤU HÌNH ALERT BOX
        // --------------------------------------------------------------------
        
        /// <summary>
        /// Lấy thông tin cấu hình Alert Box của streamer từ CSDL hoặc bộ nhớ cache
        /// </summary>
        Task<AlertBoxConfigViewModel> GetAlertBoxConfigAsync(string streamerSlug, string baseUrl);

        /// <summary>
        /// Lưu tùy biến cấu hình Alert Box (ảnh/video, âm thanh, màu chữ, font, animation) vào CSDL
        /// </summary>
        Task<bool> SaveAlertBoxConfigAsync(AlertBoxConfigViewModel model);

        /// <summary>
        /// Khôi phục cấu hình Alert Box về mặc định của hệ thống
        /// </summary>
        Task ResetAlertBoxConfigAsync(string streamerSlug);

        // --------------------------------------------------------------------
        // 2. QUẢN LÝ MỤC TIÊU QUYÊN GÓP (GOAL)
        // --------------------------------------------------------------------

        /// <summary>
        /// Lấy toàn bộ danh sách mục tiêu và tính toán tiến độ thanh chạy cho trang cấu hình
        /// </summary>
        Task<GoalManagementViewModel> GetGoalManagementAsync(string streamerSlug, string baseUrl);

        /// <summary>
        /// Lấy mục tiêu đang kích hoạt (IsActive) kèm tính toán tiến độ thanh chạy cho OBS
        /// Ví dụ: "Mua PC mới: 5.000.000đ / 10.000.000đ (50%)"
        /// </summary>
        Task<StreamerGoalViewModel?> GetActiveGoalAsync(string streamerSlug);

        /// <summary>
        /// Tạo mới hoặc cập nhật một mục tiêu quyên góp vào CSDL
        /// </summary>
        Task<bool> SaveGoalAsync(string streamerSlug, StreamerGoalViewModel model);

        /// <summary>
        /// Bật / Tắt kích hoạt mục tiêu hiển thị trên OBS
        /// </summary>
        Task<bool> ToggleGoalAsync(string streamerSlug, int goalId);

        /// <summary>
        /// Xóa vĩnh viễn một mục tiêu quyên góp
        /// </summary>
        Task<bool> DeleteGoalAsync(string streamerSlug, int goalId);

        // --------------------------------------------------------------------
        // 3. THỐNG KÊ, BÁO CÁO & XẾP HẠNG (LEADERBOARD & RECENT MESSAGES)
        // --------------------------------------------------------------------

        /// <summary>
        /// Lấy dữ liệu tổng hợp cho trang cấu hình & preview bảng xếp hạng vinh danh
        /// </summary>
        Task<LeaderboardViewModel> GetLeaderboardViewModelAsync(string streamerSlug, string period, string baseUrl);

        /// <summary>
        /// Lấy danh sách Top Donate theo chu kỳ: day (ngày), week (tuần), month (tháng), all (toàn thời gian)
        /// </summary>
        Task<List<LeaderboardDonorDto>> GetLeaderboardDonorsAsync(string streamerSlug, string period, int limit);

        /// <summary>
        /// Lấy danh sách các khoản quyên góp và tin nhắn donate mới nhất
        /// </summary>
        Task<List<RecentDonationDto>> GetRecentDonationsAsync(string streamerSlug, int limit);

        // --------------------------------------------------------------------
        // 4. KÍCH HOẠT THỬ NGHIỆM VÀ POLLING THỜI GIAN THỰC OBS
        // --------------------------------------------------------------------

        /// <summary>
        /// Kích hoạt một thông báo Alert thử nghiệm lên OBS Studio
        /// </summary>
        Task TriggerTestAlertAsync(string streamerSlug);

        /// <summary>
        /// Polling kiểm tra thông báo donate mới hoặc thông báo test cho OBS Alert Box
        /// </summary>
        Task<(bool hasAlert, AlertPollResultDto? alert)> PollAlertAsync(string streamerSlug, int lastDonationId);
    }
}
