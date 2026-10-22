using Microsoft.EntityFrameworkCore;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using DonateWeb.Areas.Widgets.Models;

namespace DonateWeb.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider, bool seedDemoData)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

            await DatabaseSchemaGuard.EnsureMigrationsAppliedAsync(context);

            try
            {
                // Schema được quản lý bằng EF Core migrations; ứng dụng chỉ seed dữ liệu sau khi đã kiểm tra migration history.

                // Roles are required in every environment; demo accounts and content are development-only.
                if (!await context.Roles.AnyAsync())
                {
                    var roles = new List<Role>
                    {
                        new Role { Name = UserRoles.Admin, Description = "Quản trị viên toàn hệ thống" },
                        new Role { Name = UserRoles.Streamer, Description = "Nhà sáng tạo nội dung / Streamer nhận donate" },
                        new Role { Name = UserRoles.Viewer, Description = "Người xem, ủng hộ và nạp ví" },
                        new Role { Name = UserRoles.SupportAgent, Description = "Nhân viên hỗ trợ người dùng qua hộp thư trực tuyến" }
                    };

                    await context.Roles.AddRangeAsync(roles);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Seeded roles: Admin, Streamer, Viewer successfully.");
                }

                // CSDL production đã có roles từ trước: bổ sung role hỗ trợ mà không phụ thuộc seed demo.
                if (!await context.Roles.AnyAsync(r => r.Name == UserRoles.SupportAgent))
                {
                    context.Roles.Add(new Role { Name = UserRoles.SupportAgent, Description = "Nhân viên hỗ trợ người dùng qua hộp thư trực tuyến" });
                    await context.SaveChangesAsync();
                }

                if (!seedDemoData)
                    logger.LogInformation("Database roles initialized; demo data seeding is disabled.");

                if (seedDemoData)
                {
                var accountIdService = scope.ServiceProvider.GetRequiredService<IAccountIdService>();
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
                    bool isStreamer = u.StreamerProfile?.ApprovalStatus == StreamerApprovalStatus.Approved ||
                        u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == UserRoles.Streamer);
                    u.AccountId = await accountIdService.GenerateAccountIdAsync(isStreamer, u.CreatedAt.Year);
                }

                // 5.2. Đảm bảo nếu tài khoản đã là Streamer mà ID vẫn còn bắt đầu bằng 9 (do nâng cấp trước đó) thì đổi thành 1
                var streamersWithViewerId = await context.Users
                    .Include(u => u.StreamerProfile)
                    .Where(u => u.StreamerProfile != null &&
                        u.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved &&
                        u.AccountId != null && u.AccountId.StartsWith("9"))
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
                }

                // Refresh the reporting total without deriving or overwriting spendable wallet balances.
                var allStreamerProfiles = await context.StreamerProfiles
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

                }
                await context.SaveChangesAsync();

                // 8. Seed Cấu hình Alert Box mặc định cho Streamer 'tenstreamer' (không seed Goal ảo)
                var tenstreamerProfile = await context.StreamerProfiles
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.Slug == "tenstreamer" || (s.User != null && s.User.Username == "tenstreamer"));
                if (seedDemoData && tenstreamerProfile != null)
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
                if (seedDemoData && !await context.ShopProducts.AnyAsync())
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
                if (seedDemoData && !await context.StreamerStatuses.AnyAsync())
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
                if (!seedDemoData)
                {
                    throw;
                }
            }
        }

    }
}
