using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.ViewModels.Auth;

namespace DonateWeb.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAccountIdService _accountIdService;

        public AuthService(AppDbContext context, IPasswordHasher passwordHasher, IAccountIdService accountIdService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _accountIdService = accountIdService;
        }

        public async Task<(bool Success, string Error, User? User, List<string> Roles)> LoginAsync(string usernameOrEmail, string password)
        {
            var normalizedInput = usernameOrEmail.Trim().ToLower();

            var user = await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedInput || u.Username.ToLower() == normalizedInput);

            if (user == null)
            {
                return (false, "Email hoặc Tên đăng nhập không tồn tại.", null, new List<string>());
            }

            if (!user.IsActive)
            {
                return (false, "Tài khoản của bạn đã bị khóa.", null, new List<string>());
            }

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                return (false, "Tài khoản này được đăng ký qua phương thức liên kết bên thứ 3 (OAuth). Vui lòng chọn đăng nhập bằng Google, Facebook hoặc YouTube.", null, new List<string>());
            }

            bool isPasswordValid = _passwordHasher.VerifyPassword(password, user.PasswordHash);
            if (!isPasswordValid)
            {
                return (false, "Mật khẩu không chính xác.", null, new List<string>());
            }

            var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
            return (true, string.Empty, user, roles);
        }

        public async Task<(bool Success, string Error, User? User)> RegisterAsync(RegisterViewModel model)
        {
            try
            {
                var normalizedUsername = model.Username.Trim().ToLower();
                var normalizedEmail = model.Email.Trim().ToLower();

                if (await _context.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername))
                {
                    return (false, "Tên đăng nhập này đã được sử dụng.", null);
                }

                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail))
                {
                    return (false, "Địa chỉ Email này đã được đăng ký.", null);
                }

                // Nếu người dùng chọn làm Streamer, kiểm tra và chuẩn hóa Slug (nếu có nhập)
                string? streamerSlug = null;
                if (model.RoleType == UserRoles.Streamer && !string.IsNullOrWhiteSpace(model.StreamerSlug))
                {
                    var rawSlug = model.StreamerSlug.Trim().ToLower().Replace("_", "-");

                    if (await _context.StreamerProfiles.AnyAsync(sp => sp.Slug.ToLower() == rawSlug))
                    {
                        return (false, $"Tên miền / Slug cá nhân '{rawSlug}' đã được streamer khác sử dụng. Vui lòng chọn tên khác.", null);
                    }
                    streamerSlug = rawSlug;
                }

                var passwordHash = _passwordHasher.HashPassword(model.Password);
                var isStreamerRegister = model.RoleType == UserRoles.Streamer;
                // Ban đầu tạo tài khoản với mã ID Viewer (bắt đầu bằng 9). Khi Admin duyệt mới nâng cấp sang 1
                var accountId = await _accountIdService.GenerateAccountIdAsync(isStreamer: false, DateTime.UtcNow.Year);

                var user = new User
                {
                    AccountId = accountId,
                    Username = model.Username.Trim(),
                    Email = model.Email.Trim(),
                    PasswordHash = passwordHash,
                    FullName = string.IsNullOrWhiteSpace(model.FullName) ? model.Username.Trim() : model.FullName.Trim(),
                    PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim(),
                    AvatarUrl = "/images/default-avatar.png",
                    WalletBalance = 0,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Luôn gán vai trò Viewer ban đầu cho tài khoản
                var viewerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Viewer);
                if (viewerRole == null)
                {
                    viewerRole = new Role { Name = UserRoles.Viewer, Description = "Người xem, ủng hộ và nạp ví" };
                    _context.Roles.Add(viewerRole);
                    await _context.SaveChangesAsync();
                }

                _context.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = viewerRole.Id
                });

                // Nếu đăng ký Streamer, tạo StreamerProfile với trạng thái Pending chờ Admin duyệt
                if (isStreamerRegister)
                {
                    var finalSlug = !string.IsNullOrEmpty(streamerSlug) ? streamerSlug : accountId;
                    var streamerProfile = new StreamerProfile
                    {
                        UserId = user.Id,
                        Slug = finalSlug, // Tên miền trang cá nhân là mã ID của streamer
                        DisplayName = string.IsNullOrWhiteSpace(model.StreamerDisplayName) ? user.FullName : model.StreamerDisplayName.Trim(),
                        GreetingMessage = "Chào mừng bạn đến với kênh donate chính thức của mình! Cảm ơn bạn đã luôn ủng hộ ❤️",
                        Bio = null,
                        AvatarUrl = user.AvatarUrl,
                        BannerUrl = "/images/default-banner.jpg",
                        MinDonateAmount = 10000,
                        IsVerified = false,
                        ApprovalStatus = StreamerApprovalStatus.Pending, // Chờ duyệt
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StreamerProfiles.Add(streamerProfile);
                }

                await _context.SaveChangesAsync();
                return (true, string.Empty, user);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi hệ thống khi tạo tài khoản: {ex.InnerException?.Message ?? ex.Message}", null);
            }
        }

        public async Task<(bool Success, string Error, User? User, List<string> Roles)> ProcessExternalLoginAsync(
            string provider,
            string providerKey,
            string? email,
            string? displayName,
            string? avatarUrl = null)
        {
            // 1. Kiểm tra đã có ExternalLogin này chưa
            var existingLogin = await _context.ExternalLogins
                .Include(el => el.User)
                    .ThenInclude(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                .Include(el => el.User.StreamerProfile)
                .FirstOrDefaultAsync(el => el.Provider == provider && el.ProviderKey == providerKey);

            if (existingLogin != null)
            {
                var user = existingLogin.User;
                if (!user.IsActive)
                {
                    return (false, "Tài khoản của bạn đã bị vô hiệu hóa.", null, new List<string>());
                }

                if (!string.IsNullOrEmpty(avatarUrl) && (string.IsNullOrEmpty(user.AvatarUrl) || user.AvatarUrl == "/images/default-avatar.png"))
                {
                    user.AvatarUrl = avatarUrl;
                    await _context.SaveChangesAsync();
                }

                var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
                return (true, string.Empty, user, roles);
            }

            // 2. Nếu chưa có liên kết này, tìm xem email đã tồn tại chưa
            User? userWithEmail = null;
            if (!string.IsNullOrEmpty(email))
            {
                userWithEmail = await _context.Users
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .Include(u => u.StreamerProfile)
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            }

            if (userWithEmail != null)
            {
                if (!userWithEmail.IsActive)
                {
                    return (false, "Tài khoản liên kết với email này đã bị vô hiệu hóa.", null, new List<string>());
                }

                if (!string.IsNullOrEmpty(avatarUrl) && (string.IsNullOrEmpty(userWithEmail.AvatarUrl) || userWithEmail.AvatarUrl == "/images/default-avatar.png"))
                {
                    userWithEmail.AvatarUrl = avatarUrl;
                }

                // Liên kết tài khoản hiện có với Provider này
                _context.ExternalLogins.Add(new ExternalLogin
                {
                    UserId = userWithEmail.Id,
                    Provider = provider,
                    ProviderKey = providerKey,
                    ProviderDisplayName = displayName,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                var roles = userWithEmail.UserRoles.Select(ur => ur.Role.Name).ToList();
                return (true, string.Empty, userWithEmail, roles);
            }

            // 3. Tạo tài khoản mới từ OAuth
            var baseUsername = !string.IsNullOrEmpty(displayName)
                ? displayName.Replace(" ", "").ToLower()
                : (email?.Split('@')[0] ?? $"user_{Guid.NewGuid().ToString("N")[..8]}");

            var uniqueUsername = baseUsername;
            int counter = 1;
            while (await _context.Users.AnyAsync(u => u.Username.ToLower() == uniqueUsername.ToLower()))
            {
                uniqueUsername = $"{baseUsername}{counter++}";
            }

            var accountId = await _accountIdService.GenerateAccountIdAsync(isStreamer: false, DateTime.UtcNow.Year);

            var newUser = new User
            {
                AccountId = accountId,
                Username = uniqueUsername,
                Email = email ?? $"{uniqueUsername}@{provider.ToLower()}.oauth",
                FullName = displayName ?? uniqueUsername,
                PasswordHash = null, // OAuth không cần mật khẩu ban đầu
                AvatarUrl = !string.IsNullOrEmpty(avatarUrl) ? avatarUrl : "/images/default-avatar.png",
                WalletBalance = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // Gán role mặc định Viewer
            var viewerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.Viewer);
            if (viewerRole == null)
            {
                viewerRole = new Role { Name = UserRoles.Viewer, Description = "Người xem / Ủng hộ" };
                _context.Roles.Add(viewerRole);
                await _context.SaveChangesAsync();
            }

            _context.UserRoles.Add(new UserRole { UserId = newUser.Id, RoleId = viewerRole.Id });

            // Lưu liên kết ExternalLogin
            _context.ExternalLogins.Add(new ExternalLogin
            {
                UserId = newUser.Id,
                Provider = provider,
                ProviderKey = providerKey,
                ProviderDisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            var newRoles = new List<string> { UserRoles.Viewer };
            return (true, string.Empty, newUser, newRoles);
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<List<string>> GetUserRolesAsync(int userId)
        {
            return await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Name)
                .ToListAsync();
        }
    }
}
