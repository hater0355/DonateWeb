using System.ComponentModel.DataAnnotations;
using DonateWeb.Models.Enums;

namespace DonateWeb.Areas.Admin.ViewModels
{
    // ====================================================================
    // 1. VIEWMODELS CHO TÍNH NĂNG: GIÁM SÁT DÒNG TIỀN RA / VÀO HỆ THỐNG
    // ====================================================================

    /// <summary>
    /// ViewModel tổng hợp toàn bộ số liệu dòng tiền, doanh thu và biểu đồ tài chính hệ thống
    /// </summary>
    public class AdminDashboardViewModel
    {
        /// <summary>Tổng dòng tiền vào hệ thống (tổng tiền các giao dịch donate thành công + tổng nạp ví)</summary>
        public decimal TotalInflow { get; set; }

        /// <summary>Tổng dòng tiền ra (tiền streamer nhận được + số tiền đã hoàn trả khiếu nại)</summary>
        public decimal TotalOutflow { get; set; }

        /// <summary>Tổng số dư ví của toàn bộ người dùng đang lưu ký trong hệ thống</summary>
        public decimal PlatformBalance { get; set; }

        /// <summary>Doanh thu phí sàn nền tảng (Ví dụ: 5% hoa hồng trên các giao dịch quyên góp)</summary>
        public decimal PlatformRevenue { get; set; }

        /// <summary>Số lượng giao dịch thành công</summary>
        public int TotalSuccessfulTransactions { get; set; }

        /// <summary>Số lượng giao dịch đang chờ xử lý</summary>
        public int TotalPendingTransactions { get; set; }

        /// <summary>Số lượng giao dịch khiếu nại đang chờ giải quyết</summary>
        public int TotalDisputedTransactions { get; set; }

        /// <summary>Số lượng streamer đang bị tạm khóa do vi phạm</summary>
        public int SuspendedStreamersCount { get; set; }

        /// <summary>Số lượng streamer đang hoạt động</summary>
        public int ActiveStreamersCount { get; set; }

        /// <summary>Số lượng đơn đăng ký streamer đang chờ Quản trị viên duyệt</summary>
        public int PendingStreamersCount { get; set; }

        // Dữ liệu phục vụ vẽ biểu đồ dòng tiền (Chart.js)
        public List<string> ChartLabels { get; set; } = new();
        public List<decimal> ChartInflowData { get; set; } = new();
        public List<decimal> ChartOutflowData { get; set; } = new();

        // Danh sách Top Streamers nhận donate nhiều nhất
        public List<TopStreamerSummaryItem> TopStreamers { get; set; } = new();

        // Danh sách Top Donors ủng hộ nhiều nhất
        public List<TopDonorSummaryItem> TopDonors { get; set; } = new();

        // 5 Nhật ký kiểm tra (Audit Logs) giao dịch mới nhất
        public List<RecentAuditLogItem> RecentLogs { get; set; } = new();
    }

    public class TopStreamerSummaryItem
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public decimal TotalReceived { get; set; }
        public int FollowerCount { get; set; }
        public bool IsActive { get; set; }
        public bool IsVerified { get; set; }
    }

    public class TopDonorSummaryItem
    {
        public string DonorName { get; set; } = string.Empty;
        public decimal TotalDonated { get; set; }
        public int DonationCount { get; set; }
    }

    public class RecentAuditLogItem
    {
        public int Id { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string? Note { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }


    // ====================================================================
    // 2. VIEWMODELS CHO TÍNH NĂNG: QUẢN LÝ TRẠNG THÁI STREAMER
    // ====================================================================

    /// <summary>
    /// ViewModel danh sách quản lý streamer kèm bộ lọc và tìm kiếm
    /// </summary>
    public class AdminStreamerListViewModel
    {
        public List<StreamerManagementItem> Streamers { get; set; } = new();
        public string? SearchTerm { get; set; }
        public string StatusFilter { get; set; } = "all"; // all, active, locked
        public string VerificationFilter { get; set; } = "all"; // all, verified, unverified

        public int TotalStreamers { get; set; }
        public int ActiveCount { get; set; }
        public int LockedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
    }

    public class StreamerManagementItem
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string? AccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string OwnerFullName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public decimal MinDonateAmount { get; set; }
        public decimal TotalReceived { get; set; }
        public int FollowerCount { get; set; }
        public bool IsActive { get; set; }
        public StreamerApprovalStatus ApprovalStatus { get; set; } = StreamerApprovalStatus.Approved;
        public string? RejectionReason { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? LockReason { get; set; }
        public DateTime? LockedAt { get; set; }
        public bool IsVerified { get; set; }
        public DateTime CreatedAt { get; set; }
    }


    // ====================================================================
    // 3. VIEWMODELS CHO TÍNH NĂNG: TRA CỨU LOG GIAO DỊCH & KHIẾU NẠI (DISPUTE)
    // ====================================================================

    /// <summary>
    /// ViewModel bộ lọc và danh sách tra cứu giao dịch
    /// </summary>
    public class AdminTransactionListViewModel
    {
        public List<TransactionListItem> Transactions { get; set; } = new();

        // Tiêu chí tìm kiếm & lọc
        public string? SearchCodeOrDonor { get; set; }
        public int? StatusFilter { get; set; }
        public int? PaymentMethodFilter { get; set; }
        public bool? IsDisputedFilter { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        // Phân trang
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalRecords { get; set; }
        public decimal FilteredTotalAmount { get; set; }
    }

    public class TransactionListItem
    {
        public int Id { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public string StreamerDisplayName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string DonorName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Message { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public DonationStatus Status { get; set; }
        public bool IsDisputed { get; set; }
        public string? DisputeReason { get; set; }
        public string? AdminNote { get; set; }
        public int AuditLogCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// ViewModel xem chi tiết giao dịch, audit log trail và form xử lý khiếu nại
    /// </summary>
    public class AdminTransactionDetailViewModel
    {
        public int Id { get; set; }
        public string TransactionCode { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DonationStatus Status { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        // Khiếu nại (Dispute)
        public bool IsDisputed { get; set; }
        public string? DisputeReason { get; set; }
        public string? AdminNote { get; set; }

        // Thông tin Streamer (Người nhận)
        public int StreamerId { get; set; }
        public string StreamerName { get; set; } = string.Empty;
        public string StreamerSlug { get; set; } = string.Empty;
        public string? StreamerBankName { get; set; }
        public string? StreamerAccountNumber { get; set; }
        public string? StreamerAccountName { get; set; }

        // Thông tin Người ủng hộ (Donor)
        public int? DonorUserId { get; set; }
        public string DonorName { get; set; } = string.Empty;
        public string? DonorEmail { get; set; }
        public decimal? DonorWalletBalance { get; set; }

        // Toàn bộ dòng thời gian nhật ký kiểm tra (Audit Log Trail)
        public List<AuditLogItemViewModel> AuditLogs { get; set; } = new();
    }

    public class AuditLogItemViewModel
    {
        public int Id { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public DonationStatus OldStatus { get; set; }
        public DonationStatus NewStatus { get; set; }
        public string? Note { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
