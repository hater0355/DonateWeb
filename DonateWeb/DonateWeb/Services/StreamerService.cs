using Microsoft.EntityFrameworkCore;
using System.Data;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;
using DonateWeb.ViewModels.Streamer;

namespace DonateWeb.Services
{
    public class StreamerService : IStreamerService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IContentModerationService _moderationService;
        private readonly IAccountIdService _accountIdService;

        public StreamerService(
            AppDbContext context,
            IPasswordHasher passwordHasher,
            IContentModerationService moderationService,
            IAccountIdService accountIdService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _moderationService = moderationService;
            _accountIdService = accountIdService;
        }

        public async Task<StreamerProfile?> GetBySlugAsync(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;

            var normalizedSlug = slug.Trim().ToLower();

            // 1. Tìm theo Slug chính xác (mã ID của streamer)
            var profile = await _context.StreamerProfiles
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.Slug.ToLower() == normalizedSlug);

            if (profile != null) return profile;

            // 2. Tìm theo User.AccountId (Mã ID 10 số của Streamer)
            profile = await _context.StreamerProfiles
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.User != null && sp.User.AccountId != null && sp.User.AccountId.ToLower() == normalizedSlug);

            if (profile != null) return profile;

            // 3. Tìm theo StreamerProfile.Id hoặc User.Id (nếu truyền vào là số nguyên)
            if (int.TryParse(normalizedSlug, out var numericId))
            {
                profile = await _context.StreamerProfiles
                    .Include(sp => sp.User)
                    .FirstOrDefaultAsync(sp => sp.Id == numericId || sp.UserId == numericId);

                if (profile != null) return profile;
            }

            // 4. Fallback tìm theo User.Username
            return await _context.StreamerProfiles
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.User != null && sp.User.Username.ToLower() == normalizedSlug);
        }

        public async Task<StreamerProfile?> GetByUserIdAsync(int userId)
        {
            return await _context.StreamerProfiles
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.UserId == userId);
        }

        public async Task<List<StreamerProfile>> GetFeaturedStreamersAsync(int count = 8)
        {
            return await _context.StreamerProfiles
                .Include(sp => sp.User)
                .OrderByDescending(sp => sp.TotalReceived)
                .ThenByDescending(sp => sp.FollowerCount)
                .Take(count)
                .ToListAsync();
        }

        public async Task<(bool Success, string Error)> UpdateProfileConfigAsync(int userId, StreamerProfileConfigViewModel model)
        {
            var profile = await _context.StreamerProfiles
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.UserId == userId);
            if (profile == null)
            {
                return (false, "Không tìm thấy hồ sơ Streamer cho tài khoản này.");
            }

            var newSlug = model.Slug.Trim().ToLower();
            // Kiểm tra slug có bị trùng với streamer khác không
            if (await _context.StreamerProfiles.AnyAsync(sp => sp.Id != profile.Id && sp.Slug.ToLower() == newSlug))
            {
                return (false, $"Slug cá nhân '{newSlug}' đã được sử dụng bởi người khác. Vui lòng chọn slug khác.");
            }

            // Cập nhật thông tin cấu hình trang nhận donate
            profile.Slug = newSlug;
            profile.DisplayName = model.DisplayName.Trim();
            profile.GreetingMessage = model.GreetingMessage.Trim();
            profile.Bio = model.Bio; // Lưu nội dung HTML từ rich text editor (Bio / About Me)
            if (!string.IsNullOrWhiteSpace(model.AvatarUrl))
            {
                profile.AvatarUrl = model.AvatarUrl.Trim();
                if (profile.User != null)
                {
                    profile.User.AvatarUrl = profile.AvatarUrl;
                }
            }
            profile.BannerUrl = string.IsNullOrWhiteSpace(model.BannerUrl) ? profile.BannerUrl : model.BannerUrl.Trim();
            profile.MinDonateAmount = model.MinDonateAmount;

            // Cập nhật thông tin thanh toán & ngân hàng
            profile.BankName = model.BankName?.Trim();
            profile.BankAccountNumber = model.BankAccountNumber?.Trim();
            profile.BankAccountName = model.BankAccountName?.Trim();
            profile.PaymentQrUrl = model.PaymentQrUrl?.Trim();

            // Cập nhật mạng xã hội
            profile.YoutubeUrl = model.YoutubeUrl?.Trim();
            profile.TwitchUrl = model.TwitchUrl?.Trim();
            profile.DiscordUrl = model.DiscordUrl?.Trim();
            profile.FacebookUrl = model.FacebookUrl?.Trim();
            profile.TiktokUrl = model.TiktokUrl?.Trim();

            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return (true, string.Empty);
        }

        public async Task<(bool Success, string Error, Donation? Donation)> ProcessDonationAsync(int? donorUserId, StreamerDonateViewModel model)
        {
            var profile = await _context.StreamerProfiles.FindAsync(model.StreamerProfileId);
            if (profile == null)
            {
                return (false, "Không tìm thấy Streamer để thực hiện Donate.", null);
            }

            if (model.Amount < profile.MinDonateAmount)
            {
                return (false, $"Số tiền Donate tối thiểu cho kênh của {profile.DisplayName} là {profile.MinDonateAmount:N0} VNĐ.", null);
            }

            if (model.Amount < 1000m || model.Amount > 100000000m || model.Amount != decimal.Truncate(model.Amount))
            {
                return (false, "Số tiền Donate phải là số nguyên từ 1.000 đến 100.000.000 VNĐ.", null);
            }

            // =================================================================================
            // [MỚI THÊM / CHỈNH SỬA] Hoàn thiện logic xử lý chỉ 2 phương thức thanh toán cho Viewer:
            // 1. PaymentMethod.Wallet (1): Donate từ số dư có sẵn trong tài khoản
            // 2. PaymentMethod.BankTransfer (2): Chuyển khoản ngân hàng (Quét mã QR)
            // =================================================================================
            if (model.PaymentMethod != PaymentMethod.Wallet && model.PaymentMethod != PaymentMethod.BankTransfer)
            {
                return (false, "Phương thức thanh toán không hợp lệ. Chỉ hỗ trợ Chuyển khoản ngân hàng hoặc Donate từ số dư tài khoản.", null);
            }

            // Bank transfers must be created as pending orders and confirmed by a verified webhook.
            if (model.PaymentMethod == PaymentMethod.BankTransfer)
            {
                return (false, "Chuyển khoản ngân hàng cần được xác nhận qua đơn thanh toán. Vui lòng tạo đơn QR để tiếp tục.", null);
            }

            // Phương thức 1: Donate từ số dư có sẵn trong tài khoản (PaymentMethod.Wallet)
            if (model.PaymentMethod == PaymentMethod.Wallet)
            {
                // Bắt buộc người dùng phải đăng nhập mới có số dư tài khoản để trừ tiền
                if (!donorUserId.HasValue)
                {
                    return (false, "Vui lòng đăng nhập tài khoản để sử dụng phương thức Donate từ số dư có sẵn, hoặc chọn phương thức Chuyển khoản ngân hàng (QR).", null);
                }

                var donorUser = await _context.Users.FindAsync(donorUserId.Value);
                if (donorUser == null)
                {
                    return (false, "Tài khoản người Donate không tồn tại.", null);
                }

                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                var debited = await _context.Users
                    .Where(user => user.Id == donorUser.Id && user.WalletBalance >= model.Amount)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        user => user.WalletBalance,
                        user => user.WalletBalance - model.Amount));

                await _context.Entry(donorUser).ReloadAsync();
                if (debited == 0)
                {
                    return (false, $"Số dư có sẵn trong tài khoản của bạn không đủ ({donorUser.WalletBalance:N0} VNĐ). Vui lòng nạp thêm hoặc chọn Chuyển khoản ngân hàng.", null);
                }

                var profileUpdated = await _context.StreamerProfiles
                    .Where(streamerProfile => streamerProfile.Id == profile.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        streamerProfile => streamerProfile.TotalReceived,
                        streamerProfile => streamerProfile.TotalReceived + model.Amount));
                if (profileUpdated == 0)
                {
                    return (false, "Hồ sơ streamer không còn tồn tại.", null);
                }

                await _context.Entry(profile).ReloadAsync();
                if (profile.UserId != donorUser.Id)
                {
                    var credited = await _context.Users
                        .Where(user => user.Id == profile.UserId)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            user => user.WalletBalance,
                            user => user.WalletBalance + model.Amount));

                    if (credited > 0 && _context.Users.Local.FirstOrDefault(user => user.Id == profile.UserId) is { } trackedStreamer)
                    {
                        await _context.Entry(trackedStreamer).ReloadAsync();
                    }
                }

                var savedDonation = await PersistDonationAsync(model, donorUserId, profile);
                await transaction.CommitAsync();
                return (true, string.Empty, savedDonation);
            }
            return (false, "Phương thức thanh toán không hợp lệ.", null);
        }

        private async Task<Donation> PersistDonationAsync(StreamerDonateViewModel model, int? donorUserId, StreamerProfile profile)
        {
            var moderation = _moderationService.ModerateContent(model.Message, model.DonorName);
            var safeDonorName = string.IsNullOrWhiteSpace(model.DonorName)
                ? "Người hâm mộ ẩn danh"
                : _moderationService.SanitizeForStream(model.DonorName.Trim());
            var transactionCode = $"DON_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
            var donation = new Donation
            {
                StreamerProfileId = profile.Id,
                DonorUserId = donorUserId,
                DonorName = safeDonorName,
                Amount = model.Amount,
                Message = moderation.SanitizedText,
                PaymentMethod = model.PaymentMethod,
                Status = DonationStatus.Success,
                TransactionCode = transactionCode,
                CreatedAt = DateTime.UtcNow
            };

            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();
            _context.TransactionAuditLogs.Add(new TransactionAuditLog
            {
                DonationId = donation.Id,
                TransactionCode = donation.TransactionCode,
                ActionType = "CREATE_DONATION",
                Note = $"Giao dịch Donate {donation.Amount:N0} VNĐ từ '{donation.DonorName}' cho kênh '{profile.DisplayName}' qua Ví DonateWeb.",
                PerformedBy = donation.DonorName,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return donation;
        }

        public async Task<(bool Success, string Error, StreamerProfile? Profile)> RegisterStreamerAsync(int? currentUserId, StreamerRegisterViewModel model)
        {
            var normalizedSlug = model.Slug.Trim().ToLower();

            // Kiểm tra Slug đã tồn tại chưa (loại trừ hồ sơ của chính người dùng nếu đang cập nhật lại đơn)
            if (await _context.StreamerProfiles.AnyAsync(sp => sp.Slug.ToLower() == normalizedSlug && (!currentUserId.HasValue || sp.UserId != currentUserId.Value)))
            {
                return (false, $"Slug cá nhân '{normalizedSlug}' đã được sử dụng. Vui lòng chọn slug khác.", null);
            }

            User? targetUser = null;

            if (currentUserId.HasValue)
            {
                targetUser = await _context.Users
                    .Include(u => u.UserRoles)
                    .Include(u => u.StreamerProfile)
                    .FirstOrDefaultAsync(u => u.Id == currentUserId.Value);

                if (targetUser == null)
                {
                    return (false, "Không tìm thấy thông tin tài khoản người dùng.", null);
                }

                if (targetUser.StreamerProfile != null)
                {
                    if (targetUser.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Pending)
                    {
                        return (false, "Hồ sơ đăng ký của bạn đang chờ Quản trị viên xét duyệt. Vui lòng chờ phản hồi từ hệ thống.", targetUser.StreamerProfile);
                    }
                    if (targetUser.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
                    {
                        return (false, "Tài khoản này đã là Streamer chính thức trên hệ thống.", targetUser.StreamerProfile);
                    }

                    // Nếu trước đó bị Rejected (từ chối), cho phép cập nhật lại thông tin và gửi lại yêu cầu duyệt
                    var existingProfile = targetUser.StreamerProfile;
                    existingProfile.Slug = normalizedSlug;
                    existingProfile.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? targetUser.FullName : model.DisplayName.Trim();
                    existingProfile.BankName = model.BankName.Trim();
                    existingProfile.BankAccountNumber = model.BankAccountNumber.Trim();
                    existingProfile.BankAccountName = model.BankAccountName.Trim().ToUpper();
                    existingProfile.FacebookUrl = model.FacebookUrl?.Trim();
                    existingProfile.YoutubeUrl = model.YoutubeUrl?.Trim();
                    existingProfile.TiktokUrl = model.TiktokUrl?.Trim();
                    existingProfile.ApprovalStatus = StreamerApprovalStatus.Pending;
                    existingProfile.RejectionReason = null;
                    existingProfile.UpdatedAt = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    return (true, string.Empty, existingProfile);
                }
            }
            else
            {
                // Người dùng chưa đăng nhập, cần Email & Mật khẩu
                if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
                {
                    return (false, "Vui lòng đăng nhập hoặc nhập Email và Mật khẩu để tạo tài khoản Streamer.", null);
                }

                var normalizedEmail = model.Email.Trim().ToLower();
                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
                {
                    return (false, "Email này đã được sử dụng. Vui lòng đăng nhập trước khi đăng ký Streamer.", null);
                }

                var username = normalizedSlug;
                int counter = 1;
                while (await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
                {
                    username = $"{normalizedSlug}{counter++}";
                }

                // Ban đầu tạo tài khoản với vai trò Viewer (mã ID 10 số bắt đầu bằng 9), sau khi Admin duyệt mới đổi thành 1
                var viewerAccountId = await _accountIdService.GenerateAccountIdAsync(isStreamer: false, DateTime.UtcNow.Year);

                targetUser = new User
                {
                    AccountId = viewerAccountId,
                    Username = username,
                    Email = normalizedEmail,
                    PasswordHash = _passwordHasher.HashPassword(model.Password),
                    FullName = model.FullName.Trim(),
                    PhoneNumber = model.PhoneNumber?.Trim(),
                    AvatarUrl = "https://api.dicebear.com/7.x/adventurer/svg?seed=" + Uri.EscapeDataString(model.FullName),
                    WalletBalance = 0,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(targetUser);
                await _context.SaveChangesAsync();
            }

            // Đảm bảo người dùng có vai trò Viewer trong lúc chờ Quản trị viên duyệt
            var viewerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Viewer);
            if (viewerRole == null)
            {
                viewerRole = new Role { Name = UserRoles.Viewer, Description = "Người xem, ủng hộ và nạp ví" };
                _context.Roles.Add(viewerRole);
                await _context.SaveChangesAsync();
            }

            if (!targetUser.UserRoles.Any(ur => ur.RoleId == viewerRole.Id))
            {
                _context.UserRoles.Add(new UserRole { UserId = targetUser.Id, RoleId = viewerRole.Id });
            }

            // Cập nhật thông tin User nếu có
            targetUser.FullName = model.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                targetUser.PhoneNumber = model.PhoneNumber.Trim();
            }

            // Tạo StreamerProfile với trạng thái Pending (Chờ Admin duyệt)
            var displayName = string.IsNullOrWhiteSpace(model.DisplayName) ? targetUser.FullName : model.DisplayName.Trim();
            var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=2|99|{Uri.EscapeDataString(model.BankAccountNumber)}|{Uri.EscapeDataString(model.BankAccountName)}|{Uri.EscapeDataString(model.BankName)}";

            var profile = new StreamerProfile
            {
                UserId = targetUser.Id,
                Slug = normalizedSlug,
                DisplayName = displayName,
                GreetingMessage = $"Chào mừng bạn đến với kênh donate chính thức của {displayName}! Cảm ơn sự ủng hộ của các bạn ❤️",
                AvatarUrl = targetUser.AvatarUrl,
                BannerUrl = "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=1200&auto=format&fit=crop",
                MinDonateAmount = 10000,
                BankName = model.BankName.Trim(),
                BankAccountNumber = model.BankAccountNumber.Trim(),
                BankAccountName = model.BankAccountName.Trim().ToUpper(),
                PaymentQrUrl = qrUrl,
                FacebookUrl = model.FacebookUrl?.Trim(),
                YoutubeUrl = model.YoutubeUrl?.Trim(),
                TiktokUrl = model.TiktokUrl?.Trim(),
                IsVerified = false,
                ApprovalStatus = StreamerApprovalStatus.Pending, // Chờ duyệt
                IsActive = true,
                FollowerCount = 0,
                Rank = 1,
                TotalReceived = 0,
                CreatedAt = DateTime.UtcNow
            };

            _context.StreamerProfiles.Add(profile);
            await _context.SaveChangesAsync();

            return (true, string.Empty, profile);
        }

        public async Task<(bool Success, string Message, User? User)> CancelStreamerAsync(int userId)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
            {
                return (false, "Không tìm thấy thông tin tài khoản người dùng.", null);
            }

            if (user.StreamerProfile == null)
            {
                return (false, "Tài khoản của bạn hiện không có hồ sơ Streamer để hủy.", user);
            }

            var profileId = user.StreamerProfile.Id;
            var profileName = user.StreamerProfile.DisplayName;

            // 1. Dọn dẹp Widgets & Goals
            var alertBoxes = await _context.AlertBoxConfigs
                .Where(abc => abc.StreamerProfileId == profileId)
                .ToListAsync();
            if (alertBoxes.Any())
            {
                _context.AlertBoxConfigs.RemoveRange(alertBoxes);
            }

            var goals = await _context.StreamerGoals
                .Where(sg => sg.StreamerProfileId == profileId)
                .ToListAsync();
            if (goals.Any())
            {
                _context.StreamerGoals.RemoveRange(goals);
            }

            // 2. Gán null cho StreamerProfileId trong WithdrawalRequests để giữ lịch sử rút tiền của User
            var withdrawalRequests = await _context.WithdrawalRequests
                .Where(wr => wr.StreamerProfileId == profileId)
                .ToListAsync();
            foreach (var wr in withdrawalRequests)
            {
                wr.StreamerProfileId = null;
            }

            // 3. Dọn dẹp các lượt donate và audit log nhận được của streamer này để tránh xung đột khóa ngoại
            var donationsReceived = await _context.Donations
                .Where(d => d.StreamerProfileId == profileId)
                .ToListAsync();
            if (donationsReceived.Any())
            {
                var donationIds = donationsReceived.Select(d => d.Id).ToList();
                var auditLogs = await _context.TransactionAuditLogs
                    .Where(al => donationIds.Contains(al.DonationId))
                    .ToListAsync();
                _context.TransactionAuditLogs.RemoveRange(auditLogs);
                _context.Donations.RemoveRange(donationsReceived);
            }

            // 4. Xóa hồ sơ StreamerProfile
            _context.StreamerProfiles.Remove(user.StreamerProfile);
            user.StreamerProfile = null;

            // 5. Cập nhật Roles: Gỡ quyền Streamer, thêm quyền Viewer nếu chưa có
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

            // 6. Đổi chữ số đầu tiên của mã ID từ 1 thành 9
            user.AccountId = await _accountIdService.DowngradeToViewerIdAsync(user.AccountId, user.CreatedAt.Year);

            await _context.SaveChangesAsync();

            return (true, $"Đã hủy tư cách Streamer thành công cho kênh '{profileName}'. Tài khoản của bạn đã trở về vai trò Viewer với mã ID: {user.AccountId}.", user);
        }
    }
}
