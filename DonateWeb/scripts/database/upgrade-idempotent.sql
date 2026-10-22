IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [BreakingNews] (
        [Id] int NOT NULL IDENTITY,
        [Content] nvarchar(1000) NOT NULL,
        [LinkUrl] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_BreakingNews] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [DiscountCoupons] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Content] nvarchar(250) NOT NULL,
        [Provider] nvarchar(100) NOT NULL,
        [DiscountType] nvarchar(20) NOT NULL,
        [DiscountValue] decimal(18,2) NOT NULL,
        [ExpiresAtUtc] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [UsedCount] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_DiscountCoupons] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [Description] nvarchar(200) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [SpecialEffectCampaigns] (
        [Id] int NOT NULL IDENTITY,
        [EffectCode] nvarchar(50) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [Description] nvarchar(500) NULL,
        [StartsAtUtc] datetime2 NOT NULL,
        [EndsAtUtc] datetime2 NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_SpecialEffectCampaigns] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [SupportAiMonthlyUsages] (
        [Period] nvarchar(7) NOT NULL,
        [TotalTokens] bigint NOT NULL,
        [ReservedTokens] bigint NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportAiMonthlyUsages] PRIMARY KEY ([Period])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [AccountId] nvarchar(10) NULL,
        [Username] nvarchar(50) NOT NULL,
        [Email] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [FullName] nvarchar(100) NOT NULL,
        [PhoneNumber] nvarchar(20) NULL,
        [AvatarUrl] nvarchar(500) NOT NULL,
        [WalletBalance] decimal(18,2) NOT NULL,
        [StreamerEarningsBalance] decimal(18,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [ExternalLogins] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Provider] nvarchar(50) NOT NULL,
        [ProviderKey] nvarchar(200) NOT NULL,
        [ProviderDisplayName] nvarchar(200) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ExternalLogins] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ExternalLogins_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerProfiles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Slug] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(100) NOT NULL,
        [GreetingMessage] nvarchar(500) NOT NULL,
        [Bio] nvarchar(max) NULL,
        [AvatarUrl] nvarchar(500) NOT NULL,
        [BannerUrl] nvarchar(500) NOT NULL,
        [MinDonateAmount] decimal(18,2) NOT NULL,
        [BankName] nvarchar(100) NULL,
        [BankAccountNumber] nvarchar(50) NULL,
        [BankAccountName] nvarchar(100) NULL,
        [PaymentQrUrl] nvarchar(500) NULL,
        [YoutubeUrl] nvarchar(255) NULL,
        [TwitchUrl] nvarchar(255) NULL,
        [DiscordUrl] nvarchar(255) NULL,
        [FacebookUrl] nvarchar(255) NULL,
        [TiktokUrl] nvarchar(255) NULL,
        [IsVerified] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [LockReason] nvarchar(500) NULL,
        [LockedAt] datetime2 NULL,
        [FollowerCount] int NOT NULL,
        [Rank] int NOT NULL,
        [ApprovalStatus] int NOT NULL,
        [RejectionReason] nvarchar(500) NULL,
        [ApprovedAt] datetime2 NULL,
        [RequiredContractVersion] nvarchar(40) NULL,
        [IdentityDataProtected] nvarchar(max) NULL,
        [CitizenIdFrontDocumentKey] nvarchar(80) NULL,
        [CitizenIdBackDocumentKey] nvarchar(80) NULL,
        [IdentityDataConsentAt] datetime2 NULL,
        [TotalReceived] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_StreamerProfiles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerProfiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [SupportConversations] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NULL,
        [GuestTokenHash] nvarchar(64) NULL,
        [VisitorName] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [AiPaused] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastMessageAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportConversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportConversations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [WalletTransactions] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [TransactionCode] nvarchar(100) NOT NULL,
        [OrderCode] bigint NULL,
        [PaymentQrCode] nvarchar(2000) NULL,
        [PaymentCheckoutUrl] nvarchar(1000) NULL,
        [PaymentBankId] nvarchar(20) NULL,
        [PaymentAccountNo] nvarchar(50) NULL,
        [PaymentAccountName] nvarchar(100) NULL,
        [FailedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        [TransactionType] nvarchar(30) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [BalanceBefore] decimal(18,2) NOT NULL,
        [BalanceAfter] decimal(18,2) NOT NULL,
        [PaymentMethodName] nvarchar(100) NULL,
        [Status] int NOT NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WalletTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTransactions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [AlertBoxConfigs] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [WidgetToken] nvarchar(64) NOT NULL,
        [MediaType] nvarchar(20) NOT NULL,
        [MediaUrl] nvarchar(1000) NOT NULL,
        [SoundUrl] nvarchar(1000) NOT NULL,
        [SoundVolume] int NOT NULL,
        [DurationSeconds] int NOT NULL,
        [TextColor] nvarchar(30) NOT NULL,
        [FontFamily] nvarchar(50) NOT NULL,
        [FontSize] int NOT NULL,
        [AnimationIn] nvarchar(50) NOT NULL,
        [AnimationOut] nvarchar(50) NOT NULL,
        [MessageTemplate] nvarchar(255) NOT NULL,
        [MinAmountToAlert] decimal(18,2) NOT NULL,
        [IsTtsEnabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AlertBoxConfigs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AlertBoxConfigs_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [Donations] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [DonorUserId] int NULL,
        [DonorName] nvarchar(100) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Message] nvarchar(1000) NULL,
        [PaymentMethod] int NOT NULL,
        [Status] int NOT NULL,
        [TransactionCode] nvarchar(100) NULL,
        [ExternalTransactionId] nvarchar(100) NULL,
        [SpecialEffectCode] nvarchar(50) NULL,
        [SpecialEffectTitle] nvarchar(120) NULL,
        [IsDisputed] bit NOT NULL,
        [DisputeReason] nvarchar(500) NULL,
        [AdminNote] nvarchar(1000) NULL,
        [ResolvedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Donations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Donations_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Donations_Users_DonorUserId] FOREIGN KEY ([DonorUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [ShopOrders] (
        [Id] int NOT NULL IDENTITY,
        [OrderCode] nvarchar(50) NOT NULL,
        [GuestAccessTokenHash] nvarchar(64) NULL,
        [StreamerProfileId] int NOT NULL,
        [BuyerUserId] int NULL,
        [BuyerName] nvarchar(100) NOT NULL,
        [BuyerPhone] nvarchar(30) NULL,
        [BuyerAddress] nvarchar(300) NULL,
        [Note] nvarchar(500) NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [SubtotalAmount] decimal(18,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [ShippingFee] decimal(18,2) NOT NULL,
        [CouponCode] nvarchar(50) NULL,
        [DiscountCouponId] int NULL,
        [PaymentMethod] nvarchar(50) NOT NULL,
        [PaymentStatus] nvarchar(50) NOT NULL,
        [StockReserved] bit NOT NULL,
        [TransactionCode] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [PaidAt] datetime2 NULL,
        CONSTRAINT [PK_ShopOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShopOrders_DiscountCoupons_DiscountCouponId] FOREIGN KEY ([DiscountCouponId]) REFERENCES [DiscountCoupons] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ShopOrders_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]),
        CONSTRAINT [FK_ShopOrders_Users_BuyerUserId] FOREIGN KEY ([BuyerUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [ShopProducts] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Category] nvarchar(80) NOT NULL,
        [AvailableSizesJson] nvarchar(1000) NOT NULL,
        [AdditionalImageUrlsJson] nvarchar(max) NULL,
        [VariantsJson] nvarchar(max) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [WeightGrams] int NULL,
        [LengthCm] int NULL,
        [WidthCm] int NULL,
        [HeightCm] int NULL,
        [ImageUrl] nvarchar(500) NULL,
        [StockQuantity] int NOT NULL,
        [IsActive] bit NOT NULL,
        [ApprovalStatus] int NOT NULL,
        [RejectionReason] nvarchar(500) NULL,
        [ApprovedAt] datetime2 NULL,
        [ApprovedBy] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_ShopProducts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShopProducts_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerContractAcceptances] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [UserId] int NOT NULL,
        [ContractVersion] nvarchar(40) NOT NULL,
        [ContractSnapshotProtected] nvarchar(max) NOT NULL,
        [ContractSnapshotSha256] nvarchar(64) NOT NULL,
        [SignerFullName] nvarchar(100) NOT NULL,
        [SignedAtUtc] datetime2 NOT NULL,
        [ClientIpAddress] nvarchar(45) NULL,
        [UserAgent] nvarchar(500) NULL,
        CONSTRAINT [PK_StreamerContractAcceptances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerContractAcceptances_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StreamerContractAcceptances_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerEffectActivations] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [SpecialEffectCampaignId] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        [ActivatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StreamerEffectActivations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerEffectActivations_SpecialEffectCampaigns_SpecialEffectCampaignId] FOREIGN KEY ([SpecialEffectCampaignId]) REFERENCES [SpecialEffectCampaigns] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_StreamerEffectActivations_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerFollows] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [StreamerProfileId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StreamerFollows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerFollows_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]),
        CONSTRAINT [FK_StreamerFollows_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerGoals] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [TargetAmount] decimal(18,2) NOT NULL,
        [StartingAmount] decimal(18,2) NOT NULL,
        [ManualAmount] decimal(18,2) NOT NULL,
        [ProgressBarColor] nvarchar(30) NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_StreamerGoals] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerGoals_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [StreamerStatuses] (
        [Id] int NOT NULL IDENTITY,
        [StreamerProfileId] int NOT NULL,
        [Content] nvarchar(4000) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [LikeCount] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StreamerStatuses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StreamerStatuses_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [WithdrawalRequests] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [StreamerProfileId] int NULL,
        [TransactionCode] nvarchar(100) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [BalanceBefore] decimal(18,2) NOT NULL,
        [BalanceAfter] decimal(18,2) NOT NULL,
        [BankName] nvarchar(100) NOT NULL,
        [BankAccountNumber] nvarchar(50) NOT NULL,
        [BankAccountName] nvarchar(100) NOT NULL,
        [Note] nvarchar(500) NULL,
        [Status] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        CONSTRAINT [PK_WithdrawalRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WithdrawalRequests_StreamerProfiles_StreamerProfileId] FOREIGN KEY ([StreamerProfileId]) REFERENCES [StreamerProfiles] ([Id]),
        CONSTRAINT [FK_WithdrawalRequests_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [SupportChatMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [ConversationId] int NOT NULL,
        [SenderUserId] int NULL,
        [SenderName] nvarchar(100) NOT NULL,
        [IsFromSupport] bit NOT NULL,
        [IsAi] bit NOT NULL,
        [Content] nvarchar(2000) NOT NULL,
        [IsReadBySupport] bit NOT NULL,
        [IsReadByVisitor] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SupportChatMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SupportChatMessages_SupportConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [SupportConversations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [WalletTransactionAuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [WalletTransactionId] int NOT NULL,
        [ActionType] nvarchar(50) NOT NULL,
        [ExpectedAmount] decimal(18,2) NOT NULL,
        [ReceivedAmount] decimal(18,2) NULL,
        [ReferenceCode] nvarchar(100) NULL,
        [Note] nvarchar(1000) NULL,
        [PerformedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WalletTransactionAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTransactionAuditLogs_WalletTransactions_WalletTransactionId] FOREIGN KEY ([WalletTransactionId]) REFERENCES [WalletTransactions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [TransactionAuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [DonationId] int NOT NULL,
        [TransactionCode] nvarchar(100) NOT NULL,
        [ActionType] nvarchar(50) NOT NULL,
        [OldStatus] int NOT NULL,
        [NewStatus] int NOT NULL,
        [Note] nvarchar(1000) NULL,
        [PerformedBy] nvarchar(100) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TransactionAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransactionAuditLogs_Donations_DonationId] FOREIGN KEY ([DonationId]) REFERENCES [Donations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE TABLE [ShopOrderItems] (
        [Id] int NOT NULL IDENTITY,
        [ShopOrderId] int NOT NULL,
        [ShopProductId] int NOT NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [VariantSelection] nvarchar(50) NULL,
        [UsesVariantStock] bit NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        CONSTRAINT [PK_ShopOrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ShopOrderItems_ShopOrders_ShopOrderId] FOREIGN KEY ([ShopOrderId]) REFERENCES [ShopOrders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ShopOrderItems_ShopProducts_ShopProductId] FOREIGN KEY ([ShopProductId]) REFERENCES [ShopProducts] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AlertBoxConfigs_StreamerProfileId] ON [AlertBoxConfigs] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DiscountCoupons_Code] ON [DiscountCoupons] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Donations_DonorUserId] ON [Donations] ([DonorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Donations_ExternalTransactionId] ON [Donations] ([ExternalTransactionId]) WHERE [ExternalTransactionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_Donations_StreamerProfileId] ON [Donations] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExternalLogins_Provider_ProviderKey] ON [ExternalLogins] ([Provider], [ProviderKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ExternalLogins_UserId] ON [ExternalLogins] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrderItems_ShopOrderId] ON [ShopOrderItems] ([ShopOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrderItems_ShopProductId] ON [ShopOrderItems] ([ShopProductId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrders_BuyerUserId] ON [ShopOrders] ([BuyerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrders_DiscountCouponId] ON [ShopOrders] ([DiscountCouponId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ShopOrders_OrderCode] ON [ShopOrders] ([OrderCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrders_StockReservations] ON [ShopOrders] ([PaymentMethod], [PaymentStatus], [StockReserved], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopOrders_StreamerProfileId] ON [ShopOrders] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ShopProducts_StreamerProfileId] ON [ShopProducts] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_SpecialEffectCampaigns_IsEnabled_StartsAtUtc_EndsAtUtc] ON [SpecialEffectCampaigns] ([IsEnabled], [StartsAtUtc], [EndsAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StreamerContractAcceptances_StreamerProfileId_ContractVersion] ON [StreamerContractAcceptances] ([StreamerProfileId], [ContractVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_StreamerContractAcceptances_UserId_SignedAtUtc] ON [StreamerContractAcceptances] ([UserId], [SignedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_StreamerEffectActivations_SpecialEffectCampaignId] ON [StreamerEffectActivations] ([SpecialEffectCampaignId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StreamerEffectActivations_StreamerProfileId] ON [StreamerEffectActivations] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_StreamerFollows_StreamerProfileId] ON [StreamerFollows] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StreamerFollows_UserId_StreamerProfileId] ON [StreamerFollows] ([UserId], [StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_StreamerGoals_StreamerProfileId_IsActive] ON [StreamerGoals] ([StreamerProfileId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StreamerProfiles_Slug] ON [StreamerProfiles] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StreamerProfiles_UserId] ON [StreamerProfiles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_StreamerStatuses_StreamerProfileId] ON [StreamerStatuses] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_SupportChatMessages_ConversationId_Id] ON [SupportChatMessages] ([ConversationId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_SupportConversations_GuestTokenHash] ON [SupportConversations] ([GuestTokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_SupportConversations_Status_LastMessageAt] ON [SupportConversations] ([Status], [LastMessageAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_SupportConversations_UserId_Status] ON [SupportConversations] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TransactionAuditLogs_CreatedAt] ON [TransactionAuditLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TransactionAuditLogs_DonationId] ON [TransactionAuditLogs] ([DonationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_TransactionAuditLogs_TransactionCode] ON [TransactionAuditLogs] ([TransactionCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_AccountId] ON [Users] ([AccountId]) WHERE [AccountId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_WalletTransactionAuditLogs_CreatedAt] ON [WalletTransactionAuditLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_WalletTransactionAuditLogs_WalletTransactionId] ON [WalletTransactionAuditLogs] ([WalletTransactionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WalletTransactions_OrderCode] ON [WalletTransactions] ([OrderCode]) WHERE [OrderCode] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WalletTransactions_TransactionCode] ON [WalletTransactions] ([TransactionCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_WalletTransactions_UserId_CreatedAt] ON [WalletTransactions] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_WithdrawalRequests_StreamerProfileId] ON [WithdrawalRequests] ([StreamerProfileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WithdrawalRequests_TransactionCode] ON [WithdrawalRequests] ([TransactionCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_WithdrawalRequests_UserId_CreatedAt] ON [WithdrawalRequests] ([UserId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021153531_InitialSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021153531_InitialSchema', N'10.0.12');
END;

COMMIT;
GO

