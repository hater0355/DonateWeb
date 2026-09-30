using Microsoft.EntityFrameworkCore;
using DonateWeb.Areas.Admin.ViewModels;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Services;

namespace DonateWeb.Areas.Admin.Services
{
    /// <summary>
    /// Lớp xử lý nghiệp vụ Quản trị (Admin Service).
    /// Triển khai các thuật toán giám sát dòng tiền, quản lý trạng thái streamer
    /// và kiểm tra tra cứu log giao dịch / giải quyết khiếu nại.
    /// </summary>
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AdminService> _logger;
        private readonly IAccountIdService _accountIdService;

        public AdminService(AppDbContext context, ILogger<AdminService> logger, IAccountIdService accountIdService)
        {
            _context = context;
            _logger = logger;
            _accountIdService = accountIdService;
        }

        // ====================================================================
        // 1. GIÁM SÁT TỔNG DÒNG TIỀN RA / VÀO HỆ THỐNG
        // ====================================================================

        public async Task<AdminDashboardViewModel> GetCashFlowDashboardAsync()
        {
            // 1.1. Lấy dữ liệu các giao dịch thành công và hoàn tiền
            var successfulDonations = await _context.Donations
                .Where(d => d.Status == DonationStatus.Success)
                .ToListAsync();

            var refundedDonations = await _context.Donations
                .Where(d => d.Status == DonationStatus.Refunded)
                .ToListAsync();

            // Tổng số tiền donate thành công trên toàn hệ thống
            var totalDonationsSuccessAmount = successfulDonations.Sum(d => d.Amount);

            // Tổng số tiền hoàn trả cho người dùng do khiếu nại/lỗi
            var totalRefundedAmount = refundedDonations.Sum(d => d.Amount);

            // Tổng số dư ví của tất cả người dùng hiện có trong hệ thống (tiền lưu ký)
            var totalUserWalletBalance = await _context.Users.SumAsync(u => u.WalletBalance);

            // TỔNG DÒNG TIỀN VÀO (INFLOW): Tổng tiền donate thành công trên hệ thống
            var totalInflow = totalDonationsSuccessAmount;

            // TỔNG DÒNG TIỀN RA (OUTFLOW): 95% chuyển giao cho Streamer nhận + Tiền hoàn trả khiếu nại
            var totalOutflow = (totalDonationsSuccessAmount * 0.95m) + totalRefundedAmount;

            // DOANH THU PHÍ SÀN (PLATFORM COMMISSION): 5% từ các giao dịch donate thành công
            var platformRevenue = totalDonationsSuccessAmount * 0.05m;

            // 1.2. Thống kê số lượng theo trạng thái
            var totalSuccessCount = successfulDonations.Count;
            var totalPendingCount = await _context.Donations.CountAsync(d => d.Status == DonationStatus.Pending);
            var totalDisputedCount = await _context.Donations.CountAsync(d => d.IsDisputed && d.Status == DonationStatus.Disputed);

            var suspendedStreamersCount = await _context.StreamerProfiles.CountAsync(s => !s.IsActive);
            var activeStreamersCount = await _context.StreamerProfiles.CountAsync(s => s.IsActive && s.ApprovalStatus == StreamerApprovalStatus.Approved);
            var pendingStreamersCount = await _context.StreamerProfiles.CountAsync(s => s.ApprovalStatus == StreamerApprovalStatus.Pending);

            // 1.3. Chuẩn bị dữ liệu vẽ biểu đồ dòng tiền 7 ngày gần nhất (Chart.js)
            var chartLabels = new List<string>();
            var chartInflow = new List<decimal>();
            var chartOutflow = new List<decimal>();

            var now = DateTime.UtcNow.Date;
            for (int i = 6; i >= 0; i--)
            {
                var targetDate = now.AddDays(-i);
                var nextDate = targetDate.AddDays(1);

                chartLabels.Add(targetDate.ToString("dd/MM"));

                // Tiền vào của ngày (donate thành công trong ngày)
                var dayInflow = successfulDonations
                    .Where(d => d.CreatedAt >= targetDate && d.CreatedAt < nextDate)
                    .Sum(d => d.Amount);

                // Tiền ra của ngày (95% streamer + tiền hoàn trả trong ngày)
                var dayRefunds = refundedDonations
                    .Where(d => d.ResolvedAt.HasValue && d.ResolvedAt.Value >= targetDate && d.ResolvedAt.Value < nextDate)
                    .Sum(d => d.Amount);

                var dayOutflow = (dayInflow * 0.95m) + dayRefunds;

                chartInflow.Add(dayInflow);
                chartOutflow.Add(dayOutflow);
            }

            // 1.4. Top 5 Streamer nhận donate nhiều nhất (chỉ hiển thị khi đã có dữ liệu donate)
            var topStreamers = await _context.StreamerProfiles
                .Where(s => s.TotalReceived > 0)
                .OrderByDescending(s => s.TotalReceived)
                .Take(5)
                .Select(s => new TopStreamerSummaryItem
                {
                    Id = s.Id,
                    DisplayName = s.DisplayName,
                    Slug = s.Slug,
                    AvatarUrl = s.AvatarUrl,
                    TotalReceived = s.TotalReceived,
                    FollowerCount = s.FollowerCount,
                    IsActive = s.IsActive,
                    IsVerified = s.IsVerified
                })
                .ToListAsync();

            // 1.5. Top 5 Donors quyên góp nhiều nhất
            var topDonors = successfulDonations
                .GroupBy(d => d.DonorName)
                .Select(g => new TopDonorSummaryItem
                {
                    DonorName = g.Key,
                    TotalDonated = g.Sum(x => x.Amount),
                    DonationCount = g.Count()
                })
                .OrderByDescending(x => x.TotalDonated)
                .Take(5)
                .ToList();

            // 1.6. 5 Audit Logs kiểm tra giao dịch mới nhất
            var recentLogs = await _context.TransactionAuditLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(5)
                .Select(l => new RecentAuditLogItem
                {
                    Id = l.Id,
                    TransactionCode = l.TransactionCode,
                    ActionType = l.ActionType,
                    Note = l.Note,
                    PerformedBy = l.PerformedBy,
                    CreatedAt = l.CreatedAt
                })
                .ToListAsync();

            return new AdminDashboardViewModel
            {
                TotalInflow = totalInflow,
                TotalOutflow = totalOutflow,
                PlatformBalance = totalUserWalletBalance,
                PlatformRevenue = platformRevenue,
                TotalSuccessfulTransactions = totalSuccessCount,
                TotalPendingTransactions = totalPendingCount,
                TotalDisputedTransactions = totalDisputedCount,
                SuspendedStreamersCount = suspendedStreamersCount,
                ActiveStreamersCount = activeStreamersCount,
                PendingStreamersCount = pendingStreamersCount,
                ChartLabels = chartLabels,
                ChartInflowData = chartInflow,
                ChartOutflowData = chartOutflow,
                TopStreamers = topStreamers,
                TopDonors = topDonors,
                RecentLogs = recentLogs
            };
        }


        // ====================================================================
        // 2. QUẢN LÝ TRẠNG THÁI STREAMER (KHÓA / KÍCH HOẠT KÊNH)
        // ====================================================================

        public async Task<AdminStreamerListViewModel> GetStreamersAsync(string? searchTerm, string statusFilter, string verificationFilter)
        {
            var query = _context.StreamerProfiles
                .Include(s => s.User)
                .AsQueryable();

            // Lọc theo từ khóa tìm kiếm (Tên kênh, slug, họ tên hoặc username chủ kênh)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(s => s.DisplayName.ToLower().Contains(term) ||
                                         s.Slug.ToLower().Contains(term) ||
                                         s.User.Username.ToLower().Contains(term) ||
                                         s.User.FullName.ToLower().Contains(term) ||
                                         (s.User.AccountId != null && s.User.AccountId.Contains(term)));
            }

            // Lọc theo trạng thái duyệt / hoạt động: "pending", "active", "locked", "rejected"
            if (statusFilter == "pending")
            {
                query = query.Where(s => s.ApprovalStatus == StreamerApprovalStatus.Pending);
            }
            else if (statusFilter == "active" || statusFilter == "approved")
            {
                query = query.Where(s => s.ApprovalStatus == StreamerApprovalStatus.Approved && s.IsActive);
            }
            else if (statusFilter == "locked")
            {
                query = query.Where(s => !s.IsActive);
            }
            else if (statusFilter == "rejected")
            {
                query = query.Where(s => s.ApprovalStatus == StreamerApprovalStatus.Rejected);
            }

            // Lọc theo tích xanh xác minh: "verified", "unverified"
            if (verificationFilter == "verified")
            {
                query = query.Where(s => s.IsVerified);
            }
            else if (verificationFilter == "unverified")
            {
                query = query.Where(s => !s.IsVerified);
            }

            var streamers = await query
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new StreamerManagementItem
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    AccountId = s.User.AccountId,
                    Username = s.User.Username,
                    OwnerFullName = s.User.FullName,
                    OwnerEmail = s.User.Email,
                    DisplayName = s.DisplayName,
                    Slug = s.Slug,
                    AvatarUrl = s.AvatarUrl,
                    MinDonateAmount = s.MinDonateAmount,
                    TotalReceived = s.TotalReceived,
                    FollowerCount = s.FollowerCount,
                    IsActive = s.IsActive,
                    ApprovalStatus = s.ApprovalStatus,
                    RejectionReason = s.RejectionReason,
                    ApprovedAt = s.ApprovedAt,
                    LockReason = s.LockReason,
                    LockedAt = s.LockedAt,
                    IsVerified = s.IsVerified,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            var totalCount = await _context.StreamerProfiles.CountAsync();
            var activeCount = await _context.StreamerProfiles.CountAsync(s => s.ApprovalStatus == StreamerApprovalStatus.Approved && s.IsActive);
            var lockedCount = await _context.StreamerProfiles.CountAsync(s => !s.IsActive);
            var pendingCount = await _context.StreamerProfiles.CountAsync(s => s.ApprovalStatus == StreamerApprovalStatus.Pending);
            var rejectedCount = await _context.StreamerProfiles.CountAsync(s => s.ApprovalStatus == StreamerApprovalStatus.Rejected);

            return new AdminStreamerListViewModel
            {
                Streamers = streamers,
                SearchTerm = searchTerm,
                StatusFilter = statusFilter,
                VerificationFilter = verificationFilter,
                TotalStreamers = totalCount,
                ActiveCount = activeCount,
                LockedCount = lockedCount,
                PendingCount = pendingCount,
                RejectedCount = rejectedCount
            };
        }

        public async Task<(bool Success, string Message)> ApproveStreamerAsync(int streamerId, string adminUsername)
        {
            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                    .ThenInclude(u => u.UserRoles)
                .FirstOrDefaultAsync(s => s.Id == streamerId);

            if (streamer == null)
            {
                return (false, "Không tìm thấy thông tin Streamer cần duyệt.");
            }

            // Cập nhật trạng thái duyệt và kích hoạt
            streamer.ApprovalStatus = StreamerApprovalStatus.Approved;
            streamer.ApprovedAt = DateTime.UtcNow;
            streamer.IsActive = true;
            streamer.RejectionReason = null;
            streamer.UpdatedAt = DateTime.UtcNow;

            var user = streamer.User;
            if (user != null)
            {
                // 1. Gán vai trò Streamer
                var streamerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Streamer);
                if (streamerRole == null)
                {
                    streamerRole = new Role { Name = UserRoles.Streamer, Description = "Nhà sáng tạo nội dung / Streamer nhận donate" };
                    _context.Roles.Add(streamerRole);
                    await _context.SaveChangesAsync();
                }

                if (!user.UserRoles.Any(ur => ur.RoleId == streamerRole.Id))
                {
                    _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = streamerRole.Id });
                }

                // 2. Nâng cấp mã ID 10 số của tài khoản: Chuyển đổi số đầu tiên từ 9 thành 1
                user.AccountId = await _accountIdService.UpgradeToStreamerIdAsync(user.AccountId, user.CreatedAt.Year);
                user.UpdatedAt = DateTime.UtcNow;

                // 3. Cập nhật phần tên miền trang cá nhân (Slug) của streamer thành mã ID của streamer đó
                if (!string.IsNullOrWhiteSpace(user.AccountId))
                {
                    streamer.Slug = user.AccountId;
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin {Admin} đã phê duyệt hồ sơ streamer {DisplayName} (Tên miền ID: {Slug}, Mã ID: {AccountId})", adminUsername, streamer.DisplayName, streamer.Slug, user?.AccountId);

            return (true, $"Đã phê duyệt hồ sơ Streamer '{streamer.DisplayName}' (Tên miền: /{streamer.Slug}) thành công! Đã cấp quyền Streamer và đổi mã ID thành {user?.AccountId}.");
        }

        public async Task<(bool Success, string Message)> RejectStreamerAsync(int streamerId, string reason, string adminUsername)
        {
            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == streamerId);

            if (streamer == null)
            {
                return (false, "Không tìm thấy thông tin Streamer.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Hồ sơ đăng ký chưa đáp ứng điều kiện và tiêu chuẩn của nền tảng.";
            }

            streamer.ApprovalStatus = StreamerApprovalStatus.Rejected;
            streamer.RejectionReason = reason.Trim();
            streamer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogWarning("Admin {Admin} đã từ chối hồ sơ streamer {Slug}. Lý do: {Reason}", adminUsername, streamer.Slug, reason);

            return (true, $"Đã từ chối đơn đăng ký Streamer '{streamer.DisplayName}' (@{streamer.Slug}).");
        }

        public async Task<(bool Success, string Message)> LockStreamerAsync(int streamerId, string reason, string adminUsername)
        {
            var streamer = await _context.StreamerProfiles.FindAsync(streamerId);
            if (streamer == null)
            {
                return (false, "Không tìm thấy thông tin Streamer cần khóa.");
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Vi phạm chính sách cộng đồng và tiêu chuẩn nội dung của nền tảng.";
            }

            streamer.IsActive = false;
            streamer.LockReason = reason.Trim();
            streamer.LockedAt = DateTime.UtcNow;
            streamer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogWarning("Admin {Admin} đã khóa kênh streamer {Slug}. Lý do: {Reason}", adminUsername, streamer.Slug, reason);

            return (true, $"Đã khóa kênh Streamer '{streamer.DisplayName}' (@{streamer.Slug}) thành công.");
        }

        public async Task<(bool Success, string Message)> UnlockStreamerAsync(int streamerId, string adminUsername)
        {
            var streamer = await _context.StreamerProfiles.FindAsync(streamerId);
            if (streamer == null)
            {
                return (false, "Không tìm thấy thông tin Streamer cần kích hoạt.");
            }

            streamer.IsActive = true;
            streamer.LockReason = null;
            streamer.LockedAt = null;
            streamer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Admin {Admin} đã kích hoạt lại kênh streamer {Slug}.", adminUsername, streamer.Slug);

            return (true, $"Kênh Streamer '{streamer.DisplayName}' (@{streamer.Slug}) đã được kích hoạt hoạt động trở lại.");
        }

        public async Task<(bool Success, string Message)> ToggleVerifyStreamerAsync(int streamerId, string adminUsername)
        {
            var streamer = await _context.StreamerProfiles.FindAsync(streamerId);
            if (streamer == null)
            {
                return (false, "Không tìm thấy thông tin Streamer.");
            }

            streamer.IsVerified = !streamer.IsVerified;
            streamer.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var statusMsg = streamer.IsVerified ? "Cấp huy hiệu xác minh (tích xanh) thành công" : "Đã gỡ huy hiệu xác minh";
            return (true, $"{statusMsg} cho streamer '{streamer.DisplayName}'.");
        }

        public async Task<(bool Success, string Message)> DeleteStreamerAsync(int streamerId, string adminUsername, bool deleteUserAccount = false)
        {
            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(s => s.Id == streamerId);

            if (streamer == null)
            {
                return (false, "Không tìm thấy hồ sơ Streamer cần xóa.");
            }

            var user = streamer.User;
            var streamerName = streamer.DisplayName;
            var streamerSlug = streamer.Slug;

            if (user != null && user.Username.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Không thể xóa hồ sơ của tài khoản Quản trị viên hệ thống.");
            }

            // 1. Dọn dẹp Widgets & Goals
            var alertBoxes = await _context.AlertBoxConfigs
                .Where(abc => abc.StreamerProfileId == streamer.Id)
                .ToListAsync();
            if (alertBoxes.Any())
            {
                _context.AlertBoxConfigs.RemoveRange(alertBoxes);
            }

            var goals = await _context.StreamerGoals
                .Where(sg => sg.StreamerProfileId == streamer.Id)
                .ToListAsync();
            if (goals.Any())
            {
                _context.StreamerGoals.RemoveRange(goals);
            }

            // 2. Gán null StreamerProfileId trong WithdrawalRequests để giữ lịch sử rút tiền
            var withdrawalRequests = await _context.WithdrawalRequests
                .Where(wr => wr.StreamerProfileId == streamer.Id)
                .ToListAsync();
            foreach (var wr in withdrawalRequests)
            {
                wr.StreamerProfileId = null;
            }

            // 3. Dọn dẹp Donations và AuditLogs nhận được của streamer này để tránh vi phạm FK Restrict
            var donations = await _context.Donations
                .Where(d => d.StreamerProfileId == streamer.Id)
                .ToListAsync();
            if (donations.Any())
            {
                var donationIds = donations.Select(d => d.Id).ToList();
                var auditLogs = await _context.TransactionAuditLogs
                    .Where(al => donationIds.Contains(al.DonationId))
                    .ToListAsync();
                _context.TransactionAuditLogs.RemoveRange(auditLogs);
                _context.Donations.RemoveRange(donations);
            }

            // 4. Xóa StreamerProfile
            _context.StreamerProfiles.Remove(streamer);

            if (user != null)
            {
                if (deleteUserAccount)
                {
                    // Xóa hoàn toàn User và toàn bộ dữ liệu phụ thuộc
                    var userDonationsSent = await _context.Donations.Where(d => d.DonorUserId == user.Id).ToListAsync();
                    foreach (var d in userDonationsSent) { d.DonorUserId = null; }

                    var userWithdrawals = await _context.WithdrawalRequests.Where(wr => wr.UserId == user.Id).ToListAsync();
                    _context.WithdrawalRequests.RemoveRange(userWithdrawals);

                    var userWalletTx = await _context.WalletTransactions.Where(wt => wt.UserId == user.Id).ToListAsync();
                    _context.WalletTransactions.RemoveRange(userWalletTx);

                    var externalLogins = await _context.ExternalLogins.Where(el => el.UserId == user.Id).ToListAsync();
                    _context.ExternalLogins.RemoveRange(externalLogins);

                    _context.Users.Remove(user);

                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Admin {Admin} đã xóa vĩnh viễn Streamer {Name} (/{Slug}) và toàn bộ tài khoản {User}.", adminUsername, streamerName, streamerSlug, user.Username);
                    return (true, $"Đã xóa vĩnh viễn kênh Streamer '{streamerName}' (/{streamerSlug}) cùng tài khoản người dùng '{user.Username}' khỏi hệ thống.");
                }
                else
                {
                    // Chuyển vai trò sang Viewer
                    var streamerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Streamer);
                    if (streamerRole != null)
                    {
                        var userStreamerRole = user.UserRoles.FirstOrDefault(ur => ur.RoleId == streamerRole.Id);
                        if (userStreamerRole != null)
                        {
                            _context.UserRoles.Remove(userStreamerRole);
                        }
                    }

                    var viewerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Viewer);
                    if (viewerRole == null)
                    {
                        viewerRole = new Role { Name = UserRoles.Viewer, Description = "Người xem, ủng hộ và nạp ví" };
                        _context.Roles.Add(viewerRole);
                        await _context.SaveChangesAsync();
                    }

                    if (!user.UserRoles.Any(ur => ur.RoleId == viewerRole.Id))
                    {
                        _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = viewerRole.Id });
                    }

                    // Chuyển đổi mã AccountId từ 1 sang 9
                    user.AccountId = await _accountIdService.DowngradeToViewerIdAsync(user.AccountId, user.CreatedAt.Year);
                }
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin {Admin} đã xóa kênh Streamer {Name} (/{Slug}). Tài khoản được chuyển về Viewer.", adminUsername, streamerName, streamerSlug);
            return (true, $"Đã xóa kênh Streamer '{streamerName}' (/{streamerSlug}) thành công. Tài khoản đã được chuyển về vai trò Viewer với mã ID: {user?.AccountId}.");
        }


        // ====================================================================
        // 3. TRA CỨU LOG GIAO DỊCH & XỬ LÝ KHIẾU NẠI (DISPUTE)
        // ====================================================================

        public async Task<AdminTransactionListViewModel> GetTransactionsAsync(
            string? search,
            int? status,
            int? paymentMethod,
            bool? isDisputed,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1,
            int pageSize = 15)
        {
            var query = _context.Donations
                .Include(d => d.StreamerProfile)
                .Include(d => d.DonorUser)
                .Include(d => d.AuditLogs)
                .AsQueryable();

            // Tìm kiếm theo Mã GD, Tên người gửi hoặc Tên Streamer
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(d => (d.TransactionCode != null && d.TransactionCode.ToLower().Contains(s)) ||
                                         d.DonorName.ToLower().Contains(s) ||
                                         d.StreamerProfile.DisplayName.ToLower().Contains(s) ||
                                         d.StreamerProfile.Slug.ToLower().Contains(s));
            }

            // Lọc theo trạng thái
            if (status.HasValue)
            {
                query = query.Where(d => (int)d.Status == status.Value);
            }

            // Lọc theo phương thức thanh toán
            if (paymentMethod.HasValue)
            {
                query = query.Where(d => (int)d.PaymentMethod == paymentMethod.Value);
            }

            // Lọc riêng các giao dịch có khiếu nại (Dispute)
            if (isDisputed.HasValue)
            {
                query = query.Where(d => d.IsDisputed == isDisputed.Value);
            }

            // Lọc theo khoảng ngày
            if (fromDate.HasValue)
            {
                query = query.Where(d => d.CreatedAt >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(d => d.CreatedAt <= endOfDay);
            }

            var totalRecords = await query.CountAsync();
            var totalFilteredAmount = await query.SumAsync(d => d.Amount);

            var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var items = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new TransactionListItem
                {
                    Id = d.Id,
                    TransactionCode = d.TransactionCode ?? $"DON_{d.Id}",
                    StreamerDisplayName = d.StreamerProfile.DisplayName,
                    StreamerSlug = d.StreamerProfile.Slug,
                    DonorName = d.DonorName,
                    Amount = d.Amount,
                    Message = d.Message,
                    PaymentMethod = d.PaymentMethod,
                    Status = d.Status,
                    IsDisputed = d.IsDisputed,
                    DisputeReason = d.DisputeReason,
                    AdminNote = d.AdminNote,
                    AuditLogCount = d.AuditLogs.Count,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync();

            return new AdminTransactionListViewModel
            {
                Transactions = items,
                SearchCodeOrDonor = search,
                StatusFilter = status,
                PaymentMethodFilter = paymentMethod,
                IsDisputedFilter = isDisputed,
                FromDate = fromDate,
                ToDate = toDate,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalRecords = totalRecords,
                FilteredTotalAmount = totalFilteredAmount
            };
        }

        public async Task<AdminTransactionDetailViewModel?> GetTransactionDetailAsync(int donationId)
        {
            var d = await _context.Donations
                .Include(x => x.StreamerProfile)
                .Include(x => x.DonorUser)
                .Include(x => x.AuditLogs)
                .FirstOrDefaultAsync(x => x.Id == donationId);

            if (d == null) return null;

            return new AdminTransactionDetailViewModel
            {
                Id = d.Id,
                TransactionCode = d.TransactionCode ?? $"DON_{d.Id}",
                Amount = d.Amount,
                Status = d.Status,
                PaymentMethod = d.PaymentMethod,
                Message = d.Message,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt,
                IsDisputed = d.IsDisputed,
                DisputeReason = d.DisputeReason,
                AdminNote = d.AdminNote,
                StreamerId = d.StreamerProfileId,
                StreamerName = d.StreamerProfile.DisplayName,
                StreamerSlug = d.StreamerProfile.Slug,
                StreamerBankName = d.StreamerProfile.BankName,
                StreamerAccountNumber = d.StreamerProfile.BankAccountNumber,
                StreamerAccountName = d.StreamerProfile.BankAccountName,
                DonorUserId = d.DonorUserId,
                DonorName = d.DonorName,
                DonorEmail = d.DonorUser?.Email,
                DonorWalletBalance = d.DonorUser?.WalletBalance,
                AuditLogs = d.AuditLogs
                    .OrderByDescending(l => l.CreatedAt)
                    .Select(l => new AuditLogItemViewModel
                    {
                        Id = l.Id,
                        ActionType = l.ActionType,
                        OldStatus = l.OldStatus,
                        NewStatus = l.NewStatus,
                        Note = l.Note,
                        PerformedBy = l.PerformedBy,
                        CreatedAt = l.CreatedAt
                    })
                    .ToList()
            };
        }

        public async Task<(bool Success, string Message)> ResolveDisputeOrChangeStatusAsync(
            int donationId,
            DonationStatus newStatus,
            string actionType,
            string note,
            string adminUsername)
        {
            var donation = await _context.Donations
                .Include(d => d.DonorUser)
                .Include(d => d.StreamerProfile)
                .FirstOrDefaultAsync(d => d.Id == donationId);

            if (donation == null)
            {
                return (false, "Không tìm thấy thông tin giao dịch cần xử lý.");
            }

            var oldStatus = donation.Status;
            var txCode = donation.TransactionCode ?? $"DON_{donation.Id}";

            // 1. Trường hợp HOÀN TIỀN (Refunded):
            if (newStatus == DonationStatus.Refunded)
            {
                // Hoàn lại tiền vào ví người dùng nếu có tài khoản nội bộ
                if (donation.DonorUser != null)
                {
                    donation.DonorUser.WalletBalance += donation.Amount;
                    _logger.LogInformation("Đã hoàn {Amount} VNĐ vào ví cho user {Username}", donation.Amount, donation.DonorUser.Username);
                }

                // Trừ lại số tiền streamer đã nhận nếu trước đó là thành công
                if (oldStatus == DonationStatus.Success && donation.StreamerProfile != null)
                {
                    donation.StreamerProfile.TotalReceived = Math.Max(0, donation.StreamerProfile.TotalReceived - donation.Amount);
                }

                donation.IsDisputed = false;
                donation.ResolvedAt = DateTime.UtcNow;
            }
            // 2. Trường hợp XÁC NHẬN THÀNH CÔNG (Success):
            else if (newStatus == DonationStatus.Success)
            {
                if (oldStatus != DonationStatus.Success && donation.StreamerProfile != null)
                {
                    donation.StreamerProfile.TotalReceived += donation.Amount;
                }
                donation.IsDisputed = false;
                donation.ResolvedAt = DateTime.UtcNow;
            }
            // 3. Trường hợp BÁC BỎ KHIẾU NẠI (Giữ nguyên trạng thái, đóng dispute):
            else if (actionType == "REJECT_DISPUTE")
            {
                donation.IsDisputed = false;
                donation.ResolvedAt = DateTime.UtcNow;
            }
            // 4. Trường hợp mở khiếu nại mới (Dispute):
            else if (newStatus == DonationStatus.Disputed)
            {
                donation.IsDisputed = true;
                donation.ResolvedAt = null;
            }

            donation.Status = newStatus;
            if (!string.IsNullOrWhiteSpace(note))
            {
                donation.AdminNote = note.Trim();
            }

            // Ghi nhận dòng nhật ký kiểm tra (TransactionAuditLog)
            var auditLog = new TransactionAuditLog
            {
                DonationId = donation.Id,
                TransactionCode = txCode,
                ActionType = actionType,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                Note = note?.Trim(),
                PerformedBy = adminUsername,
                CreatedAt = DateTime.UtcNow
            };

            _context.TransactionAuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Admin {Admin} đã xử lý giao dịch {Code}. Thao tác: {Action}, Từ {Old} sang {New}",
                adminUsername, txCode, actionType, oldStatus, newStatus);

            return (true, $"Xử lý giao dịch '{txCode}' thành công. Đã cập nhật trạng thái và ghi nhật ký kiểm tra.");
        }
    }
}
