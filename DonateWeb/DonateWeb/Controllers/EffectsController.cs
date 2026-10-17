using System.Security.Claims;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using DonateWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Controllers;

public class EffectsController : Controller
{
    private readonly AppDbContext _context;

    public EffectsController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : (int?)null;
        var profile = userId.HasValue
            ? await _context.StreamerProfiles.AsNoTracking()
                .FirstOrDefaultAsync(streamer => streamer.UserId == userId.Value &&
                    streamer.IsActive && streamer.ApprovalStatus == StreamerApprovalStatus.Approved)
            : null;

        var activation = profile == null
            ? null
            : await _context.StreamerEffectActivations.AsNoTracking()
                .FirstOrDefaultAsync(item => item.StreamerProfileId == profile.Id && item.IsEnabled);

        var campaigns = await _context.SpecialEffectCampaigns.AsNoTracking()
            .OrderByDescending(campaign => campaign.StartsAtUtc)
            .ToListAsync();

        var model = new SpecialEffectsViewModel
        {
            CanManage = profile != null && User.IsInRole(UserRoles.Streamer),
            StreamerName = profile?.DisplayName,
            ActiveCampaignId = activation?.SpecialEffectCampaignId,
            Campaigns = campaigns.Select(campaign =>
            {
                var available = campaign.IsEnabled && campaign.StartsAtUtc <= now && campaign.EndsAtUtc >= now;
                var status = !campaign.IsEnabled ? "Tạm dừng bởi quản trị viên"
                    : campaign.StartsAtUtc > now ? "Sắp diễn ra"
                    : campaign.EndsAtUtc < now ? "Đã hết hạn"
                    : "Đang diễn ra";
                return new SpecialEffectCampaignCardViewModel
                {
                    Id = campaign.Id,
                    Title = campaign.Title,
                    Description = campaign.Description,
                    StartsAtLocal = VietnamTime.ToLocal(campaign.StartsAtUtc),
                    EndsAtLocal = VietnamTime.ToLocal(campaign.EndsAtUtc),
                    IsAvailable = available,
                    CanActivate = campaign.IsEnabled && campaign.EndsAtUtc >= now,
                    IsStreamerEnabled = activation is { IsEnabled: true } && activation.SpecialEffectCampaignId == campaign.Id,
                    Status = status
                };
            }).ToList()
        };

        return View(model);
    }

    /// <summary>Streamer bật/tắt hiệu ứng đang nằm trong thời gian Admin đã lên lịch.</summary>
    [HttpPost]
    [Authorize(Roles = UserRoles.Streamer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Challenge();

        var profile = await _context.StreamerProfiles.FirstOrDefaultAsync(streamer =>
            streamer.UserId == userId && streamer.IsActive &&
            streamer.ApprovalStatus == StreamerApprovalStatus.Approved);
        if (profile == null)
        {
            TempData["ErrorMessage"] = "Kênh của bạn chưa được Admin duyệt hoặc hiện không hoạt động.";
            return RedirectToAction(nameof(Index));
        }

        var campaign = await _context.SpecialEffectCampaigns.FirstOrDefaultAsync(item => item.Id == id);
        if (campaign == null)
        {
            TempData["ErrorMessage"] = "Không tìm thấy sự kiện hiệu ứng.";
            return RedirectToAction(nameof(Index));
        }

        var activation = await _context.StreamerEffectActivations
            .FirstOrDefaultAsync(item => item.StreamerProfileId == profile.Id);
        if (activation?.SpecialEffectCampaignId == campaign.Id && activation.IsEnabled)
        {
            activation.IsEnabled = false;
            activation.UpdatedAtUtc = DateTime.UtcNow;
            TempData["SuccessMessage"] = "Bạn đã tắt hiệu ứng cho kênh của mình.";
        }
        else
        {
            var now = DateTime.UtcNow;
            if (!campaign.IsEnabled || campaign.EndsAtUtc < now)
            {
                TempData["ErrorMessage"] = "Hiệu ứng này chưa đến ngày mở, đã hết hạn hoặc đã bị Admin tạm dừng.";
                return RedirectToAction(nameof(Index));
            }

            if (activation == null)
            {
                _context.StreamerEffectActivations.Add(new DonateWeb.Models.Entities.StreamerEffectActivation
                {
                    StreamerProfileId = profile.Id,
                    SpecialEffectCampaignId = campaign.Id,
                    IsEnabled = true,
                    ActivatedAtUtc = now
                });
            }
            else
            {
                activation.SpecialEffectCampaignId = campaign.Id;
                activation.IsEnabled = true;
                activation.ActivatedAtUtc = now;
                activation.UpdatedAtUtc = now;
            }
            TempData["SuccessMessage"] = campaign.StartsAtUtc > now
                ? "Đã bật sẵn hiệu ứng. Nó sẽ bắt đầu chạy khi đến thời gian Admin đã đặt."
                : "Hiệu ứng đã bật. Donate mới trong thời gian sự kiện sẽ kích hoạt hoạt ảnh trên AlertBox.";
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
