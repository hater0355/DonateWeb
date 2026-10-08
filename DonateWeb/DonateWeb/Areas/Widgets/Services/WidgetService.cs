using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Areas.Widgets.Models;
using DonateWeb.Areas.Widgets.ViewModels;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.ContentModeration;

namespace DonateWeb.Areas.Widgets.Services
{
    /// <summary>
    /// SERVICE XỬ LÝ TOÀN BỘ NGHIỆP VỤ WIDGET OBS CHO STREAMER
    /// Thư mục riêng: Areas/Widgets/Services/
    /// Bao gồm:
    /// 1. Quản lý cấu hình Alert Box (ảnh GIF, video WebM/MP4, âm thanh, phông chữ, màu sắc, animation, TTS)
    /// 2. Quản lý Mục tiêu quyên góp (Goal): Tự động tính toán tiến độ thanh chạy mục tiêu (ví dụ: "Mua PC mới: 5.000.000đ / 10.000.000đ")
    /// 3. Báo cáo & Thống kê: Top Donate (ngày/tuần/tháng/toàn thời gian), tin nhắn gần nhất
    /// 4. Test Alert & Polling thời gian thực phục vụ Browser Source của OBS Studio
    /// 5. TÍCH HỢP BỘ LỌC NỘI DUNG (CONTENT MODERATION): Đảm bảo tin nhắn donate hiển thị trên màn hình stream luôn được lọc sạch từ ngữ tục tĩu và link độc hại.
    /// </summary>
    public class WidgetService : IWidgetService
    {
        private readonly AppDbContext _context;
        private readonly IContentModerationService _moderationService;
        private readonly ILogger<WidgetService> _logger;

        // Bộ nhớ đệm (Cache in-memory) cấu hình Alert Box theo slug streamer để tối ưu tốc độ polling 2s/lần của OBS
        private static readonly ConcurrentDictionary<string, AlertBoxConfigViewModel> _alertConfigsCache = new();

        // Hàng đợi phát Alert thử nghiệm (Test Alert) trên OBS Browser Source
        private static readonly ConcurrentDictionary<string, ConcurrentQueue<AlertPollResultDto>> _testAlertQueues = new();

        public WidgetService(
            AppDbContext context,
            IContentModerationService moderationService,
            ILogger<WidgetService> logger)
        {
            _context = context;
            _moderationService = moderationService;
            _logger = logger;
        }

        // ====================================================================
        // 1. CÀI ĐẶT ALERT BOX (LƯU CẤU HÌNH HIỂN THỊ OBS)
        // ====================================================================

        private async Task<StreamerProfile?> FindStreamerProfileAsync(string cleanSlug)
        {
            return await _context.StreamerProfiles
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug || 
                                          (p.User != null && p.User.AccountId != null && p.User.AccountId.ToLower() == cleanSlug) || 
                                          (p.User != null && p.User.Username.ToLower() == cleanSlug));
        }

        /// <summary>
        /// Lấy cấu hình Alert Box của Streamer. Ưu tiên tải từ CSDL SQL Server,
        /// nếu chưa có sẽ tự động khởi tạo cấu hình mặc định và lưu vào CSDL.
        /// </summary>
        public async Task<AlertBoxConfigViewModel> GetAlertBoxConfigAsync(string streamerSlug, string baseUrl)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await FindStreamerProfileAsync(cleanSlug);

            var overlayUrl = $"{baseUrl.TrimEnd('/')}/Widgets/Overlay/AlertBox/{cleanSlug}";

            // 1. Nếu có trong Cache in-memory thì cập nhật URL overlay và trả về nhanh
            if (_alertConfigsCache.TryGetValue(cleanSlug, out var cachedConfig))
            {
                cachedConfig.ObsOverlayUrl = overlayUrl;
                if (profile != null)
                {
                    cachedConfig.StreamerProfileId = profile.Id;
                    cachedConfig.DisplayName = profile.DisplayName;
                }
                return cachedConfig;
            }

            // 2. Nếu chưa có trong Cache, truy vấn từ CSDL SQL Server (bảng AlertBoxConfigs)
            AlertBoxConfig? entity = null;
            if (profile != null)
            {
                entity = await _context.AlertBoxConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.StreamerProfileId == profile.Id);
            }

            AlertBoxConfigViewModel model;

            if (entity != null)
            {
                model = new AlertBoxConfigViewModel
                {
                    StreamerProfileId = entity.StreamerProfileId,
                    StreamerSlug = cleanSlug,
                    DisplayName = profile?.DisplayName ?? "Streamer",
                    WidgetToken = entity.WidgetToken,
                    ObsOverlayUrl = overlayUrl,
                    MediaType = entity.MediaType ?? "image",
                    ImageUrl = entity.MediaUrl,
                    SoundUrl = entity.SoundUrl,
                    SoundVolume = entity.SoundVolume,
                    DurationSeconds = entity.DurationSeconds,
                    TextColor = entity.TextColor,
                    FontFamily = entity.FontFamily,
                    FontSize = entity.FontSize,
                    AnimationIn = entity.AnimationIn,
                    AnimationOut = entity.AnimationOut,
                    MessageTemplate = entity.MessageTemplate,
                    MinAmountToAlert = entity.MinAmountToAlert,
                    IsTtsEnabled = entity.IsTtsEnabled
                };
            }
            else
            {
                // Cấu hình mặc định nếu streamer chưa từng lưu
                model = new AlertBoxConfigViewModel
                {
                    StreamerProfileId = profile?.Id ?? 0,
                    StreamerSlug = cleanSlug,
                    DisplayName = profile?.DisplayName ?? "Streamer",
                    WidgetToken = Guid.NewGuid().ToString("N"),
                    ObsOverlayUrl = overlayUrl,
                    MediaType = "image",
                    ImageUrl = "https://media.giphy.com/media/l41lFw057lAJQMwg0/giphy.gif",
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
                    IsTtsEnabled = true
                };
            }

            _alertConfigsCache[cleanSlug] = model;
            return model;
        }

        /// <summary>
        /// Lưu cấu hình Alert Box vào CSDL SQL Server và đồng bộ lại Cache
        /// </summary>
        public async Task<bool> SaveAlertBoxConfigAsync(AlertBoxConfigViewModel model)
        {
            var cleanSlug = (model.StreamerSlug ?? "tenstreamer").Trim().ToLower();
            model.StreamerSlug = cleanSlug;

            var profile = await FindStreamerProfileAsync(cleanSlug);

            if (profile == null)
            {
                _logger.LogWarning("Không tìm thấy profile streamer cho slug {Slug}", cleanSlug);
                return false;
            }

            // Tự động phát hiện MediaType nếu người dùng dán link video WebM/MP4
            var mediaUrl = (model.ImageUrl ?? "").Trim();
            if (mediaUrl.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                mediaUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                model.MediaType = "video";
            }
            else if (string.IsNullOrWhiteSpace(model.MediaType))
            {
                model.MediaType = "image";
            }

            var entity = await _context.AlertBoxConfigs
                .FirstOrDefaultAsync(a => a.StreamerProfileId == profile.Id);

            if (entity == null)
            {
                entity = new AlertBoxConfig
                {
                    StreamerProfileId = profile.Id,
                    WidgetToken = string.IsNullOrWhiteSpace(model.WidgetToken) ? Guid.NewGuid().ToString("N") : model.WidgetToken,
                    CreatedAt = DateTime.UtcNow
                };
                _context.AlertBoxConfigs.Add(entity);
            }

            // Cập nhật các trường cấu hình
            entity.MediaType = model.MediaType;
            entity.MediaUrl = mediaUrl;
            entity.SoundUrl = model.SoundUrl?.Trim() ?? "";
            entity.SoundVolume = model.SoundVolume;
            entity.DurationSeconds = model.DurationSeconds;
            entity.TextColor = model.TextColor?.Trim() ?? "#10b981";
            entity.FontFamily = model.FontFamily?.Trim() ?? "Inter";
            entity.FontSize = model.FontSize;
            entity.AnimationIn = model.AnimationIn?.Trim() ?? "fadeInDown";
            entity.AnimationOut = model.AnimationOut?.Trim() ?? "fadeOutUp";
            entity.MessageTemplate = model.MessageTemplate?.Trim() ?? "{donor} vừa ủng hộ {amount} VNĐ!";
            entity.MinAmountToAlert = model.MinAmountToAlert;
            entity.IsTtsEnabled = model.IsTtsEnabled;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            model.WidgetToken = entity.WidgetToken;

            // Cập nhật Cache in-memory
            model.StreamerProfileId = profile.Id;
            model.DisplayName = profile.DisplayName;
            _alertConfigsCache[cleanSlug] = model;

            return true;
        }

        /// <summary>
        /// Khôi phục cấu hình Alert Box về mặc định của hệ thống
        /// </summary>
        public async Task ResetAlertBoxConfigAsync(string streamerSlug)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            _alertConfigsCache.TryRemove(cleanSlug, out _);

            var profile = await _context.StreamerProfiles.FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug);
            if (profile != null)
            {
                var entity = await _context.AlertBoxConfigs.FirstOrDefaultAsync(a => a.StreamerProfileId == profile.Id);
                if (entity != null)
                {
                    _context.AlertBoxConfigs.Remove(entity);
                    await _context.SaveChangesAsync();
                }
            }
        }

        // ====================================================================
        // 2. QUẢN LÝ GOAL (MỤC TIÊU QUYÊN GÓP VÀ TÍNH TOÁN TIẾN ĐỘ THANH CHẠY)
        // ====================================================================

        /// <summary>
        /// Lấy toàn bộ danh sách Mục tiêu quyên góp của streamer, đồng thời tính toán
        /// tiến độ thực tế dựa trên số tiền khởi điểm + các khoản donate thành công từ lúc bắt đầu mục tiêu.
        /// Ví dụ: "Mua PC mới: 5.000.000đ / 10.000.000đ (50%)"
        /// </summary>
        public async Task<GoalManagementViewModel> GetGoalManagementAsync(string streamerSlug, string baseUrl)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await FindStreamerProfileAsync(cleanSlug);

            var overlayUrl = $"{baseUrl.TrimEnd('/')}/Widgets/Overlay/Goal/{cleanSlug}";

            if (profile == null)
            {
                return new GoalManagementViewModel { ObsOverlayUrl = overlayUrl };
            }

            // Lấy danh sách các mục tiêu từ CSDL
            var goalEntities = await _context.StreamerGoals
                .Where(g => g.StreamerProfileId == profile.Id)
                .OrderByDescending(g => g.IsActive)
                .ThenByDescending(g => g.Id)
                .ToListAsync();

            var goalViewModels = new List<StreamerGoalViewModel>();

            foreach (var g in goalEntities)
            {
                // TÍNH TOÁN TIẾN ĐỘ: Số tiền khởi điểm + Tổng các khoản donate thành công trong khoảng thời gian mục tiêu
                var donatedSum = await _context.Donations
                    .AsNoTracking()
                    .Where(d => d.StreamerProfileId == profile.Id
                             && d.Status == DonationStatus.Success
                             && d.CreatedAt >= g.StartDate
                             && (!g.EndDate.HasValue || d.CreatedAt <= g.EndDate.Value))
                    .SumAsync(d => (decimal?)d.Amount) ?? 0;

                var totalCurrent = g.StartingAmount + g.ManualAmount + donatedSum;

                goalViewModels.Add(new StreamerGoalViewModel
                {
                    Id = g.Id,
                    StreamerProfileId = g.StreamerProfileId,
                    Title = g.Title,
                    TargetAmount = g.TargetAmount,
                    StartingAmount = g.StartingAmount,
                    CurrentAmount = totalCurrent,
                    ProgressBarColor = g.ProgressBarColor,
                    StartDate = g.StartDate,
                    EndDate = g.EndDate,
                    IsActive = g.IsActive
                });
            }

            var activeGoal = goalViewModels.FirstOrDefault(g => g.IsActive);

            return new GoalManagementViewModel
            {
                ObsOverlayUrl = overlayUrl,
                ActiveGoal = activeGoal,
                NewGoal = new StreamerGoalViewModel
                {
                    StreamerProfileId = profile.Id,
                    Title = string.Empty,
                    TargetAmount = 0,
                    StartingAmount = 0,
                    CurrentAmount = 0,
                    ProgressBarColor = "#10b981",
                    IsActive = true
                },
                Goals = goalViewModels
            };
        }

        /// <summary>
        /// Lấy mục tiêu đang kích hoạt (IsActive) để phát lên OBS Overlay
        /// Tự động tính toán tiến độ thanh chạy thời gian thực:
        /// "Mua PC mới: 5.000.000đ / 10.000.000đ (50%)"
        /// </summary>
        public async Task<StreamerGoalViewModel?> GetActiveGoalAsync(string streamerSlug)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await _context.StreamerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug);

            if (profile == null) return null;

            var activeEntity = await _context.StreamerGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.StreamerProfileId == profile.Id && g.IsActive);

            if (activeEntity == null) return null;

            // Tính toán tiến độ từ các khoản donate thành công thực tế
            var donatedSum = await _context.Donations
                .AsNoTracking()
                .Where(d => d.StreamerProfileId == profile.Id
                         && d.Status == DonationStatus.Success
                         && d.CreatedAt >= activeEntity.StartDate
                         && (!activeEntity.EndDate.HasValue || d.CreatedAt <= activeEntity.EndDate.Value))
                .SumAsync(d => (decimal?)d.Amount) ?? 0;

            var totalCurrent = activeEntity.StartingAmount + activeEntity.ManualAmount + donatedSum;

            return new StreamerGoalViewModel
            {
                Id = activeEntity.Id,
                StreamerProfileId = activeEntity.StreamerProfileId,
                Title = activeEntity.Title,
                TargetAmount = activeEntity.TargetAmount,
                StartingAmount = activeEntity.StartingAmount,
                CurrentAmount = totalCurrent,
                ProgressBarColor = activeEntity.ProgressBarColor,
                StartDate = activeEntity.StartDate,
                EndDate = activeEntity.EndDate,
                IsActive = activeEntity.IsActive
            };
        }

        /// <summary>
        /// Tạo mới hoặc cập nhật một mục tiêu quyên góp vào CSDL
        /// </summary>
        public async Task<bool> SaveGoalAsync(string streamerSlug, StreamerGoalViewModel model)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await _context.StreamerProfiles.FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug);
            if (profile == null) return false;

            if (model.Id > 0)
            {
                // Cập nhật mục tiêu đã có
                var existing = await _context.StreamerGoals
                    .FirstOrDefaultAsync(g => g.Id == model.Id && g.StreamerProfileId == profile.Id);

                if (existing != null)
                {
                    existing.Title = model.Title.Trim();
                    existing.TargetAmount = model.TargetAmount;
                    existing.StartingAmount = model.StartingAmount;
                    existing.ProgressBarColor = model.ProgressBarColor?.Trim() ?? "#10b981";
                    existing.EndDate = model.EndDate;
                    existing.UpdatedAt = DateTime.UtcNow;

                    if (model.IsActive && !existing.IsActive)
                    {
                        // Tắt các mục tiêu khác của streamer để chỉ có 1 mục tiêu active
                        var otherGoals = await _context.StreamerGoals
                            .Where(g => g.StreamerProfileId == profile.Id && g.Id != existing.Id)
                            .ToListAsync();
                        foreach (var og in otherGoals) og.IsActive = false;

                        existing.IsActive = true;
                    }
                    else if (!model.IsActive && existing.IsActive)
                    {
                        existing.IsActive = false;
                    }

                    await _context.SaveChangesAsync();
                    return true;
                }
            }

            // Tạo mục tiêu mới
            if (model.IsActive)
            {
                var otherGoals = await _context.StreamerGoals
                    .Where(g => g.StreamerProfileId == profile.Id)
                    .ToListAsync();
                foreach (var og in otherGoals) og.IsActive = false;
            }

            var newGoal = new StreamerGoal
            {
                StreamerProfileId = profile.Id,
                Title = model.Title.Trim(),
                TargetAmount = model.TargetAmount,
                StartingAmount = model.StartingAmount,
                ManualAmount = 0,
                ProgressBarColor = model.ProgressBarColor?.Trim() ?? "#10b981",
                StartDate = DateTime.UtcNow,
                EndDate = model.EndDate,
                IsActive = model.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.StreamerGoals.Add(newGoal);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Bật/Tắt trạng thái kích hoạt của mục tiêu trên OBS
        /// </summary>
        public async Task<bool> ToggleGoalAsync(string streamerSlug, int goalId)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await _context.StreamerProfiles.FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug);
            if (profile == null) return false;

            var target = await _context.StreamerGoals
                .FirstOrDefaultAsync(g => g.Id == goalId && g.StreamerProfileId == profile.Id);

            if (target == null) return false;

            var newStatus = !target.IsActive;
            if (newStatus)
            {
                // Nếu bật, tắt các goal khác
                var otherGoals = await _context.StreamerGoals
                    .Where(g => g.StreamerProfileId == profile.Id && g.Id != goalId)
                    .ToListAsync();
                foreach (var og in otherGoals) og.IsActive = false;
            }

            target.IsActive = newStatus;
            target.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Xóa vĩnh viễn một mục tiêu quyên góp
        /// </summary>
        public async Task<bool> DeleteGoalAsync(string streamerSlug, int goalId)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await _context.StreamerProfiles.FirstOrDefaultAsync(p => p.Slug.ToLower() == cleanSlug);
            if (profile == null) return false;

            var target = await _context.StreamerGoals
                .FirstOrDefaultAsync(g => g.Id == goalId && g.StreamerProfileId == profile.Id);

            if (target == null) return false;

            _context.StreamerGoals.Remove(target);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Khởi tạo dữ liệu mẫu nếu bảng StreamerGoals chưa có dữ liệu cho streamer
        /// </summary>
        private async Task EnsureDefaultGoalsInDbAsync(int streamerProfileId)
        {
            if (streamerProfileId <= 0) return;

            if (!await _context.StreamerGoals.AnyAsync(g => g.StreamerProfileId == streamerProfileId))
            {
                var defaultGoals = new List<StreamerGoal>
                {
                    new StreamerGoal
                    {
                        StreamerProfileId = streamerProfileId,
                        Title = "Mua PC mới",
                        TargetAmount = 10000000,
                        StartingAmount = 5000000,
                        ManualAmount = 0,
                        ProgressBarColor = "#10b981",
                        StartDate = DateTime.UtcNow.AddDays(-10),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new StreamerGoal
                    {
                        StreamerProfileId = streamerProfileId,
                        Title = "Nâng cấp Microphone & Sound Card",
                        TargetAmount = 5000000,
                        StartingAmount = 1500000,
                        ManualAmount = 0,
                        ProgressBarColor = "#3b82f6",
                        StartDate = DateTime.UtcNow.AddDays(-20),
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow
                    }
                };
                _context.StreamerGoals.AddRange(defaultGoals);
                await _context.SaveChangesAsync();
            }
        }

        // ====================================================================
        // 3. BÁO CÁO & THỐNG KÊ: BẢNG XẾP HẠNG TOP DONATE & TIN NHẮN GẦN NHẤT
        // ====================================================================

        /// <summary>
        /// Lấy ViewModel tổng thể cho trang Báo cáo Top Donate và Tin nhắn gần nhất
        /// </summary>
        public async Task<LeaderboardViewModel> GetLeaderboardViewModelAsync(string streamerSlug, string period, string baseUrl)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var p = string.IsNullOrWhiteSpace(period) ? "all" : period.Trim().ToLower();

            var topDonors = await GetLeaderboardDonorsAsync(cleanSlug, p, 10);
            var recentDonations = await GetRecentDonationsAsync(cleanSlug, 10);

            var overlayUrl = $"{baseUrl.TrimEnd('/')}/Widgets/Overlay/Leaderboard/{cleanSlug}?period={p}";

            return new LeaderboardViewModel
            {
                StreamerSlug = cleanSlug,
                ObsOverlayUrl = overlayUrl,
                Period = p,
                TopDonors = topDonors,
                RecentDonations = recentDonations
            };
        }

        /// <summary>
        /// Lấy danh sách bảng xếp hạng Top Donate theo chu kỳ:
        /// - "day": Hôm nay (tính từ 00:00:00 UTC)
        /// - "week": Tuần này (7 ngày gần nhất)
        /// - "month": Tháng này (30 ngày gần nhất)
        /// - "all": Toàn thời gian
        /// </summary>
        public async Task<List<LeaderboardDonorDto>> GetLeaderboardDonorsAsync(string streamerSlug, string period, int limit)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await FindStreamerProfileAsync(cleanSlug);
            var profileId = profile?.Id ?? 0;
            var query = _context.Donations
                .AsNoTracking()
                .Where(d => (d.StreamerProfileId == profileId || d.StreamerProfile.Slug.ToLower() == cleanSlug) && d.Status == DonationStatus.Success);

            var now = DateTime.UtcNow;
            switch (period?.ToLower())
            {
                case "day":
                    var startOfDay = now.Date;
                    query = query.Where(d => d.CreatedAt >= startOfDay);
                    break;
                case "week":
                    var startOfWeek = now.Date.AddDays(-7);
                    query = query.Where(d => d.CreatedAt >= startOfWeek);
                    break;
                case "month":
                    var startOfMonth = now.Date.AddDays(-30);
                    query = query.Where(d => d.CreatedAt >= startOfMonth);
                    break;
            }

            var grouped = await query
                .GroupBy(d => d.DonorName)
                .Select(g => new
                {
                    DonorName = g.Key,
                    Count = g.Count(),
                    Total = g.Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.Total)
                .Take(limit > 0 ? limit : 10)
                .ToListAsync();

            var result = new List<LeaderboardDonorDto>();
            int rank = 1;
            foreach (var item in grouped)
            {
                result.Add(new LeaderboardDonorDto
                {
                    Rank = rank++,
                    DonorName = item.DonorName,
                    DonationCount = item.Count,
                    TotalAmount = item.Total
                });
            }

            return result;
        }

        /// <summary>
        /// Lấy danh sách các khoản quyên góp và tin nhắn donate mới nhất để hiển thị widget trên stream
        /// </summary>
        public async Task<List<RecentDonationDto>> GetRecentDonationsAsync(string streamerSlug, int limit)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var profile = await FindStreamerProfileAsync(cleanSlug);
            var profileId = profile?.Id ?? 0;
            var donations = await _context.Donations
                .AsNoTracking()
                .Where(d => (d.StreamerProfileId == profileId || d.StreamerProfile.Slug.ToLower() == cleanSlug) && d.Status == DonationStatus.Success)
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit > 0 ? limit : 10)
                .ToListAsync();

            var now = DateTime.UtcNow;
            return donations.Select(d => new RecentDonationDto
            {
                Id = d.Id,
                DonorName = _moderationService.SanitizeForStream(d.DonorName),
                Amount = d.Amount,
                Message = _moderationService.SanitizeForStream(d.Message),
                CreatedAt = d.CreatedAt,
                TimeAgo = FormatTimeAgo(now - d.CreatedAt)
            }).ToList();
        }

        /// <summary>
        /// Định dạng khoảng thời gian tương đối dễ đọc: "Vừa xong", "5 phút trước", "2 giờ trước",...
        /// </summary>
        private static string FormatTimeAgo(TimeSpan span)
        {
            if (span.TotalMinutes < 1) return "Vừa xong";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} phút trước";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} giờ trước";
            return $"{(int)span.TotalDays} ngày trước";
        }

        // ====================================================================
        // 4. TEST ALERT VÀ POLLING THỜI GIAN THỰC CHO OBS STUDIO
        // ====================================================================

        /// <summary>
        /// Kích hoạt một thông báo Alert thử nghiệm lên OBS Browser Source
        /// </summary>
        public async Task TriggerTestAlertAsync(string streamerSlug)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();
            var config = await GetAlertBoxConfigAsync(cleanSlug, "");

            var queue = _testAlertQueues.GetOrAdd(cleanSlug, _ => new ConcurrentQueue<AlertPollResultDto>());

            var testAlert = new AlertPollResultDto
            {
                DonationId = 0, // 0 biểu thị alert thử nghiệm
                DonorName = "Khán Giả Thử Nghiệm",
                Amount = 50000,
                FormattedAmount = "50.000đ",
                Message = "Chào streamer! Chúc kênh của bạn luôn phát triển và có thật nhiều niềm vui nhé!",
                DisplayText = "Khán Giả Thử Nghiệm vừa ủng hộ 50.000 VNĐ!",
                MediaType = config.MediaType ?? "image",
                ImageUrl = config.ImageUrl,
                SoundUrl = config.SoundUrl,
                SoundVolume = config.SoundVolume,
                DurationSeconds = config.DurationSeconds,
                TextColor = config.TextColor,
                FontFamily = config.FontFamily,
                FontSize = config.FontSize,
                AnimationIn = config.AnimationIn,
                AnimationOut = config.AnimationOut,
                IsTtsEnabled = config.IsTtsEnabled
            };

            queue.Enqueue(testAlert);
        }

        public async Task<AlertPollResultDto?> CreateDonationAlertAsync(Donation donation, string streamerSlug)
        {
            var config = await GetAlertBoxConfigAsync(streamerSlug, "");
            return DonationAlertFactory.Create(donation, config, _moderationService);
        }

        /// <summary>
        /// Polling kiểm tra thông báo donate mới hoặc Test Alert từ streamer
        /// Trả về đối tượng thông báo nếu có khoản quyên góp mới vượt ngưỡng tối thiểu
        /// </summary>
        public async Task<(bool hasAlert, AlertPollResultDto? alert)> PollAlertAsync(string streamerSlug, int lastDonationId)
        {
            var cleanSlug = (streamerSlug ?? "tenstreamer").Trim().ToLower();

            // 1. Kiểm tra hàng đợi Test Alert ưu tiên trước
            if (_testAlertQueues.TryGetValue(cleanSlug, out var queue) && queue.TryDequeue(out var testAlert))
            {
                return (true, testAlert);
            }

            // 2. Kiểm tra donate thật từ CSDL
            var config = await GetAlertBoxConfigAsync(cleanSlug, "");
            var profile = await FindStreamerProfileAsync(cleanSlug);
            var profileId = profile?.Id ?? 0;
            var donation = await _context.Donations
                .AsNoTracking()
                .Where(d => (d.StreamerProfileId == profileId || d.StreamerProfile.Slug.ToLower() == cleanSlug)
                         && d.Status == DonationStatus.Success
                         && d.Id > lastDonationId
                         && d.Amount >= config.MinAmountToAlert)
                .OrderBy(d => d.Id)
                .FirstOrDefaultAsync();

            if (donation != null)
            {
                var alert = DonationAlertFactory.Create(donation, config, _moderationService);
                if (alert != null)
                {
                    return (true, alert);
                }
            }

            return (false, null);
        }
    }
}
