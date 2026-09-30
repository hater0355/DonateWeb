using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
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

                    // Top người ủng hộ nhiều nhất từ database
                    var topDonors = await _context.Donations
                        .AsNoTracking()
                        .Where(d => d.Status == DonationStatus.Success && !string.IsNullOrWhiteSpace(d.DonorName))
                        .GroupBy(d => d.DonorName)
                        .Select(g => new { DonorName = g.Key, Total = g.Sum(x => x.Amount) })
                        .OrderByDescending(x => x.Total)
                        .Take(5)
                        .ToListAsync();

                    foreach (var donor in topDonors)
                    {
                        activities.Add(new HomeActivityStatViewModel
                        {
                            Title = donor.DonorName,
                            Value = $"{donor.Total:N0} đ",
                            IconClass = "fa-solid fa-medal text-warning me-2"
                        });
                    }
                }

                var model = new HomeViewModel
                {
                    SearchKeyword = trimmedKeyword,
                    FeaturedStreamers = featuredStreamers,
                    Stories = stories,
                    RecentActivities = activities,
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