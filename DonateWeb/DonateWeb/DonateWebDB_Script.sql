-- ====================================================================
-- DỰ ÁN: DONATEWEB - WEBSITE DONATE CHO STREAMER
-- HỆ THỐNG CƠ SỞ DỮ LIỆU SQL SERVER
-- Tác giả: Nhóm BTL Web Donate
-- Mô tả: Script khởi tạo Database, Bảng, Ràng buộc khóa ngoại, Index
--       và Dữ liệu mẫu (Roles, Admin, Streamer, Viewer)
-- ====================================================================

USE master;
GO

-- 1. Tạo Database nếu chưa có
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'DonateWebDB')
BEGIN
    CREATE DATABASE DonateWebDB;
END
GO

USE DonateWebDB;
GO

-- 2. Xóa các bảng cũ nếu tồn tại (đúng thứ tự khóa ngoại)
IF OBJECT_ID('dbo.WithdrawalRequests', 'U') IS NOT NULL DROP TABLE dbo.WithdrawalRequests;
IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NOT NULL DROP TABLE dbo.WalletTransactions;
IF OBJECT_ID('dbo.StreamerGoals', 'U') IS NOT NULL DROP TABLE dbo.StreamerGoals;
IF OBJECT_ID('dbo.AlertBoxConfigs', 'U') IS NOT NULL DROP TABLE dbo.AlertBoxConfigs;
IF OBJECT_ID('dbo.TransactionAuditLogs', 'U') IS NOT NULL DROP TABLE dbo.TransactionAuditLogs;
IF OBJECT_ID('dbo.Donations', 'U') IS NOT NULL DROP TABLE dbo.Donations;
IF OBJECT_ID('dbo.StreamerProfiles', 'U') IS NOT NULL DROP TABLE dbo.StreamerProfiles;
IF OBJECT_ID('dbo.ExternalLogins', 'U') IS NOT NULL DROP TABLE dbo.ExternalLogins;
IF OBJECT_ID('dbo.UserRoles', 'U') IS NOT NULL DROP TABLE dbo.UserRoles;
IF OBJECT_ID('dbo.Roles', 'U') IS NOT NULL DROP TABLE dbo.Roles;
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DROP TABLE dbo.Users;
GO

-- ====================================================================
-- 3. TẠO CÁC BẢNG (TABLES)
-- ====================================================================

-- 3.1. Bảng Users (Người dùng: Admin, Streamer, Viewer/Donor)
CREATE TABLE dbo.Users (
    Id INT IDENTITY(1,1) NOT NULL,
    AccountId NVARCHAR(10) NULL, -- ID 10 số (Streamer bắt đầu bằng 1, Viewer bắt đầu bằng 9, 4 số tiếp theo là năm tạo)
    Username NVARCHAR(50) NOT NULL,
    Email NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(MAX) NULL, -- Hash bằng BCrypt; NULL nếu đăng ký thuần OAuth
    FullName NVARCHAR(100) NOT NULL,
    PhoneNumber NVARCHAR(20) NULL,
    AvatarUrl NVARCHAR(500) NOT NULL DEFAULT '/images/default-avatar.png',
    WalletBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL,
    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id)
);
GO

-- Tạo chỉ mục duy nhất (Unique Index) cho Username, Email và AccountId
CREATE UNIQUE NONCLUSTERED INDEX IX_Users_Username ON dbo.Users (Username);
CREATE UNIQUE NONCLUSTERED INDEX IX_Users_Email ON dbo.Users (Email);
CREATE UNIQUE NONCLUSTERED INDEX IX_Users_AccountId ON dbo.Users (AccountId) WHERE AccountId IS NOT NULL;
GO

-- 3.2. Bảng Roles (Vai trò người dùng trong hệ thống RBAC: Admin, Streamer, Viewer)
CREATE TABLE dbo.Roles (
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(50) NOT NULL,
    Description NVARCHAR(200) NULL,
    CONSTRAINT PK_Roles PRIMARY KEY CLUSTERED (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_Roles_Name ON dbo.Roles (Name);
GO

-- 3.3. Bảng UserRoles (Liên kết Nhiều - Nhiều giữa Users và Roles)
CREATE TABLE dbo.UserRoles (
    UserId INT NOT NULL,
    RoleId INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY CLUSTERED (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (Id) ON DELETE CASCADE
);
GO

-- 3.4. Bảng ExternalLogins (Lưu thông tin đăng nhập OAuth2: Google, Discord, Twitch, YouTube)
CREATE TABLE dbo.ExternalLogins (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Provider NVARCHAR(50) NOT NULL,          -- 'Google', 'Discord', 'Twitch', 'YouTube'
    ProviderKey NVARCHAR(200) NOT NULL,       -- ID người dùng từ phía Provider
    ProviderDisplayName NVARCHAR(200) NULL,  -- Tên hiển thị bên thứ 3
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ExternalLogins PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ExternalLogins_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_ExternalLogins_Provider_Key UNIQUE (Provider, ProviderKey)
);
GO

-- 3.5. Bảng StreamerProfiles (Hồ sơ Streamer: Slug cá nhân, cấu hình trang nhận donate, tài khoản nhận tiền)
CREATE TABLE dbo.StreamerProfiles (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Slug NVARCHAR(50) NOT NULL,               -- Tên miền / Định danh trang cá nhân là ID của streamer: domain.com/{streamerId}
    DisplayName NVARCHAR(100) NOT NULL,       -- Tên hiển thị streamer trên trang donate
    GreetingMessage NVARCHAR(500) NOT NULL,   -- Lời chào người xem
    Bio NVARCHAR(MAX) NULL,                   -- Giới thiệu bản thân (Bio / About Me) - lưu HTML từ rich text editor
    AvatarUrl NVARCHAR(500) NOT NULL DEFAULT '/images/default-avatar.png',
    BannerUrl NVARCHAR(500) NOT NULL DEFAULT '/images/default-banner.jpg',
    MinDonateAmount DECIMAL(18,2) NOT NULL DEFAULT 10000, -- Mức donate tối thiểu (VNĐ)
    
    -- Thông tin thanh toán & nhận donate
    BankName NVARCHAR(100) NULL,
    BankAccountNumber NVARCHAR(50) NULL,
    BankAccountName NVARCHAR(100) NULL,
    PaymentQrUrl NVARCHAR(500) NULL,          -- Link ảnh mã VietQR / MoMo
    
    -- Mạng xã hội
    YoutubeUrl NVARCHAR(255) NULL,
    TwitchUrl NVARCHAR(255) NULL,
    DiscordUrl NVARCHAR(255) NULL,
    FacebookUrl NVARCHAR(255) NULL,
    TiktokUrl NVARCHAR(255) NULL,
    
    IsVerified BIT NOT NULL DEFAULT 0,
    ApprovalStatus INT NOT NULL DEFAULT 1,    -- Trạng thái duyệt: 0: Chờ duyệt, 1: Đã duyệt, 2: Bị từ chối
    RejectionReason NVARCHAR(500) NULL,       -- Lý do từ chối duyệt
    ApprovedAt DATETIME2 NULL,                -- Thời điểm phê duyệt
    IsActive BIT NOT NULL DEFAULT 1,          -- Trạng thái kênh: 1: Hoạt động, 0: Khóa vi phạm
    LockReason NVARCHAR(500) NULL,            -- Lý do khóa kênh vi phạm chính sách
    LockedAt DATETIME2 NULL,                  -- Thời điểm khóa
    FollowerCount INT NOT NULL DEFAULT 0,
    [Rank] INT NOT NULL DEFAULT 1,
    TotalReceived DECIMAL(18,2) NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL,
    
    CONSTRAINT PK_StreamerProfiles PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_StreamerProfiles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

-- Ràng buộc 1 User chỉ có tối đa 1 StreamerProfile và Slug phải là duy nhất
CREATE UNIQUE NONCLUSTERED INDEX IX_StreamerProfiles_UserId ON dbo.StreamerProfiles (UserId);
CREATE UNIQUE NONCLUSTERED INDEX IX_StreamerProfiles_Slug ON dbo.StreamerProfiles (Slug);
GO

-- 3.6. Bảng Donations (Giao dịch ủng hộ / quyên góp cho Streamer)
CREATE TABLE dbo.Donations (
    Id INT IDENTITY(1,1) NOT NULL,
    StreamerProfileId INT NOT NULL,
    DonorUserId INT NULL,                     -- Nullable nếu người ủng hộ ẩn danh
    DonorName NVARCHAR(100) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Message NVARCHAR(1000) NULL,
    PaymentMethod INT NOT NULL DEFAULT 1,     -- 1: Ví, 2: Chuyển khoản QR, 3: MoMo, 4: VNPay
    [Status] INT NOT NULL DEFAULT 0,          -- 0: Pending, 1: Success, 2: Failed, 3: Cancelled, 4: Disputed, 5: Refunded
    TransactionCode NVARCHAR(100) NULL,
    IsDisputed BIT NOT NULL DEFAULT 0,        -- 1: Đang có khiếu nại tranh chấp
    DisputeReason NVARCHAR(500) NULL,         -- Lý do khiếu nại của người dùng
    AdminNote NVARCHAR(1000) NULL,            -- Ghi chú kiểm tra và phân xử của Admin
    ResolvedAt DATETIME2 NULL,                -- Thời gian giải quyết xong
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT PK_Donations PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Donations_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id),
    CONSTRAINT FK_Donations_Users FOREIGN KEY (DonorUserId) REFERENCES dbo.Users (Id) ON DELETE SET NULL
);
GO

-- 3.7. Bảng TransactionAuditLogs (Nhật ký kiểm tra và xử lý khiếu nại/lỗi giao dịch)
CREATE TABLE dbo.TransactionAuditLogs (
    Id INT IDENTITY(1,1) NOT NULL,
    DonationId INT NOT NULL,
    TransactionCode NVARCHAR(100) NOT NULL,
    ActionType NVARCHAR(50) NOT NULL,         -- DISPUTE_OPENED, REFUNDED, VERIFIED_SUCCESS, CANCELLED, ADMIN_NOTE
    OldStatus INT NOT NULL,
    NewStatus INT NOT NULL,
    Note NVARCHAR(1000) NULL,
    PerformedBy NVARCHAR(100) NOT NULL DEFAULT 'Admin',
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT PK_TransactionAuditLogs PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_TransactionAuditLogs_Donations FOREIGN KEY (DonationId) REFERENCES dbo.Donations (Id) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_DonationId ON dbo.TransactionAuditLogs (DonationId);
CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_TransactionCode ON dbo.TransactionAuditLogs (TransactionCode);
CREATE NONCLUSTERED INDEX IX_TransactionAuditLogs_CreatedAt ON dbo.TransactionAuditLogs (CreatedAt);
GO

-- 3.8. Bảng AlertBoxConfigs (Cấu hình hiển thị Alert Box trên OBS: GIF/Video, âm thanh, font chữ, animation)
CREATE TABLE dbo.AlertBoxConfigs (
    Id INT IDENTITY(1,1) NOT NULL,
    StreamerProfileId INT NOT NULL,
    WidgetToken NVARCHAR(64) NOT NULL,
    MediaType NVARCHAR(20) NOT NULL DEFAULT 'image',          -- 'image' hoặc 'video'
    MediaUrl NVARCHAR(1000) NOT NULL,                        -- URL ảnh GIF hoặc video WebM/MP4
    SoundUrl NVARCHAR(1000) NOT NULL,                        -- URL file âm thanh MP3
    SoundVolume INT NOT NULL DEFAULT 80,                     -- Âm lượng (0 - 100%)
    DurationSeconds INT NOT NULL DEFAULT 6,                  -- Thời gian hiển thị (giây)
    TextColor NVARCHAR(30) NOT NULL DEFAULT '#10b981',       -- Mã màu chữ thông báo
    FontFamily NVARCHAR(50) NOT NULL DEFAULT 'Inter',        -- Phông chữ hiển thị
    FontSize INT NOT NULL DEFAULT 28,                        -- Cỡ chữ (px)
    AnimationIn NVARCHAR(50) NOT NULL DEFAULT 'fadeInDown',  -- Hiệu ứng xuất hiện
    AnimationOut NVARCHAR(50) NOT NULL DEFAULT 'fadeOutUp',  -- Hiệu ứng biến mất
    MessageTemplate NVARCHAR(255) NOT NULL DEFAULT '{donor} vừa ủng hộ {amount} VNĐ!',
    MinAmountToAlert DECIMAL(18,2) NOT NULL DEFAULT 10000,
    IsTtsEnabled BIT NOT NULL DEFAULT 1,                     -- Bật giọng đọc Text-To-Speech
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL,
    
    CONSTRAINT PK_AlertBoxConfigs PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_AlertBoxConfigs_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id) ON DELETE CASCADE
);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_AlertBoxConfigs_StreamerProfileId ON dbo.AlertBoxConfigs (StreamerProfileId);
GO

-- 3.9. Bảng StreamerGoals (Mục tiêu quyên góp streamer: Tiến độ thanh chạy "Mua PC mới: 5.000.000đ / 10.000.000đ")
CREATE TABLE dbo.StreamerGoals (
    Id INT IDENTITY(1,1) NOT NULL,
    StreamerProfileId INT NOT NULL,
    Title NVARCHAR(150) NOT NULL,
    TargetAmount DECIMAL(18,2) NOT NULL DEFAULT 10000000,     -- Số tiền mục tiêu cần đạt
    StartingAmount DECIMAL(18,2) NOT NULL DEFAULT 0,         -- Số tiền khởi điểm ban đầu
    ManualAmount DECIMAL(18,2) NOT NULL DEFAULT 0,           -- Số tiền cộng dồn thủ công
    ProgressBarColor NVARCHAR(30) NOT NULL DEFAULT '#10b981',-- Màu thanh chạy
    StartDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    EndDate DATETIME2 NULL,
    IsActive BIT NOT NULL DEFAULT 1,                         -- Kích hoạt phát trên OBS
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL,
    
    CONSTRAINT PK_StreamerGoals PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_StreamerGoals_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX IX_StreamerGoals_StreamerProfileId_IsActive ON dbo.StreamerGoals (StreamerProfileId, IsActive);
GO

-- 3.10. Bảng WalletTransactions (Lịch sử Nạp tiền cho Viewer & Rút tiền cho Streamer)
CREATE TABLE dbo.WalletTransactions (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    TransactionCode NVARCHAR(100) NOT NULL,
    TransactionType NVARCHAR(30) NOT NULL,               -- 'DEPOSIT' (Nạp tiền), 'WITHDRAW' (Rút tiền)
    Amount DECIMAL(18,2) NOT NULL,
    BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
    BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
    PaymentMethodName NVARCHAR(100) NULL,
    [Status] INT NOT NULL DEFAULT 1,                     -- 0: Pending, 1: Success, 2: Failed/Rejected
    Note NVARCHAR(500) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    
    CONSTRAINT PK_WalletTransactions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_WalletTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_WalletTransactions_TransactionCode ON dbo.WalletTransactions (TransactionCode);
CREATE NONCLUSTERED INDEX IX_WalletTransactions_UserId_CreatedAt ON dbo.WalletTransactions (UserId, CreatedAt);
GO

-- 3.11. Bảng WithdrawalRequests (Yêu cầu rút tiền dành cho Streamer)
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
    [Status] INT NOT NULL DEFAULT 0,                     -- 0: Đang chờ duyệt (Pending), 1: Đã duyệt (Approved), 2: Từ chối (Rejected)
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedAt DATETIME2 NULL,
    
    CONSTRAINT PK_WithdrawalRequests PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_WithdrawalRequests_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
    CONSTRAINT FK_WithdrawalRequests_StreamerProfiles FOREIGN KEY (StreamerProfileId) REFERENCES dbo.StreamerProfiles (Id)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX IX_WithdrawalRequests_TransactionCode ON dbo.WithdrawalRequests (TransactionCode);
CREATE NONCLUSTERED INDEX IX_WithdrawalRequests_UserId_CreatedAt ON dbo.WithdrawalRequests (UserId, CreatedAt);
GO

-- ====================================================================
-- 4. DỮ LIỆU KHỞI TẠO MẪU (SEED DATA)
-- ====================================================================

-- 4.1. Thêm Roles
INSERT INTO dbo.Roles (Name, Description) VALUES
(N'Admin', N'Quản trị viên toàn hệ thống'),
(N'Streamer', N'Nhà sáng tạo nội dung / Streamer nhận donate'),
(N'Viewer', N'Người xem, ủng hộ và nạp ví');
GO

-- 4.2. Thêm Tài khoản Users mẫu
-- Mật khẩu đã được hash bằng BCrypt (WorkFactor = 11):
-- Admin@123     -> $2a$11$sGYrf59mi.TUe1FpsSfhJOmIXv6cH5rrHjYd6pMfkG7xM0e.XMaTu
-- Streamer@123  -> $2a$11$HKHfJMdSqIZvBme6VqFl9esGlu5ci2b8h6XLRyT/xzpmOyKs.Lwpe
-- Viewer@123    -> $2a$11$TR/gORn9yb/Nyob9ooE.FuyLb0XdhbBFAdssKyt6zmi0PnpG/JXXS

INSERT INTO dbo.Users (AccountId, Username, Email, PasswordHash, FullName, PhoneNumber, AvatarUrl, WalletBalance, IsActive, CreatedAt)
VALUES 
(N'1202600001', N'admin', N'admin@donateweb.com', N'$2a$11$sGYrf59mi.TUe1FpsSfhJOmIXv6cH5rrHjYd6pMfkG7xM0e.XMaTu', N'Quản Trị Viên Hệ Thống', N'0900000001', N'https://api.dicebear.com/7.x/bottts/svg?seed=Admin', 0.00, 1, SYSUTCDATETIME()),
(N'1202612345', N'tenstreamer', N'streamer@donateweb.com', N'$2a$11$HKHfJMdSqIZvBme6VqFl9esGlu5ci2b8h6XLRyT/xzpmOyKs.Lwpe', N'Nguyễn Hoàng Nam', N'0900000002', N'https://api.dicebear.com/7.x/adventurer/svg?seed=HoangNam', 0.00, 1, SYSUTCDATETIME()),
(N'9202654321', N'viewer', N'viewer@donateweb.com', N'$2a$11$TR/gORn9yb/Nyob9ooE.FuyLb0XdhbBFAdssKyt6zmi0PnpG/JXXS', N'Trần Anh Khoa', N'0900000003', N'https://api.dicebear.com/7.x/micah/svg?seed=AnhKhoa', 0.00, 1, SYSUTCDATETIME());
GO

-- 4.3. Phân quyền User - Role
INSERT INTO dbo.UserRoles (UserId, RoleId)
SELECT u.Id, r.Id FROM dbo.Users u, dbo.Roles r WHERE u.Username = N'admin' AND r.Name = N'Admin'
UNION ALL
SELECT u.Id, r.Id FROM dbo.Users u, dbo.Roles r WHERE u.Username = N'tenstreamer' AND r.Name = N'Streamer'
UNION ALL
SELECT u.Id, r.Id FROM dbo.Users u, dbo.Roles r WHERE u.Username = N'viewer' AND r.Name = N'Viewer';
GO

-- 4.4. Cấu hình Profile Streamer (tenstreamer)
INSERT INTO dbo.StreamerProfiles (
    UserId, Slug, DisplayName, GreetingMessage, AvatarUrl, BannerUrl, MinDonateAmount,
    BankName, BankAccountNumber, BankAccountName, PaymentQrUrl,
    YoutubeUrl, DiscordUrl, FacebookUrl, IsVerified, FollowerCount, [Rank], TotalReceived, CreatedAt
)
SELECT 
    u.Id,
    COALESCE(u.AccountId, CAST(u.Id AS NVARCHAR(50))), -- Tên miền trang cá nhân là ID của streamer (ví dụ: 1202612345)
    N'Hoàng Nam Gaming',
    N'Cảm ơn các bạn đã ghé thăm kênh và ủng hộ mình nhé! Mọi đóng góp của bạn đều giúp mình phát triển kênh tốt hơn ❤️',
    N'https://api.dicebear.com/7.x/adventurer/svg?seed=HoangNam',
    N'https://images.unsplash.com/photo-1542751371-adc38448a05e?q=80&w=1200&auto=format&fit=crop',
    10000.00,
    N'MB Bank (Ngân Hàng Quân Đội)',
    N'999988887777',
    N'NGUYEN HOANG NAM',
    N'https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=2|99|0900000002|NGUYEN%20HOANG%20NAM|streamer@donateweb.com|0|0|10000',
    N'https://youtube.com',
    N'https://discord.com',
    N'https://facebook.com',
    1,
    0,
    1,
    0.00,
    SYSUTCDATETIME()
FROM dbo.Users u
WHERE u.Username = N'tenstreamer';
GO

-- 4.5. Cấu hình Alert Box mặc định cho streamer 'tenstreamer'
INSERT INTO dbo.AlertBoxConfigs (
    StreamerProfileId, WidgetToken, MediaType, MediaUrl, SoundUrl, SoundVolume, DurationSeconds,
    TextColor, FontFamily, FontSize, AnimationIn, AnimationOut, MessageTemplate, MinAmountToAlert, IsTtsEnabled, CreatedAt
)
SELECT 
    sp.Id,
    N'9f2d1e8c7b6a5049382716a5b4c3d2e1',
    N'image',
    N'https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif',
    N'https://assets.mixkit.co/active_storage/sfx/2869/2869-preview.mp3',
    80,
    6,
    N'#10b981',
    N'Inter',
    28,
    N'fadeInDown',
    N'fadeOutUp',
    N'{donor} vừa ủng hộ {amount} VNĐ!',
    10000.00,
    1,
    SYSUTCDATETIME()
FROM dbo.StreamerProfiles sp
WHERE sp.Slug = N'tenstreamer';
GO

-- ====================================================================
-- HOÀN TẤT KHỞI TẠO CƠ SỞ DỮ LIỆU
-- ====================================================================
SELECT 'Cơ sở dữ liệu DonateWebDB đã được tạo thành công với đầy đủ bảng và dữ liệu mẫu!' AS Notification;
GO

-- ====================================================================
-- MIGRATION: Thêm cột Bio (Giới thiệu bản thân) vào bảng StreamerProfiles
-- Chạy đoạn này nếu database đã tồn tại và cần cập nhật thêm cột mới.
-- (Không cần chạy nếu bạn khởi tạo lại database từ đầu với script trên)
-- ====================================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.StreamerProfiles') AND name = 'Bio'
)
BEGIN
    ALTER TABLE dbo.StreamerProfiles
    ADD Bio NVARCHAR(MAX) NULL;
    PRINT 'Da them cot Bio vao bang StreamerProfiles thanh cong!';
END
ELSE
BEGIN
    PRINT 'Cot Bio da ton tai trong bang StreamerProfiles. Bo qua.';
END
GO

-- MIGRATION: Thêm cột AccountId (ID 10 số) vào bảng Users
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'AccountId'
)
BEGIN
    ALTER TABLE dbo.Users
    ADD AccountId NVARCHAR(10) NULL;
    PRINT 'Da them cot AccountId vao bang Users thanh cong!';
END
ELSE
BEGIN
    PRINT 'Cot AccountId da ton tai trong bang Users. Bo qua.';
END
GO
