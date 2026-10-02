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
        public DbSet<Donation> Donations => Set<Donation>();
        public DbSet<TransactionAuditLog> TransactionAuditLogs => Set<TransactionAuditLog>();
        public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
        public DbSet<WalletTransactionAuditLog> WalletTransactionAuditLogs => Set<WalletTransactionAuditLog>();
        public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();

        // Các bảng thuộc phân hệ Widget & Tùy biến Streamer (OBS)
        public DbSet<AlertBoxConfig> AlertBoxConfigs => Set<AlertBoxConfig>();
        public DbSet<StreamerGoal> StreamerGoals => Set<StreamerGoal>();

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
        }
    }
}
