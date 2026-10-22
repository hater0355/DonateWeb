-- Legacy database baseline for migration 20261021153531_InitialSchema
-- Do not run before a full backup and a comparison with upgrade-idempotent.sql.
-- The 2026-10-21 schema-only export was compared against the migration: table/column
-- definitions and foreign-key behavior match. Existing extra defaults/checks/indexes
-- are preserved. This script adds only the two indexes missing from that export.
-- Do not execute the schema-only export itself; it contains CREATE USER statements.
-- This confirmation is enabled for the reviewed DonateWebDB export and verified backup.
-- Reset it to 0 before reusing this script for a different database.
-- This script adds indexes and stamps history; it does not change business row data.
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @ExpectedDatabaseName sysname = N'DonateWebDB';
IF DB_NAME() <> @ExpectedDatabaseName THROW 51006, N'Run this reviewed baseline only against DonateWebDB.', 1;
DECLARE @ConfirmSchemaReviewed BIT = 1;
IF @ConfirmSchemaReviewed <> 1 THROW 51000, N'Schema review and backup confirmation are required before baseline.', 1;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.BreakingNews', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'Content') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'LinkUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'CreatedBy') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.BreakingNews', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.DiscountCoupons', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'Code') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'Content') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'Provider') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'DiscountType') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'DiscountValue') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'ExpiresAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'UsedCount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'CreatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.DiscountCoupons', N'UpdatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.Roles', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Roles', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Roles', N'Name') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Roles', N'Description') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.SpecialEffectCampaigns', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'EffectCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'Title') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'Description') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'StartsAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'EndsAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'IsEnabled') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'CreatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SpecialEffectCampaigns', N'UpdatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.SupportAiMonthlyUsages', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportAiMonthlyUsages', N'Period') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportAiMonthlyUsages', N'TotalTokens') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportAiMonthlyUsages', N'ReservedTokens') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportAiMonthlyUsages', N'UpdatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'AccountId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'Username') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'Email') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'PasswordHash') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'FullName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'PhoneNumber') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'AvatarUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'WalletBalance') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'StreamerEarningsBalance') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Users', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.ExternalLogins', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'Provider') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'ProviderKey') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'ProviderDisplayName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ExternalLogins', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerProfiles', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'Slug') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'DisplayName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'GreetingMessage') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'Bio') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'AvatarUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'BannerUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'MinDonateAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'BankName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'BankAccountNumber') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'BankAccountName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'PaymentQrUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'YoutubeUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'TwitchUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'DiscordUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'FacebookUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'TiktokUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'IsVerified') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'LockReason') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'LockedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'FollowerCount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'Rank') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'ApprovalStatus') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'RejectionReason') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'ApprovedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'RequiredContractVersion') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'IdentityDataProtected') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'CitizenIdFrontDocumentKey') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'CitizenIdBackDocumentKey') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'IdentityDataConsentAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'TotalReceived') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerProfiles', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.SupportConversations', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'GuestTokenHash') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'VisitorName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'Status') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'AiPaused') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportConversations', N'LastMessageAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.UserRoles', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.UserRoles', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.UserRoles', N'RoleId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.WalletTransactions', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'TransactionCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'OrderCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentQrCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentCheckoutUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentBankId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentAccountNo') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentAccountName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'FailedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'CompletedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'TransactionType') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'Amount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'BalanceBefore') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'BalanceAfter') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'PaymentMethodName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'Status') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'Note') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactions', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.AlertBoxConfigs', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'WidgetToken') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'MediaType') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'MediaUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'SoundUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'SoundVolume') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'DurationSeconds') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'TextColor') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'FontFamily') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'FontSize') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'AnimationIn') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'AnimationOut') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'MessageTemplate') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'MinAmountToAlert') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'IsTtsEnabled') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.AlertBoxConfigs', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.Donations', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'DonorUserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'DonorName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'Amount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'Message') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'PaymentMethod') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'Status') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'TransactionCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'ExternalTransactionId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'SpecialEffectCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'SpecialEffectTitle') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'IsDisputed') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'DisputeReason') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'AdminNote') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'ResolvedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.Donations', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.ShopOrders', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'OrderCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'GuestAccessTokenHash') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'BuyerUserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'BuyerName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'BuyerPhone') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'BuyerAddress') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'Note') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'TotalAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'SubtotalAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'DiscountAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'ShippingFee') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'CouponCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'DiscountCouponId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'PaymentMethod') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'PaymentStatus') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'StockReserved') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'TransactionCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrders', N'PaidAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.ShopProducts', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'Name') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'Description') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'Category') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'AvailableSizesJson') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'AdditionalImageUrlsJson') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'VariantsJson') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'Price') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'WeightGrams') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'LengthCm') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'WidthCm') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'HeightCm') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'ImageUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'StockQuantity') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'ApprovalStatus') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'RejectionReason') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'ApprovedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'ApprovedBy') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopProducts', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerContractAcceptances', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'ContractVersion') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'ContractSnapshotProtected') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'ContractSnapshotSha256') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'SignerFullName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'SignedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'ClientIpAddress') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerContractAcceptances', N'UserAgent') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerEffectActivations', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'SpecialEffectCampaignId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'IsEnabled') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'ActivatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerEffectActivations', N'UpdatedAtUtc') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerFollows', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerFollows', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerFollows', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerFollows', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerFollows', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerGoals', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'Title') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'TargetAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'StartingAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'ManualAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'ProgressBarColor') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'StartDate') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'EndDate') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'IsActive') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerGoals', N'UpdatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.StreamerStatuses', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'Content') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'ImageUrl') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'LikeCount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.StreamerStatuses', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.WithdrawalRequests', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'UserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'StreamerProfileId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'TransactionCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'Amount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'BalanceBefore') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'BalanceAfter') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'BankName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'BankAccountNumber') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'BankAccountName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'Note') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'Status') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WithdrawalRequests', N'ProcessedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.SupportChatMessages', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'ConversationId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'SenderUserId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'SenderName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'IsFromSupport') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'IsAi') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'Content') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'IsReadBySupport') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'IsReadByVisitor') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.SupportChatMessages', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.WalletTransactionAuditLogs', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'WalletTransactionId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'ActionType') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'ExpectedAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'ReceivedAmount') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'ReferenceCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'Note') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'PerformedBy') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.WalletTransactionAuditLogs', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.TransactionAuditLogs', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'DonationId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'TransactionCode') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'ActionType') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'OldStatus') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'NewStatus') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'Note') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'PerformedBy') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.TransactionAuditLogs', N'CreatedAt') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF OBJECT_ID(N'dbo.ShopOrderItems', N'U') IS NULL THROW 51001, N'Legacy database is missing a required table; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'Id') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'ShopOrderId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'ShopProductId') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'ProductName') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'VariantSelection') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'UsesVariantStock') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'Price') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;
    IF COL_LENGTH(N'dbo.ShopOrderItems', N'Quantity') IS NULL THROW 51002, N'Legacy database is missing a required column; do not baseline it.', 1;

    -- Các index này có trong migration nhưng chưa có trong schema VPS đã xuất.
    -- Nếu đã có index tương đương dưới tên khác thì giữ nguyên, không tạo bản trùng.
    DECLARE @ShopOrdersObjectId int = OBJECT_ID(N'dbo.ShopOrders', N'U');
    IF EXISTS (
        SELECT 1 FROM sys.indexes AS i
        WHERE i.object_id = @ShopOrdersObjectId
          AND i.name = N'IX_ShopOrders_DiscountCouponId'
          AND NOT (
              i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0
              AND i.is_unique_constraint = 0 AND i.has_filter = 0 AND i.is_disabled = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic
                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
              AND EXISTS (
                  SELECT 1 FROM sys.index_columns AS ic
                  JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND c.name = N'DiscountCouponId'
              )
          )
    ) THROW 51004, N'Index IX_ShopOrders_DiscountCouponId exists with an unexpected definition; inspect manually.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS i
        WHERE i.object_id = @ShopOrdersObjectId
          AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0 AND i.has_filter = 0 AND i.is_disabled = 0
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1 FROM sys.index_columns AS ic
              JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1 AND c.name = N'DiscountCouponId'
          )
    )
        CREATE NONCLUSTERED INDEX IX_ShopOrders_DiscountCouponId ON dbo.ShopOrders(DiscountCouponId);

    DECLARE @StreamerEffectActivationsObjectId int = OBJECT_ID(N'dbo.StreamerEffectActivations', N'U');
    IF EXISTS (
        SELECT 1 FROM sys.indexes AS i
        WHERE i.object_id = @StreamerEffectActivationsObjectId
          AND i.name = N'IX_StreamerEffectActivations_SpecialEffectCampaignId'
          AND NOT (
              i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0
              AND i.is_unique_constraint = 0 AND i.has_filter = 0 AND i.is_disabled = 0
              AND (SELECT COUNT(*) FROM sys.index_columns AS ic
                   WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
              AND EXISTS (
                  SELECT 1 FROM sys.index_columns AS ic
                  JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND c.name = N'SpecialEffectCampaignId'
              )
          )
    ) THROW 51005, N'Index IX_StreamerEffectActivations_SpecialEffectCampaignId exists with an unexpected definition; inspect manually.', 1;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes AS i
        WHERE i.object_id = @StreamerEffectActivationsObjectId
          AND i.type = 2 AND i.is_unique = 0 AND i.is_primary_key = 0
          AND i.is_unique_constraint = 0 AND i.has_filter = 0 AND i.is_disabled = 0
          AND (SELECT COUNT(*) FROM sys.index_columns AS ic
               WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
          AND EXISTS (
              SELECT 1 FROM sys.index_columns AS ic
              JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                AND ic.key_ordinal = 1 AND c.name = N'SpecialEffectCampaignId'
          )
    )
        CREATE NONCLUSTERED INDEX IX_StreamerEffectActivations_SpecialEffectCampaignId
            ON dbo.StreamerEffectActivations(SpecialEffectCampaignId);

    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
        EXEC(N'CREATE TABLE dbo.__EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);');
    DECLARE @HasOtherMigrationHistory BIT = 0;
    EXEC sys.sp_executesql
        N'SELECT @Result = CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId <> N''20261021153531_InitialSchema'') THEN 1 ELSE 0 END;',
        N'@Result bit OUTPUT',
        @Result = @HasOtherMigrationHistory OUTPUT;
    IF @HasOtherMigrationHistory = 1 THROW 51003, N'Existing EF migration history detected; inspect it manually.', 1;
    DECLARE @HasBaseline BIT = 0;
    EXEC sys.sp_executesql
        N'SELECT @Result = CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N''20261021153531_InitialSchema'') THEN 1 ELSE 0 END;',
        N'@Result bit OUTPUT',
        @Result = @HasBaseline OUTPUT;
    IF @HasBaseline = 0
        EXEC(N'INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N''20261021153531_InitialSchema'', N''10.0.12'');');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
