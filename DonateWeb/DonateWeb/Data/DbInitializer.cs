using Microsoft.EntityFrameworkCore;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using DonateWeb.Areas.Widgets.Models;

namespace DonateWeb.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

            try
            {
                // Tự động tạo cơ sở dữ liệu và các bảng nếu chưa có
                await context.Database.EnsureCreatedAsync();

                // Đảm bảo cột Bio và các cột bổ sung trong StreamerProfiles tồn tại trước khi EF Core truy vấn
                await context.Database.ExecuteSqlRawAsync(@"
                    IF OBJECT_ID('dbo.StreamerProfiles', 'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'Bio')
                        BEGIN
                            ALTER TABLE dbo.StreamerProfiles ADD Bio NVARCHAR(MAX) NULL;
                        END
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'IsActive')
                        BEGIN
                            ALTER TABLE dbo.StreamerProfiles ADD IsActive BIT NOT NULL DEFAULT 1;
                            ALTER TABLE dbo.StreamerProfiles ADD LockReason NVARCHAR(500) NULL;
                            ALTER TABLE dbo.StreamerProfiles ADD LockedAt DATETIME2 NULL;
                        END
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'ApprovalStatus')
                        BEGIN
                            ALTER TABLE dbo.StreamerProfiles ADD ApprovalStatus INT NOT NULL DEFAULT 1;
                            ALTER TABLE dbo.StreamerProfiles ADD RejectionReason NVARCHAR(500) NULL;
                            ALTER TABLE dbo.StreamerProfiles ADD ApprovedAt DATETIME2 NULL;
                        END
                    END

                    IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'AccountId')
                        BEGIN
                            ALTER TABLE dbo.Users ADD AccountId NVARCHAR(10) NULL;
                        END
                    END

                    -- Bảng Sản phẩm Gian hàng Streamer
                    IF OBJECT_ID('dbo.ShopProducts', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.ShopProducts (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            StreamerProfileId INT NOT NULL,
                            Name NVARCHAR(200) NOT NULL,
                            Description NVARCHAR(MAX) NULL,
                            Price DECIMAL(18,2) NOT NULL,
                            ImageUrl NVARCHAR(500) NULL,
                            StockQuantity INT NOT NULL DEFAULT 999,
                            IsActive BIT NOT NULL DEFAULT 1,
                            ApprovalStatus INT NOT NULL DEFAULT 0,
                            RejectionReason NVARCHAR(500) NULL,
                            ApprovedAt DATETIME2 NULL,
                            ApprovedBy NVARCHAR(100) NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT FK_ShopProducts_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles(Id) ON DELETE CASCADE
                        );
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ShopProducts') AND name = 'ApprovalStatus')
                        BEGIN
                            ALTER TABLE dbo.ShopProducts ADD ApprovalStatus INT NOT NULL DEFAULT 1;
                            ALTER TABLE dbo.ShopProducts ADD RejectionReason NVARCHAR(500) NULL;
                            ALTER TABLE dbo.ShopProducts ADD ApprovedAt DATETIME2 NULL;
                            ALTER TABLE dbo.ShopProducts ADD ApprovedBy NVARCHAR(100) NULL;
                        END
                    END

                    -- Bảng Đơn hàng Mua hàng Streamer
                    IF OBJECT_ID('dbo.ShopOrders', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.ShopOrders (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            OrderCode NVARCHAR(50) NOT NULL,
                            StreamerProfileId INT NOT NULL,
                            BuyerUserId INT NULL,
                            BuyerName NVARCHAR(100) NOT NULL,
                            BuyerPhone NVARCHAR(30) NULL,
                            BuyerAddress NVARCHAR(300) NULL,
                            Note NVARCHAR(500) NULL,
                            TotalAmount DECIMAL(18,2) NOT NULL,
                            PaymentMethod NVARCHAR(50) NOT NULL DEFAULT 'Wallet',
                            PaymentStatus NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                            TransactionCode NVARCHAR(100) NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            PaidAt DATETIME2 NULL,
                            CONSTRAINT FK_ShopOrders_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles(Id) ON DELETE NO ACTION,
                            CONSTRAINT FK_ShopOrders_Users FOREIGN KEY (BuyerUserId) REFERENCES dbo.Users(Id) ON DELETE SET NULL
                        );
                    END

                    -- Bảng Chi tiết mặt hàng trong đơn hàng
                    IF OBJECT_ID('dbo.ShopOrderItems', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.ShopOrderItems (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            ShopOrderId INT NOT NULL,
                            ShopProductId INT NOT NULL,
                            ProductName NVARCHAR(200) NOT NULL,
                            Price DECIMAL(18,2) NOT NULL,
                            Quantity INT NOT NULL DEFAULT 1,
                            CONSTRAINT FK_ShopOrderItems_ShopOrders FOREIGN KEY (ShopOrderId) REFERENCES dbo.ShopOrders(Id) ON DELETE CASCADE,
                            CONSTRAINT FK_ShopOrderItems_ShopProducts FOREIGN KEY (ShopProductId) REFERENCES dbo.ShopProducts(Id) ON DELETE NO ACTION
                        );
                    END

                    -- Bảng Breaking News
                    IF OBJECT_ID('dbo.BreakingNews', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.BreakingNews (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            Content NVARCHAR(1000) NOT NULL,
                            LinkUrl NVARCHAR(500) NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedBy NVARCHAR(100) NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            UpdatedAt DATETIME2 NULL
                        );
                    END

                    -- Bảng StreamerFollows (Theo dõi Streamer - Streamer Yêu Thích)
                    IF OBJECT_ID('dbo.StreamerFollows', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.StreamerFollows (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            UserId INT NOT NULL,
                            StreamerProfileId INT NOT NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            CONSTRAINT FK_StreamerFollows_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
                            CONSTRAINT FK_StreamerFollows_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles(Id) ON DELETE NO ACTION,
                            CONSTRAINT UQ_StreamerFollows_User_Streamer UNIQUE (UserId, StreamerProfileId)
                        );
                    END

                    -- Bảng StreamerStatuses (Status / Bài viết của Streamer)
                    IF OBJECT_ID('dbo.StreamerStatuses', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.StreamerStatuses (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            StreamerProfileId INT NOT NULL,
                            Content NVARCHAR(MAX) NOT NULL,
                            ImageUrl NVARCHAR(500) NULL,
                            LikeCount INT NOT NULL DEFAULT 0,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            CONSTRAINT FK_StreamerStatuses_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles(Id) ON DELETE CASCADE
                        );
                    END
                ");

                var accountIdService = scope.ServiceProvider.GetRequiredService<IAccountIdService>();

                // 1. Seed Roles (RBAC: Admin, Streamer, Viewer)
                if (!await context.Roles.AnyAsync())
                {
                    var roles = new List<Role>
                    {
                        new Role { Name = UserRoles.Admin, Description = "Quản trị viên toàn hệ thống" },
                        new Role { Name = UserRoles.Streamer, Description = "Nhà sáng tạo nội dung / Streamer nhận donate" },
                        new Role { Name = UserRoles.Viewer, Description = "Người xem, ủng hộ và nạp ví" }
                    };

                    await context.Roles.AddRangeAsync(roles);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded roles: Admin, Streamer, Viewer successfully.");
                }

                var adminRole = await context.Roles.FirstAsync(r => r.Name == UserRoles.Admin);
                var streamerRole = await context.Roles.FirstAsync(r => r.Name == UserRoles.Streamer);
                var viewerRole = await context.Roles.FirstAsync(r => r.Name == UserRoles.Viewer);

                // 2. Seed Admin User
                if (!await context.Users.AnyAsync(u => u.Email == "admin@donateweb.com"))
                {
                    var adminUser = new User
                    {
                        AccountId = await accountIdService.GenerateAccountIdAsync(isStreamer: true, DateTime.UtcNow.Year),
                        Username = "admin",
                        Email = "admin@donateweb.com",
                        PasswordHash = passwordHasher.HashPassword("Admin@123"),
                        FullName = "Quản Trị Viên Hệ Thống",
                        PhoneNumber = "0900000001",
                        AvatarUrl = "https://api.dicebear.com/7.x/bottts/svg?seed=Admin",
                        WalletBalance = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Users.Add(adminUser);
                    await context.SaveChangesAsync();

                    context.UserRoles.Add(new UserRole
                    {
                        UserId = adminUser.Id,
                        RoleId = adminRole.Id
                    });
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded Admin user (admin@donateweb.com / Admin@123).");
                }

                // 3. Seed Streamer User với slug 'tenstreamer'
                if (!await context.Users.AnyAsync(u => u.Email == "streamer@donateweb.com"))
                {
                    var streamerUser = new User
                    {
                        AccountId = await accountIdService.GenerateAccountIdAsync(isStreamer: true, DateTime.UtcNow.Year),
                        Username = "tenstreamer",
                        Email = "streamer@donateweb.com",
                        PasswordHash = passwordHasher.HashPassword("Streamer@123"),
                        FullName = "Nguyễn Hoàng Nam",
                        PhoneNumber = "0900000002",
                        AvatarUrl = "https://api.dicebear.com/7.x/adventurer/svg?seed=HoangNam",
                        WalletBalance = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Users.Add(streamerUser);
                    await context.SaveChangesAsync();

                    context.UserRoles.Add(new UserRole
                    {
                        UserId = streamerUser.Id,
                        RoleId = streamerRole.Id
                    });

                    // Cấu hình Profile Streamer hoàn chỉnh theo đúng yêu cầu (không gán sẵn dữ liệu doanh thu/follower ảo)
                    var streamerProfile = new StreamerProfile
                    {
                        UserId = streamerUser.Id,
                        Slug = streamerUser.AccountId ?? "1202612345", // Tên miền trang cá nhân là ID của streamer
                        DisplayName = "Hoàng Nam Gaming",
                        GreetingMessage = "Cảm ơn các bạn đã ghé thăm kênh và ủng hộ mình nhé! Mọi đóng góp của bạn đều giúp mình phát triển kênh tốt hơn ❤️",
                        AvatarUrl = "https://api.dicebear.com/7.x/adventurer/svg?seed=HoangNam",
                        BannerUrl = "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=1200&auto=format&fit=crop",
                        MinDonateAmount = 10000,
                        BankName = "MB Bank (Ngân Hàng Quân Đội)",
                        BankAccountNumber = "999988887777",
                        BankAccountName = "NGUYEN HOANG NAM",
                        PaymentQrUrl = "https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=2|99|0900000002|NGUYEN%20HOANG%20NAM|streamer@donateweb.com|0|0|10000",
                        YoutubeUrl = "https://youtube.com",
                        DiscordUrl = "https://discord.com",
                        FacebookUrl = "https://facebook.com",
                        IsVerified = true,
                        ApprovalStatus = StreamerApprovalStatus.Approved,
                        ApprovedAt = DateTime.UtcNow,
                        FollowerCount = 0,
                        Rank = 1,
                        TotalReceived = 0,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.StreamerProfiles.Add(streamerProfile);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded Streamer user with slug 'tenstreamer' (streamer@donateweb.com / Streamer@123).");
                }

                // 4. Seed Viewer User
                if (!await context.Users.AnyAsync(u => u.Email == "viewer@donateweb.com"))
                {
                    var viewerUser = new User
                    {
                        AccountId = await accountIdService.GenerateAccountIdAsync(isStreamer: false, DateTime.UtcNow.Year),
                        Username = "viewer",
                        Email = "viewer@donateweb.com",
                        PasswordHash = passwordHasher.HashPassword("Viewer@123"),
                        FullName = "Trần Anh Khoa",
                        PhoneNumber = "0900000003",
                        AvatarUrl = "https://api.dicebear.com/7.x/micah/svg?seed=AnhKhoa",
                        WalletBalance = 0,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    context.Users.Add(viewerUser);
                    await context.SaveChangesAsync();

                    context.UserRoles.Add(new UserRole
                    {
                        UserId = viewerUser.Id,
                        RoleId = viewerRole.Id
                    });
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded Viewer user (viewer@donateweb.com / Viewer@123).");
                }

                // 5. Tự động chuẩn hóa phông chữ tiếng Việt & đặt lại số dư ví có sẵn của Admin và Viewer về 0
                var adminExisting = await context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
                if (adminExisting != null)
                {
                    adminExisting.FullName = "Quản Trị Viên Hệ Thống";
                    adminExisting.WalletBalance = 0;
                }

                var streamerExisting = await context.Users.FirstOrDefaultAsync(u => u.Username == "tenstreamer");
                if (streamerExisting != null && streamerExisting.FullName != "Nguyễn Hoàng Nam")
                {
                    streamerExisting.FullName = "Nguyễn Hoàng Nam";
                }

                var viewerExisting = await context.Users.FirstOrDefaultAsync(u => u.Username == "viewer");
                if (viewerExisting != null && viewerExisting.FullName != "Trần Anh Khoa")
                {
                    viewerExisting.FullName = "Trần Anh Khoa";
                }

                var streamerProfileExisting = await context.StreamerProfiles.FirstOrDefaultAsync(s => s.Slug == "tenstreamer");
                if (streamerProfileExisting != null)
                {
                    streamerProfileExisting.DisplayName = "Hoàng Nam Gaming";
                    streamerProfileExisting.GreetingMessage = "Cảm ơn các bạn đã ghé thăm kênh và ủng hộ mình nhé! Mọi đóng góp của bạn đều giúp mình phát triển kênh tốt hơn ❤️";
                    streamerProfileExisting.BankName = "MB Bank (Ngân Hàng Quân Đội)";
                    streamerProfileExisting.ApprovalStatus = StreamerApprovalStatus.Approved;
                    if (streamerProfileExisting.ApprovedAt == null)
                    {
                        streamerProfileExisting.ApprovedAt = DateTime.UtcNow;
                    }
                    if (streamerProfileExisting.FollowerCount == 12500)
                    {
                        streamerProfileExisting.FollowerCount = 0;
                    }
                }

                await context.SaveChangesAsync();

                // 5.1. Đồng bộ / cấp mã AccountId 10 số cho toàn bộ tài khoản hiện có trong DB nếu chưa có
                var usersWithoutAccountId = await context.Users
                    .Include(u => u.UserRoles)
                        .ThenInclude(ur => ur.Role)
                    .Include(u => u.StreamerProfile)
                    .Where(u => u.AccountId == null || u.AccountId == "")
                    .ToListAsync();

                foreach (var u in usersWithoutAccountId)
                {
                    bool isStreamer = u.StreamerProfile != null || u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Streamer);
                    u.AccountId = await accountIdService.GenerateAccountIdAsync(isStreamer, u.CreatedAt.Year);
                }

                // 5.2. Đảm bảo nếu tài khoản đã là Streamer mà ID vẫn còn bắt đầu bằng 9 (do nâng cấp trước đó) thì đổi thành 1
                var streamersWithViewerId = await context.Users
                    .Include(u => u.StreamerProfile)
                    .Where(u => u.StreamerProfile != null && u.AccountId != null && u.AccountId.StartsWith("9"))
                    .ToListAsync();

                foreach (var s in streamersWithViewerId)
                {
                    s.AccountId = await accountIdService.UpgradeToStreamerIdAsync(s.AccountId, s.CreatedAt.Year);
                }

                if (usersWithoutAccountId.Any() || streamersWithViewerId.Any())
                {
                    await context.SaveChangesAsync();
                    logger.LogInformation("Processed 10-digit AccountId for existing users (Added: {Count}, Upgraded: {Upgraded}).", usersWithoutAccountId.Count, streamersWithViewerId.Count);
                }

                // 5.3. Cập nhật phần tên miền trang cá nhân (Slug) của từng streamer thành ID của streamer đó
                var allStreamersForSync = await context.StreamerProfiles
                    .Include(s => s.User)
                    .ToListAsync();

                bool anySlugUpdated = false;
                foreach (var st in allStreamersForSync)
                {
                    var streamerId = st.User?.AccountId ?? st.UserId.ToString();
                    if (!string.IsNullOrWhiteSpace(streamerId) && st.Slug != streamerId)
                    {
                        if (!allStreamersForSync.Any(o => o.Id != st.Id && o.Slug == streamerId))
                        {
                            st.Slug = streamerId;
                            anySlugUpdated = true;
                        }
                    }
                }

                if (anySlugUpdated)
                {
                    await context.SaveChangesAsync();
                    logger.LogInformation("Đã đồng bộ phần tên miền trang cá nhân của tất cả streamer thành mã ID của streamer đó.");
                }

                // 6. Tự động kiểm tra và tạo bảng / cột mới cho Admin Panel nếu chạy trên máy khác
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'IsActive')
                    BEGIN
                        ALTER TABLE dbo.StreamerProfiles ADD IsActive BIT NOT NULL DEFAULT 1;
                        ALTER TABLE dbo.StreamerProfiles ADD LockReason NVARCHAR(500) NULL;
                        ALTER TABLE dbo.StreamerProfiles ADD LockedAt DATETIME2 NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'ApprovalStatus')
                    BEGIN
                        ALTER TABLE dbo.StreamerProfiles ADD ApprovalStatus INT NOT NULL DEFAULT 1;
                        ALTER TABLE dbo.StreamerProfiles ADD RejectionReason NVARCHAR(500) NULL;
                        ALTER TABLE dbo.StreamerProfiles ADD ApprovedAt DATETIME2 NULL;
                    END

                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Donations') AND name = 'IsDisputed')
                    BEGIN
                        ALTER TABLE dbo.Donations ADD IsDisputed BIT NOT NULL DEFAULT 0;
                        ALTER TABLE dbo.Donations ADD DisputeReason NVARCHAR(500) NULL;
                        ALTER TABLE dbo.Donations ADD AdminNote NVARCHAR(1000) NULL;
                        ALTER TABLE dbo.Donations ADD ResolvedAt DATETIME2 NULL;
                    END

                    IF OBJECT_ID('dbo.TransactionAuditLogs', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.TransactionAuditLogs (
                            Id INT IDENTITY(1,1) NOT NULL,
                            DonationId INT NOT NULL,
                            TransactionCode NVARCHAR(100) NOT NULL,
                            ActionType NVARCHAR(50) NOT NULL,
                            OldStatus INT NOT NULL,
                            NewStatus INT NOT NULL,
                            Note NVARCHAR(1000) NULL,
                            PerformedBy NVARCHAR(100) NOT NULL DEFAULT 'Admin',
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            CONSTRAINT PK_TransactionAuditLogs PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_TransactionAuditLogs_Donations FOREIGN KEY (DonationId) REFERENCES dbo.Donations (Id) ON DELETE CASCADE
                        );
                        CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_DonationId ON dbo.TransactionAuditLogs (DonationId);
                        CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_TransactionCode ON dbo.TransactionAuditLogs (TransactionCode);
                        CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_CreatedAt ON dbo.TransactionAuditLogs (CreatedAt);
                    END

                    -- Bảng WalletTransactions (Lịch sử Nạp tiền cho Viewer & Rút tiền cho Streamer)
                    IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.WalletTransactions (
                            Id INT IDENTITY(1,1) NOT NULL,
                            UserId INT NOT NULL,
                            TransactionCode NVARCHAR(100) NOT NULL,
                            TransactionType NVARCHAR(30) NOT NULL,
                            Amount DECIMAL(18,2) NOT NULL,
                            BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
                            BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
                            PaymentMethodName NVARCHAR(100) NULL,
                            OrderCode BIGINT NULL,
                            [Status] INT NOT NULL DEFAULT 1,
                            Note NVARCHAR(500) NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            CONSTRAINT PK_WalletTransactions PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_WalletTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
                        );
                        CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_TransactionCode ON dbo.WalletTransactions (TransactionCode);
                        CREATE NONCLUSTERED INDEX IX_WalletTransactions_UserId_CreatedAt ON dbo.WalletTransactions (UserId, CreatedAt);
                    END

                    IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WalletTransactions') AND name = 'OrderCode')
                            ALTER TABLE dbo.WalletTransactions ADD OrderCode BIGINT NULL;

                        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.WalletTransactions') AND name = 'IX_WalletTransactions_OrderCode')
                            EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_OrderCode ON dbo.WalletTransactions (OrderCode) WHERE [OrderCode] IS NOT NULL;');
                    END

                    IF OBJECT_ID('dbo.WalletTransactionAuditLogs', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.WalletTransactionAuditLogs (
                            Id INT IDENTITY(1,1) NOT NULL,
                            WalletTransactionId INT NOT NULL,
                            ActionType NVARCHAR(50) NOT NULL,
                            ExpectedAmount DECIMAL(18,2) NOT NULL,
                            ReceivedAmount DECIMAL(18,2) NULL,
                            ReferenceCode NVARCHAR(100) NULL,
                            Note NVARCHAR(1000) NULL,
                            PerformedBy NVARCHAR(100) NOT NULL DEFAULT 'PayOS:Webhook',
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            CONSTRAINT PK_WalletTransactionAuditLogs PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_WalletTransactionAuditLogs_WalletTransactions FOREIGN KEY (WalletTransactionId) REFERENCES dbo.WalletTransactions (Id) ON DELETE CASCADE
                        );
                        CREATE NONCLUSTERED INDEX IX_WalletTransactionAuditLogs_WalletTransactionId ON dbo.WalletTransactionAuditLogs (WalletTransactionId);
                        CREATE NONCLUSTERED INDEX IX_WalletTransactionAuditLogs_CreatedAt ON dbo.WalletTransactionAuditLogs (CreatedAt);
                    END

                    -- Bảng WithdrawalRequests (Yêu cầu rút tiền dành cho Streamer)
                    IF OBJECT_ID('dbo.WithdrawalRequests', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.WithdrawalRequests (
                            Id INT IDENTITY(1,1) NOT NULL,
                            UserId INT NOT NULL,
                            StreamerProfileId INT NULL,
                            TransactionCode NVARCHAR(100) NOT NULL,
                            Amount DECIMAL(18,2) NOT NULL,
                            BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
                            BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
                            BankName NVARCHAR(100) NOT NULL,
                            BankAccountNumber NVARCHAR(50) NOT NULL,
                            BankAccountName NVARCHAR(100) NOT NULL,
                            Note NVARCHAR(500) NULL,
                            [Status] INT NOT NULL DEFAULT 0,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            ProcessedAt DATETIME2 NULL,
                            CONSTRAINT PK_WithdrawalRequests PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_WithdrawalRequests_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
                            CONSTRAINT FK_WithdrawalRequests_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id)
                        );
                        CREATE UNIQUE NONCLUSTERED INDEX IX_WithdrawalRequests_TransactionCode ON dbo.WithdrawalRequests (TransactionCode);
                        CREATE NONCLUSTERED INDEX IX_WithdrawalRequests_UserId_CreatedAt ON dbo.WithdrawalRequests (UserId, CreatedAt);
                    END

                    -- Phân hệ Widget OBS: Bảng AlertBoxConfigs
                    IF OBJECT_ID('dbo.AlertBoxConfigs', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.AlertBoxConfigs (
                            Id INT IDENTITY(1,1) NOT NULL,
                            StreamerProfileId INT NOT NULL,
                            WidgetToken NVARCHAR(64) NOT NULL,
                            MediaType NVARCHAR(20) NOT NULL DEFAULT 'image',
                            MediaUrl NVARCHAR(1000) NOT NULL,
                            SoundUrl NVARCHAR(1000) NOT NULL,
                            SoundVolume INT NOT NULL DEFAULT 80,
                            DurationSeconds INT NOT NULL DEFAULT 6,
                            TextColor NVARCHAR(30) NOT NULL DEFAULT '#10b981',
                            FontFamily NVARCHAR(50) NOT NULL DEFAULT 'Inter',
                            FontSize INT NOT NULL DEFAULT 28,
                            AnimationIn NVARCHAR(50) NOT NULL DEFAULT 'fadeInDown',
                            AnimationOut NVARCHAR(50) NOT NULL DEFAULT 'fadeOutUp',
                            MessageTemplate NVARCHAR(255) NOT NULL DEFAULT 'Thong bao ung ho',
                            MinAmountToAlert DECIMAL(18,2) NOT NULL DEFAULT 10000,
                            IsTtsEnabled BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT PK_AlertBoxConfigs PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_AlertBoxConfigs_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id) ON DELETE CASCADE
                        );
                        CREATE UNIQUE NONCLUSTERED INDEX IX_AlertBoxConfigs_StreamerProfileId ON dbo.AlertBoxConfigs (StreamerProfileId);
                    END

                    -- Phân hệ Widget OBS: Bảng StreamerGoals
                    IF OBJECT_ID('dbo.StreamerGoals', 'U') IS NULL
                    BEGIN
                        CREATE TABLE dbo.StreamerGoals (
                            Id INT IDENTITY(1,1) NOT NULL,
                            StreamerProfileId INT NOT NULL,
                            Title NVARCHAR(150) NOT NULL,
                            TargetAmount DECIMAL(18,2) NOT NULL DEFAULT 10000000,
                            StartingAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
                            ManualAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
                            ProgressBarColor NVARCHAR(30) NOT NULL DEFAULT '#10b981',
                            StartDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            EndDate DATETIME2 NULL,
                            IsActive BIT NOT NULL DEFAULT 1,
                            CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                            UpdatedAt DATETIME2 NULL,
                            CONSTRAINT PK_StreamerGoals PRIMARY KEY CLUSTERED (Id),
                            CONSTRAINT FK_StreamerGoals_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id) ON DELETE CASCADE
                        );
                        CREATE NONCLUSTERED INDEX IX_StreamerGoals_StreamerProfileId_IsActive ON dbo.StreamerGoals (StreamerProfileId, IsActive);
                    END

                    -- Migration check cho các cột bổ sung nếu bảng đã tồn tại từ trước
                    IF OBJECT_ID('dbo.AlertBoxConfigs', 'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AlertBoxConfigs') AND name = 'MediaType')
                        BEGIN
                            ALTER TABLE dbo.AlertBoxConfigs ADD MediaType NVARCHAR(20) NOT NULL DEFAULT 'image';
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AlertBoxConfigs') AND name = 'MediaUrl')
                        BEGIN
                            ALTER TABLE dbo.AlertBoxConfigs ADD MediaUrl NVARCHAR(1000) NOT NULL DEFAULT 'https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif';
                            IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AlertBoxConfigs') AND name = 'ImageUrl')
                            BEGIN
                                EXEC('UPDATE dbo.AlertBoxConfigs SET MediaUrl = ImageUrl WHERE ImageUrl IS NOT NULL AND ImageUrl <> ''''');
                            END
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AlertBoxConfigs') AND name = 'CreatedAt')
                        BEGIN
                            ALTER TABLE dbo.AlertBoxConfigs ADD CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME();
                        END
                    END

                    IF OBJECT_ID('dbo.StreamerGoals', 'U') IS NOT NULL
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerGoals') AND name = 'StartingAmount')
                        BEGIN
                            ALTER TABLE dbo.StreamerGoals ADD StartingAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.StreamerGoals') AND name = 'ManualAmount')
                        BEGIN
                            ALTER TABLE dbo.StreamerGoals ADD ManualAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
                        END
                    END
                ");

                // 7. Xóa các dữ liệu mẫu (giao dịch mẫu, audit log mẫu, goal mẫu và doanh thu ảo) nếu đã tồn tại từ trước
                // để trang Admin hoàn toàn sạch dữ liệu cũ và chỉ hiển thị dữ liệu mới khi thêm vào
                var sampleTxCodes = new[] { "DON_SAMPLE_001", "DON_DISPUTE_001", "DON_REFUND_002", "DON_20260924013736_76EF90" };
                var sampleDonations = await context.Donations
                    .Where(d => d.TransactionCode != null && sampleTxCodes.Contains(d.TransactionCode))
                    .ToListAsync();

                if (sampleDonations.Any())
                {
                    var sampleDonationIds = sampleDonations.Select(d => d.Id).ToList();
                    var sampleLogs = await context.TransactionAuditLogs
                        .Where(l => sampleDonationIds.Contains(l.DonationId) || sampleTxCodes.Contains(l.TransactionCode))
                        .ToListAsync();

                    if (sampleLogs.Any())
                    {
                        context.TransactionAuditLogs.RemoveRange(sampleLogs);
                    }

                    context.Donations.RemoveRange(sampleDonations);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Đã xóa các giao dịch mẫu và nhật ký log mẫu trong trang Admin.");
                }

                // Đồng bộ lại TotalReceived và WalletBalance của tất cả Streamer theo đúng tổng tiền donate thành công thực tế
                var allStreamerProfiles = await context.StreamerProfiles
                    .Include(sp => sp.User)
                    .ToListAsync();
                foreach (var sp in allStreamerProfiles)
                {
                    var actualTotal = await context.Donations
                        .Where(d => d.StreamerProfileId == sp.Id && d.Status == DonationStatus.Success)
                        .SumAsync(d => (decimal?)d.Amount) ?? 0m;

                    if (sp.TotalReceived != actualTotal)
                    {
                        sp.TotalReceived = actualTotal;
                    }

                    if (sp.User != null)
                    {
                        if (actualTotal == 0m)
                        {
                            // Chưa có dữ liệu donate -> đặt số dư của Streamer về 0
                            sp.User.WalletBalance = 0m;
                        }
                        else
                        {
                            var totalWithdrawn = await context.WithdrawalRequests
                                .Where(wr => wr.UserId == sp.UserId && (wr.Status == 0 || wr.Status == 1))
                                .SumAsync(wr => (decimal?)wr.Amount) ?? 0m;
                            sp.User.WalletBalance = Math.Max(0m, actualTotal - totalWithdrawn);
                        }
                    }
                }
                await context.SaveChangesAsync();

                // 8. Seed Cấu hình Alert Box mặc định cho Streamer 'tenstreamer' (không seed Goal ảo)
                var tenstreamerProfile = await context.StreamerProfiles
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.Slug == "tenstreamer" || (s.User != null && s.User.Username == "tenstreamer"));
                if (tenstreamerProfile != null)
                {
                    if (!await context.AlertBoxConfigs.AnyAsync(a => a.StreamerProfileId == tenstreamerProfile.Id))
                    {
                        var alertConfig = new AlertBoxConfig
                        {
                            StreamerProfileId = tenstreamerProfile.Id,
                            WidgetToken = Guid.NewGuid().ToString("N"),
                            MediaType = "image",
                            MediaUrl = "https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif",
                            SoundUrl = "https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3",
                            SoundVolume = 80,
                            DurationSeconds = 6,
                            TextColor = "#10b981",
                            FontFamily = "Inter",
                            FontSize = 28,
                            AnimationIn = "fadeInDown",
                            AnimationOut = "fadeOutUp",
                            MessageTemplate = "{donor} vừa ủng hộ {amount} VNĐ!",
                            MinAmountToAlert = 10000,
                            IsTtsEnabled = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        context.AlertBoxConfigs.Add(alertConfig);
                        await context.SaveChangesAsync();
                    }

                    // Xóa các Goal mẫu có sẵn nếu có
                    var sampleGoals = await context.StreamerGoals
                        .Where(g => g.StreamerProfileId == tenstreamerProfile.Id &&
                                   ((g.Title == "Mua PC mới" && g.StartingAmount == 5000000) ||
                                    (g.Title == "Nâng cấp Microphone & Sound Card" && g.StartingAmount == 1500000)))
                        .ToListAsync();
                    if (sampleGoals.Any())
                    {
                        context.StreamerGoals.RemoveRange(sampleGoals);
                        await context.SaveChangesAsync();
                    }
                }

                // 8. Seed Sản phẩm mẫu cho Gian hàng Streamer nếu chưa có
                if (!await context.ShopProducts.AnyAsync())
                {
                    var allActiveStreamers = await context.StreamerProfiles.Where(s => s.IsActive).ToListAsync();
                    var sampleProducts = new List<ShopProduct>();

                    foreach (var s in allActiveStreamers)
                    {
                        sampleProducts.Add(new ShopProduct
                        {
                            StreamerProfileId = s.Id,
                            Name = $"Áo Thun Fan Kênh {s.DisplayName} (Edition 2026)",
                            Description = "Chất liệu cotton 100% thoáng mát, co giãn 4 chiều, in hình logo độc quyền sắc nét phong cách streetwear năng động.",
                            Price = 250000,
                            ImageUrl = "https://images.unsplash.com/photo-1521572267360-ee0c2909d518?q=80&w=800&auto=format&fit=crop",
                            StockQuantity = 100,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        sampleProducts.Add(new ShopProduct
                        {
                            StreamerProfileId = s.Id,
                            Name = $"Lót Chuột Gaming Speed Pro ({s.DisplayName} Edition)",
                            Description = "Kích thước lớn 900x400x4mm, bề mặt vải dệt micro-weave tối ưu mắt đọc cảm biến chuột, đế cao su chống trượt tuyệt đối.",
                            Price = 180000,
                            ImageUrl = "https://images.unsplash.com/photo-1616588589676-62b3bd4ff6d2?q=80&w=800&auto=format&fit=crop",
                            StockQuantity = 150,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        sampleProducts.Add(new ShopProduct
                        {
                            StreamerProfileId = s.Id,
                            Name = $"Móc Khóa Mica Khắc Tên Kênh {s.DisplayName}",
                            Description = "Móc khóa mica acrylic 2 lớp cao cấp dày 3mm, chống trầy xước, kèm chuông và móc kim loại không gỉ sang trọng.",
                            Price = 45000,
                            ImageUrl = "https://images.unsplash.com/photo-1607604276583-eef5d076aa5f?q=80&w=800&auto=format&fit=crop",
                            StockQuantity = 200,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        sampleProducts.Add(new ShopProduct
                        {
                            StreamerProfileId = s.Id,
                            Name = $"Bình Nước Giữ Nhiệt {s.DisplayName} 500ml",
                            Description = "Inox 304 tiêu chuẩn thực phẩm, giữ nhiệt nóng/lạnh 12 tiếng, nắp cảm ứng hiển thị nhiệt độ LED thông minh.",
                            Price = 195000,
                            ImageUrl = "https://images.unsplash.com/photo-1602143407151-7111542de6e8?q=80&w=800&auto=format&fit=crop",
                            StockQuantity = 80,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    if (sampleProducts.Any())
                    {
                        await context.ShopProducts.AddRangeAsync(sampleProducts);
                        await context.SaveChangesAsync();
                        logger.LogInformation("Đã seed {count} sản phẩm gian hàng mẫu cho các Streamer thành công.", sampleProducts.Count);
                    }
                }

                // Seed bài viết Status mẫu cho Streamer nếu chưa có
                if (!await context.StreamerStatuses.AnyAsync())
                {
                    var allActiveStreamers = await context.StreamerProfiles.Where(s => s.IsActive).Take(3).ToListAsync();
                    if (allActiveStreamers.Any())
                    {
                        var sampleStatuses = new List<StreamerStatus>();
                        int index = 0;
                        foreach (var st in allActiveStreamers)
                        {
                            sampleStatuses.Add(new StreamerStatus
                            {
                                StreamerProfileId = st.Id,
                                Content = index == 0
                                    ? $"Chào cả nhà! Tối nay 20h00 mình lên sóng livestream giao lưu cùng mọi người nhé. Có rất nhiều quà tặng hấp dẫn đang chờ đón anh em! ❤️🎮"
                                    : $"Cảm ơn tất cả mọi người đã luôn ủng hộ và đồng hành cùng {st.DisplayName} trong suốt thời gian qua! Chúc đại gia đình một ngày tràn ngập niềm vui ✨🍀",
                                ImageUrl = index == 0
                                    ? "https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=800&auto=format&fit=crop"
                                    : "https://images.unsplash.com/photo-1511512578047-dfb367046420?q=80&w=800&auto=format&fit=crop",
                                LikeCount = 15 + index * 8,
                                CreatedAt = DateTime.UtcNow.AddHours(-2 - index * 3)
                            });
                            index++;
                        }
                        await context.StreamerStatuses.AddRangeAsync(sampleStatuses);
                        await context.SaveChangesAsync();
                        logger.LogInformation("Đã seed {count} status mẫu cho các Streamer.", sampleStatuses.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi xảy ra trong quá trình khởi tạo và seed cơ sở dữ liệu.");
            }
        }
    }
}
