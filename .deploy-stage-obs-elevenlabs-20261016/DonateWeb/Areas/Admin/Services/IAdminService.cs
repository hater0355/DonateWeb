using DonateWeb.Areas.Admin.ViewModels;
using DonateWeb.Models.Enums;

namespace DonateWeb.Areas.Admin.Services
{
    /// <summary>
    /// Giao diện dịch vụ nghiệp vụ dành riêng cho Trang Quản trị (Admin Panel).
    /// Phục vụ 3 nhóm chức năng chính:
    /// 1. Giám sát tổng dòng tiền ra/vào hệ thống
    /// 2. Quản lý trạng thái streamer (khóa kênh vi phạm, kích hoạt kênh, cấp tích xanh)
    /// 3. Tra cứu và kiểm tra log giao dịch khi có khiếu nại (dispute/lỗi giao dịch)
    /// </summary>
    public interface IAdminService
    {
        // -------------------------------------------------------------
        // 1. NGHIỆP VỤ GIÁM SÁT DÒNG TIỀN RA / VÀO HỆ THỐNG
        // -------------------------------------------------------------

        /// <summary>
        /// Lấy toàn bộ số liệu báo cáo dòng tiền vào, dòng tiền ra, số dư sàn và biểu đồ 7 ngày gần nhất
        /// </summary>
        Task<AdminDashboardViewModel> GetCashFlowDashboardAsync();


        // -------------------------------------------------------------
        // 2. NGHIỆP VỤ QUẢN LÝ TRẠNG THÁI STREAMER
        // -------------------------------------------------------------

        /// <summary>
        /// Lấy danh sách streamer có áp dụng tìm kiếm theo tên/slug và lọc theo trạng thái hoạt động/xác minh
        /// </summary>
        Task<AdminStreamerListViewModel> GetStreamersAsync(string? searchTerm, string statusFilter, string verificationFilter);

        /// <summary>
        /// Phê duyệt đơn đăng ký làm Streamer (kích hoạt kênh, cấp quyền Streamer và đổi mã ID đầu 9 thành 1)
        /// </summary>
        Task<(bool Success, string Message)> ApproveStreamerAsync(int streamerId, string adminUsername);

        /// <summary>
        /// Từ chối đơn đăng ký làm Streamer kèm theo lý do từ chối
        /// </summary>
        Task<(bool Success, string Message)> RejectStreamerAsync(int streamerId, string reason, string adminUsername);

        /// <summary>
        /// Khóa kênh streamer vi phạm tiêu chuẩn cộng đồng, lưu lại lý do và thời điểm khóa
        /// </summary>
        Task<(bool Success, string Message)> LockStreamerAsync(int streamerId, string reason, string adminUsername);

        /// <summary>
        /// Kích hoạt mở khóa lại kênh streamer sau khi kiểm tra hoàn tất
        /// </summary>
        Task<(bool Success, string Message)> UnlockStreamerAsync(int streamerId, string adminUsername);

        /// <summary>
        /// Cấp hoặc thu hồi huy hiệu xác minh (tích xanh) cho kênh streamer
        /// </summary>
        Task<(bool Success, string Message)> ToggleVerifyStreamerAsync(int streamerId, string adminUsername);

        /// <summary>
        /// Xóa streamer khỏi hệ thống (xóa hồ sơ streamer, widget, goal; chuyển tài khoản về Viewer hoặc xóa toàn bộ)
        /// </summary>
        Task<(bool Success, string Message)> DeleteStreamerAsync(int streamerId, string adminUsername, bool deleteUserAccount = false);


        // -------------------------------------------------------------
        // 3. NGHIỆP VỤ TRA CỨU LOG GIAO DỊCH & XỬ LÝ KHIẾU NẠI (DISPUTE)
        // -------------------------------------------------------------

        /// <summary>
        /// Tra cứu danh sách giao dịch có phân trang và bộ lọc nâng cao (mã GD, người gửi, streamer, trạng thái, khiếu nại)
        /// </summary>
        Task<AdminTransactionListViewModel> GetTransactionsAsync(
            string? search,
            int? status,
            int? paymentMethod,
            bool? isDisputed,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 15);

        /// <summary>
        /// Xem toàn bộ thông tin chi tiết của 1 giao dịch kèm dòng thời gian nhật ký kiểm tra (Audit Logs)
        /// </summary>
        Task<AdminTransactionDetailViewModel?> GetTransactionDetailAsync(int donationId);

        /// <summary>
        /// Xử lý khiếu nại hoặc chuyển đổi trạng thái giao dịch (Hoàn tiền, Xác nhận thành công, Từ chối khiếu nại, Ghi log)
        /// </summary>
        Task<(bool Success, string Message)> ResolveDisputeOrChangeStatusAsync(
            int donationId,
            DonationStatus newStatus,
            string actionType,
            string note,
            string adminUsername);


        // -------------------------------------------------------------
        // 4. NGHIỆP VỤ QUẢN LÝ & PHÊ DUYỆT SẢN PHẨM GIAN HÀNG STREAMER
        // -------------------------------------------------------------

        /// <summary>
        /// Lấy danh sách sản phẩm do streamer đăng bán, hỗ trợ tìm kiếm và lọc theo trạng thái duyệt
        /// </summary>
        Task<AdminProductListViewModel> GetProductsAsync(
            string? searchTerm,
            string statusFilter,
            int? streamerId,
            int page = 1,
            int pageSize = 15);

        /// <summary>
        /// Phê duyệt sản phẩm của Streamer cho phép bày bán trên sàn
        /// </summary>
        Task<(bool Success, string Message)> ApproveProductAsync(int productId, string adminUsername);

        /// <summary>
        /// Từ chối sản phẩm của Streamer kèm lý do
        /// </summary>
        Task<(bool Success, string Message)> RejectProductAsync(int productId, string reason, string adminUsername);

        /// <summary>
        /// Xóa vĩnh viễn sản phẩm vi phạm
        /// </summary>
        Task<(bool Success, string Message)> DeleteProductAsync(int productId, string adminUsername);

        /// <summary>
        /// Bật / Tắt trạng thái kích hoạt của sản phẩm
        /// </summary>
        Task<(bool Success, string Message)> ToggleProductActiveAsync(int productId, string adminUsername);


        // -------------------------------------------------------------
        // 5. NGHIỆP VỤ ĐĂNG & QUẢN LÝ BẢN TIN BREAKING NEWS
        // -------------------------------------------------------------

        /// <summary>
        /// Lấy dữ liệu quản lý Breaking News (tin hiện tại và lịch sử)
        /// </summary>
        Task<BreakingNewsManageViewModel> GetBreakingNewsManageAsync();

        /// <summary>
        /// Đăng mới hoặc cập nhật Breaking News
        /// </summary>
        Task<(bool Success, string Message)> SaveBreakingNewsAsync(BreakingNewsInputModel model, string adminUsername);

        /// <summary>
        /// Bật / Tắt trạng thái hiển thị của một bản tin
        /// </summary>
        Task<(bool Success, string Message)> ToggleBreakingNewsAsync(int id, string adminUsername);

        /// <summary>
        /// Xóa bản tin khỏi hệ thống
        /// </summary>
        Task<(bool Success, string Message)> DeleteBreakingNewsAsync(int id, string adminUsername);

        /// <summary>
        /// Để trống Breaking News (tắt toàn bộ các bản tin đang phát)
        /// </summary>
        Task<(bool Success, string Message)> ClearActiveBreakingNewsAsync(string adminUsername);
    }
}
