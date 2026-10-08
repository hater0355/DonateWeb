using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.ViewModels;

namespace DonateWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(AppDbContext context, ILogger<HomeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? keyword)
        {
            try
            {
                var trimmedKeyword = keyword?.Trim();

                // 1. Truy vấn danh sách Streamer đang hoạt động và lọc theo từ khóa bằng LINQ Contains
                var streamerQuery = _context.StreamerProfiles
                    .AsNoTracking()
                    .Include(sp => sp.User)
                    .Where(sp => sp.IsActive);

                if (!string.IsNullOrWhiteSpace(trimmedKeyword))
                {
                    streamerQuery = streamerQuery.Where(sp =>
                        sp.DisplayName.Contains(trimmedKeyword) ||
                        sp.Slug.Contains(trimmedKeyword) ||
                        (sp.User != null && sp.User.AccountId != null && sp.User.AccountId.Contains(trimmedKeyword)) ||
                        (sp.Bio != null && sp.Bio.Contains(trimmedKeyword)) ||
                        (sp.GreetingMessage != null && sp.GreetingMessage.Contains(trimmedKeyword)));
                }

                var streamers = await streamerQuery
                    .OrderByDescending(sp => sp.TotalReceived)
                    .ThenByDescending(sp => sp.FollowerCount)
                    .Take(string.IsNullOrWhiteSpace(trimmedKeyword) ? 8 : 24)
                    .ToListAsync();

                var featuredStreamers = streamers.Select(sp => new HomeStreamerCardViewModel
                {
                    Id = sp.Id,
                    Slug = sp.Slug,
                    DisplayName = sp.DisplayName,
                    AvatarUrl = string.IsNullOrWhiteSpace(sp.AvatarUrl) ? "/images/default-avatar.png" : sp.AvatarUrl,
                    FollowerCount = sp.FollowerCount,
                    Rank = sp.Rank,
                    TotalReceived = sp.TotalReceived,
                    IsVerified = sp.IsVerified
                }).ToList();

                var stories = streamers.Take(6).Select(sp => new HomeStoryViewModel
                {
                    StreamerId = sp.Id,
                    Slug = sp.Slug,
                    Name = sp.DisplayName,
                    AvatarUrl = string.IsNullOrWhiteSpace(sp.AvatarUrl) ? "/images/default-avatar.png" : sp.AvatarUrl,
                    FollowerCount = sp.FollowerCount,
                    Rank = sp.Rank
                }).ToList();

                // 2. Lấy hoạt động donate thực tế từ Database
                var activities = new List<HomeActivityStatViewModel>();

                var totalDonations = await _context.Donations
                    .AsNoTracking()
                    .CountAsync(d => d.Status == DonationStatus.Success);

                var totalAmount = await _context.Donations
                    .AsNoTracking()
                    .Where(d => d.Status == DonationStatus.Success)
                    .SumAsync(d => (decimal?)d.Amount) ?? 0;

                if (totalDonations > 0)
                {
                    activities.Add(new HomeActivityStatViewModel
                    {
                        Title = "Tổng số lượt donate",
                        Value = totalDonations.ToString("N0"),
                        IconClass = "fa-solid fa-crown text-warning me-2"
                    });
                }

                // 2.1 Bảng xếp hạng TOP NẠP (kết hợp giao dịch nạp ví thành công và donate)
                var walletDeposits = await _context.WalletTransactions
                    .AsNoTracking()
                    .Include(w => w.User)
                    .Where(w => (w.TransactionType.ToUpper() == "DEPOSIT" || w.TransactionType.ToUpper() == "NAP") &&
                                (w.Status == 1 || w.Status == WalletTransaction.StatusCompleted))
                    .GroupBy(w => new { w.UserId, w.User.FullName, w.User.Username, w.User.AvatarUrl })
                    .Select(g => new
                    {
                        Name = !string.IsNullOrWhiteSpace(g.Key.FullName) ? g.Key.FullName : (g.Key.Username ?? "Người dùng"),
                        AvatarUrl = (string?)g.Key.AvatarUrl,
                        TotalAmount = g.Sum(x => x.Amount)
                    })
                    .ToListAsync();

                var donationDeposits = await _context.Donations
                    .AsNoTracking()
                    .Include(d => d.DonorUser)
                    .Where(d => d.Status == DonationStatus.Success && !string.IsNullOrWhiteSpace(d.DonorName))
                    .GroupBy(d => new { d.DonorName, Avatar = d.DonorUser != null ? d.DonorUser.AvatarUrl : null })
                    .Select(g => new
                    {
                        Name = g.Key.DonorName,
                        AvatarUrl = g.Key.Avatar,
                        TotalAmount = g.Sum(x => x.Amount)
                    })
                    .ToListAsync();

                var mergedTopDonors = walletDeposits
                    .Concat(donationDeposits)
                    .GroupBy(x => x.Name)
                    .Select(g => new HomeTopDonorViewModel
                    {
                        Name = g.Key,
                        AvatarUrl = g.FirstOrDefault(x => !string.IsNullOrEmpty(x.AvatarUrl))?.AvatarUrl,
                        TotalAmount = g.Sum(x => x.TotalAmount)
                    })
                    .OrderByDescending(x => x.TotalAmount)
                    .Take(5)
                    .ToList();

                for (int i = 0; i < mergedTopDonors.Count; i++)
                {
                    mergedTopDonors[i].Rank = i + 1;
                }

                // 2.2 Bảng xếp hạng TOP STREAMER: Tính hoàn toàn theo số tiền được donate thực tế
                var topStreamerProfiles = await _context.StreamerProfiles
                    .AsNoTracking()
                    .Where(sp => sp.IsActive)
                    .Select(sp => new
                    {
                        sp.Id,
                        sp.Slug,
                        sp.DisplayName,
                        sp.AvatarUrl,
                        sp.FollowerCount,
                        sp.IsVerified,
                        sp.Rank,
                        // Tính chính xác tổng tiền từ các lượt donate thành công trong bảng Donations, fallback về TotalReceived
                        TotalDonated = _context.Donations
                            .Where(d => d.StreamerProfileId == sp.Id && d.Status == DonationStatus.Success)
                            .Sum(d => (decimal?)d.Amount) ?? sp.TotalReceived
                    })
                    .OrderByDescending(x => x.TotalDonated)
                    .ThenBy(x => x.Id)
                    .Take(5)
                    .ToListAsync();

                var topStreamers = topStreamerProfiles.Select((sp, index) => new HomeStreamerCardViewModel
                {
                    Id = sp.Id,
                    Slug = sp.Slug,
                    DisplayName = sp.DisplayName,
                    AvatarUrl = string.IsNullOrWhiteSpace(sp.AvatarUrl) ? "/images/default-avatar.png" : sp.AvatarUrl,
                    FollowerCount = sp.FollowerCount,
                    Rank = index + 1,
                    TotalReceived = sp.TotalDonated,
                    IsVerified = sp.IsVerified
                }).ToList();

                // 4. Lấy danh sách Status của tất cả Streamer
                var likedCookie = Request.Cookies["LikedStatuses"] ?? "";
                var likedIds = likedCookie.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

                var rawStatuses = await _context.StreamerStatuses
                    .AsNoTracking()
                    .Include(s => s.StreamerProfile)
                    .Where(s => s.StreamerProfile.IsActive)
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(12)
                    .ToListAsync();

                var streamerStatuses = rawStatuses.Select(s => new StreamerStatusViewModel
                {
                    Id = s.Id,
                    StreamerProfileId = s.StreamerProfileId,
                    StreamerName = s.StreamerProfile.DisplayName,
                    StreamerSlug = !string.IsNullOrWhiteSpace(s.StreamerProfile.Slug) ? s.StreamerProfile.Slug : s.StreamerProfile.Id.ToString(),
                    StreamerAvatarUrl = string.IsNullOrWhiteSpace(s.StreamerProfile.AvatarUrl) ? "/images/default-avatar.png" : s.StreamerProfile.AvatarUrl,
                    Content = s.Content,
                    ImageUrl = s.ImageUrl,
                    LikeCount = s.LikeCount,
                    CreatedAt = s.CreatedAt,
                    IsLikedByCurrentUser = likedIds.Contains(s.Id.ToString())
                }).ToList();

                var model = new HomeViewModel
                {
                    SearchKeyword = trimmedKeyword,
                    FeaturedStreamers = featuredStreamers,
                    Stories = stories,
                    Statuses = streamerStatuses,
                    RecentActivities = activities,
                    TopDonors = mergedTopDonors,
                    TopStreamers = topStreamers,
                    TotalDonationCount = totalDonations,
                    TotalDonatedAmount = totalAmount,
                    TotalStreamerCount = await _context.StreamerProfiles.CountAsync(s => s.IsActive)
                };

                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra khi truy vấn dữ liệu trang chủ từ database.");
                return View("Index", new HomeViewModel { SearchKeyword = keyword?.Trim() });
            }
        }

        [HttpGet]
        public Task<IActionResult> Search(string? keyword)
        {
            return Index(keyword);
        }
    }
}