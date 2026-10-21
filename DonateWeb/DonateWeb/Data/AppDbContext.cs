using Microsoft.EntityFrameworkCore;
using DonateWeb.Models.Entities;
using DonateWeb.Areas.Widgets.Models;

namespace DonateWeb.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
        public DbSet<StreamerProfile> StreamerProfiles => Set<StreamerProfile>();
        public DbSet<StreamerContractAcceptance> StreamerContractAcceptances => Set<StreamerContractAcceptance>();
        public DbSet<Donation> Donations => Set<Donation>();
        public DbSet<TransactionAuditLog> TransactionAuditLogs => Set<TransactionAuditLog>();
        public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
        public DbSet<WalletTransactionAuditLog> WalletTransactionAuditLogs => Set<WalletTransactionAuditLog>();
        public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();

        // Các bảng thuộc phân hệ Widget & Tùy biến Streamer (OBS)
        public DbSet<AlertBoxConfig> AlertBoxConfigs => Set<AlertBoxConfig>();
        public DbSet<StreamerGoal> StreamerGoals => Set<StreamerGoal>();
        public DbSet<SpecialEffectCampaign> SpecialEffectCampaigns => Set<SpecialEffectCampaign>();
        public DbSet<StreamerEffectActivation> StreamerEffectActivations => Set<StreamerEffectActivation>();

        // Phân hệ Gian hàng & Giỏ hàng Streamer
        public DbSet<ShopProduct> ShopProducts => Set<ShopProduct>();
        public DbSet<ShopOrder> ShopOrders => Set<ShopOrder>();
        public DbSet<ShopOrderItem> ShopOrderItems => Set<ShopOrderItem>();
        public DbSet<DiscountCoupon> DiscountCoupons => Set<DiscountCoupon>();

        // Bản tin Breaking News
        public DbSet<BreakingNews> BreakingNews => Set<BreakingNews>();

        // Danh sách theo dõi Streamer (Streamer yêu thích)
        public DbSet<StreamerFollow> StreamerFollows => Set<StreamerFollow>();

        // Bài viết Status của Streamer
        public DbSet<StreamerStatus> StreamerStatuses => Set<StreamerStatus>();

        // Hộp thư hỗ trợ trực tuyến và lịch sử hội thoại.
        public DbSet<SupportConversation> SupportConversations => Set<SupportConversation>();
        public DbSet<SupportChatMessage> SupportChatMessages => Set<SupportChatMessage>();
        public DbSet<SupportAiMonthlyUsage> SupportAiMonthlyUsages => Set<SupportAiMonthlyUsage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User - Unique Constraints
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.AccountId)
                      .IsUnique()
                      .HasFilter("[AccountId] IS NOT NULL");
            });

            // Role - Unique Name
            modelBuilder.Entity<Role>(entity =>
            {
                entity.HasIndex(r => r.Name).IsUnique();
            });

            // UserRole - Composite Primary Key
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.HasKey(ur => new { ur.UserId, ur.RoleId });

                entity.HasOne(ur => ur.User)
                      .WithMany(u => u.UserRoles)
                      .HasForeignKey(ur => ur.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ur => ur.Role)
                      .WithMany(r => r.UserRoles)
                      .HasForeignKey(ur => ur.RoleId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SupportConversation>(entity =>
            {
                entity.HasIndex(c => new { c.Status, c.LastMessageAt });
                entity.HasIndex(c => new { c.UserId, c.Status });
                entity.HasIndex(c => c.GuestTokenHash);
                entity.HasOne(c => c.User)
                    .WithMany()
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<SupportChatMessage>(entity =>
            {
                entity.HasIndex(m => new { m.ConversationId, m.Id });
                entity.HasOne(m => m.Conversation)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(m => m.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SupportAiMonthlyUsage>(entity =>
            {
                entity.HasKey(usage => usage.Period);
                entity.Property(usage => usage.Period).HasMaxLength(7);
            });

            // StreamerProfile - Unique Slug & 1-to-1 with User
            modelBuilder.Entity<StreamerProfile>(entity =>
            {
                entity.HasIndex(sp => sp.Slug).IsUnique();
                entity.HasIndex(sp => sp.UserId).IsUnique();

                entity.HasOne(sp => sp.User)
                      .WithOne(u => u.StreamerProfile)
                      .HasForeignKey<StreamerProfile>(sp => sp.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StreamerContractAcceptance>(entity =>
            {
                entity.HasIndex(a => new { a.StreamerProfileId, a.ContractVersion }).IsUnique();
                entity.HasIndex(a => new { a.UserId, a.SignedAtUtc });
                entity.HasOne<StreamerProfile>()
                    .WithMany()
                    .HasForeignKey(a => a.StreamerProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // ExternalLogin - Foreign key & Index
            modelBuilder.Entity<ExternalLogin>(entity =>
            {
                entity.HasIndex(el => new { el.Provider, el.ProviderKey }).IsUnique();

                entity.HasOne(el => el.User)
                      .WithMany(u => u.ExternalLogins)
                      .HasForeignKey(el => el.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Donation - Foreign keys
            modelBuilder.Entity<Donation>(entity =>
            {
                entity.HasIndex(d => d.ExternalTransactionId)
                      .IsUnique()
                      .HasFilter("[ExternalTransactionId] IS NOT NULL");

                entity.HasOne(d => d.StreamerProfile)
                      .WithMany(sp => sp.DonationsReceived)
                      .HasForeignKey(d => d.StreamerProfileId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.DonorUser)
                      .WithMany(u => u.DonationsSent)
                      .HasForeignKey(d => d.DonorUserId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // TransactionAuditLog - Foreign key với Donation
            modelBuilder.Entity<TransactionAuditLog>(entity =>
            {
                entity.HasOne(al => al.Donation)
                      .WithMany(d => d.AuditLogs)
                      .HasForeignKey(al => al.DonationId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(al => al.TransactionCode);
                entity.HasIndex(al => al.CreatedAt);
            });

            // WalletTransaction - Foreign key với User (Lịch sử Nạp tiền / Rút tiền)
            modelBuilder.Entity<WalletTransaction>(entity =>
            {
                entity.HasOne(wt => wt.User)
                      .WithMany(u => u.WalletTransactions)
                      .HasForeignKey(wt => wt.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(wt => wt.TransactionCode).IsUnique();
                entity.HasIndex(wt => wt.OrderCode).IsUnique().HasFilter("[OrderCode] IS NOT NULL");
                entity.HasIndex(wt => new { wt.UserId, wt.CreatedAt });
            });

            modelBuilder.Entity<WalletTransactionAuditLog>(entity =>
            {
                entity.HasOne(log => log.WalletTransaction)
                      .WithMany(transaction => transaction.AuditLogs)
                      .HasForeignKey(log => log.WalletTransactionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(log => log.WalletTransactionId);
                entity.HasIndex(log => log.CreatedAt);
            });

            // WithdrawalRequest - Foreign key với User & StreamerProfile (Yêu cầu rút tiền của Streamer)
            modelBuilder.Entity<WithdrawalRequest>(entity =>
            {
                entity.HasOne(wr => wr.User)
                      .WithMany(u => u.WithdrawalRequests)
                      .HasForeignKey(wr => wr.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(wr => wr.StreamerProfile)
                      .WithMany()
                      .HasForeignKey(wr => wr.StreamerProfileId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(wr => wr.TransactionCode).IsUnique();
                entity.HasIndex(wr => new { wr.UserId, wr.CreatedAt });
            });

            // AlertBoxConfig - Foreign key với StreamerProfile (1 - 1)
            modelBuilder.Entity<AlertBoxConfig>(entity =>
            {
                entity.HasOne(abc => abc.StreamerProfile)
                      .WithMany()
                      .HasForeignKey(abc => abc.StreamerProfileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(abc => abc.StreamerProfileId).IsUnique();
            });

            // StreamerGoal - Foreign key với StreamerProfile (1 - N)
            modelBuilder.Entity<StreamerGoal>(entity =>
            {
                entity.HasOne(sg => sg.StreamerProfile)
                      .WithMany()
                      .HasForeignKey(sg => sg.StreamerProfileId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(sg => new { sg.StreamerProfileId, sg.IsActive });
            });

            modelBuilder.Entity<SpecialEffectCampaign>(entity =>
            {
                entity.Property(campaign => campaign.EffectCode).HasMaxLength(50).IsRequired();
                entity.Property(campaign => campaign.Title).HasMaxLength(120).IsRequired();
                entity.Property(campaign => campaign.Description).HasMaxLength(500);
                entity.HasIndex(campaign => new { campaign.IsEnabled, campaign.StartsAtUtc, campaign.EndsAtUtc });
            });

            modelBuilder.Entity<StreamerEffectActivation>(entity =>
            {
                entity.HasIndex(activation => activation.StreamerProfileId).IsUnique();
                entity.HasOne(activation => activation.StreamerProfile)
                    .WithMany()
                    .HasForeignKey(activation => activation.StreamerProfileId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(activation => activation.Campaign)
                    .WithMany(campaign => campaign.Activations)
                    .HasForeignKey(activation => activation.SpecialEffectCampaignId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ShopProduct
            modelBuilder.Entity<ShopProduct>(entity =>
            {
                entity.Property(product => product.VariantsJson).HasColumnType("nvarchar(max)").IsRequired();
                entity.HasOne(p => p.StreamerProfile)
                      .WithMany(s => s.ShopProducts)
                      .HasForeignKey(p => p.StreamerProfileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ShopOrder
            modelBuilder.Entity<ShopOrder>(entity =>
            {
                entity.HasOne(o => o.StreamerProfile)
                      .WithMany(s => s.ShopOrders)
                      .HasForeignKey(o => o.StreamerProfileId)
                      .OnDelete(DeleteBehavior.NoAction);

                entity.HasOne(o => o.BuyerUser)
                      .WithMany()
                      .HasForeignKey(o => o.BuyerUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(o => o.OrderCode).IsUnique();
                entity.HasIndex(o => o.DiscountCouponId);
                entity.HasIndex(o => new { o.PaymentMethod, o.PaymentStatus, o.StockReserved, o.CreatedAt })
                      .HasDatabaseName("IX_ShopOrders_StockReservations");

                entity.HasOne(o => o.DiscountCoupon)
                      .WithMany(coupon => coupon.ShopOrders)
                      .HasForeignKey(o => o.DiscountCouponId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<DiscountCoupon>(entity =>
            {
                entity.HasIndex(coupon => coupon.Code).IsUnique();
                entity.Property(coupon => coupon.DiscountValue).HasPrecision(18, 2);
                entity.Property(coupon => coupon.DiscountType).HasMaxLength(20).IsRequired();
            });

            // ShopOrderItem
            modelBuilder.Entity<ShopOrderItem>(entity =>
            {
                entity.HasOne(i => i.ShopOrder)
                      .WithMany(o => o.Items)
                      .HasForeignKey(i => i.ShopOrderId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(i => i.ShopProduct)
                      .WithMany(p => p.OrderItems)
                      .HasForeignKey(i => i.ShopProductId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // StreamerFollow (Mối quan hệ theo dõi Streamer - Streamer yêu thích)
            modelBuilder.Entity<StreamerFollow>(entity =>
            {
                entity.HasKey(sf => sf.Id);
                entity.HasIndex(sf => new { sf.UserId, sf.StreamerProfileId }).IsUnique();

                entity.HasOne(sf => sf.User)
                      .WithMany(u => u.FollowedStreamers)
                      .HasForeignKey(sf => sf.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(sf => sf.StreamerProfile)
                      .WithMany(sp => sp.Followers)
                      .HasForeignKey(sf => sf.StreamerProfileId)
                      .OnDelete(DeleteBehavior.NoAction);
            });

            // StreamerStatus (Bài viết Status của Streamer)
            modelBuilder.Entity<StreamerStatus>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.Content).IsRequired().HasMaxLength(4000);
                entity.Property(s => s.ImageUrl).HasMaxLength(500);

                entity.HasOne(s => s.StreamerProfile)
                      .WithMany(sp => sp.Statuses)
                      .HasForeignKey(s => s.StreamerProfileId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
