using DonateWeb.Areas.Admin.ViewModels;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = UserRoles.Admin)]
[Route("Admin/SupportAgents")]
public sealed class AdminSupportAgentsController : Controller
{
    private readonly AppDbContext _db;
    public AdminSupportAgentsController(AppDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var agentRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.SupportAgent, cancellationToken);
        if (agentRole == null) return Problem("Chưa khởi tạo quyền Hỗ trợ viên. Hãy khởi động lại ứng dụng.");

        var agentIds = await _db.UserRoles.Where(ur => ur.RoleId == agentRole.Id).Select(ur => ur.UserId).ToListAsync(cancellationToken);
        var agents = await _db.Users.Where(u => agentIds.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new SupportAgentUserViewModel { Id = u.Id, Username = u.Username, FullName = u.FullName, Email = u.Email })
            .ToListAsync(cancellationToken);

        var candidates = new List<SupportAgentUserViewModel>();
        if (agents.Count < 2)
        {
            var query = _db.Users
                .Where(u => u.IsActive && !agentIds.Contains(u.Id)
                    && u.UserRoles.Any(ur => ur.Role!.Name == UserRoles.Viewer)
                    && !u.UserRoles.Any(ur => ur.Role!.Name == UserRoles.Admin || ur.Role.Name == UserRoles.Streamer));
            var term = search?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(u => u.Username.Contains(term) || u.FullName.Contains(term) || u.Email.Contains(term));
            candidates = await query.OrderBy(u => u.FullName).Take(30)
                .Select(u => new SupportAgentUserViewModel { Id = u.Id, Username = u.Username, FullName = u.FullName, Email = u.Email })
                .ToListAsync(cancellationToken);
        }

        return View(new SupportAgentManagementViewModel
        {
            Search = search ?? string.Empty,
            Agents = agents,
            Candidates = candidates
        });
    }

    [HttpPost("Grant")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Grant(int userId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == UserRoles.SupportAgent, cancellationToken);
        var target = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (role == null || target == null || !target.IsActive
            || !target.UserRoles.Any(ur => ur.Role!.Name == UserRoles.Viewer)
            || target.UserRoles.Any(ur => ur.Role!.Name == UserRoles.Admin || ur.Role.Name == UserRoles.Streamer))
        {
            TempData["SupportAgentsError"] = "Không thể cấp quyền cho tài khoản này. Chỉ tài khoản Viewer đang hoạt động được chọn.";
            return RedirectToAction(nameof(Index));
        }

        var assignedCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, cancellationToken);
        var alreadyAssigned = target.UserRoles.Any(ur => ur.RoleId == role.Id);
        if (!alreadyAssigned && assignedCount >= 2)
        {
            TempData["SupportAgentsError"] = "Đã có đủ hai Hỗ trợ viên. Hãy thu hồi một tài khoản trước khi cấp cho người khác.";
            return RedirectToAction(nameof(Index));
        }

        if (!alreadyAssigned)
        {
            _db.UserRoles.Add(new UserRole { UserId = target.Id, RoleId = role.Id });
            await _db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        TempData["SupportAgentsSuccess"] = $"Đã cấp quyền Hỗ trợ viên cho {target.FullName} ({target.Username}). Tài khoản cần đăng xuất rồi đăng nhập lại để nhận quyền mới.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int userId, CancellationToken cancellationToken)
    {
        var assignment = await _db.UserRoles.Include(ur => ur.Role)
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.Role!.Name == UserRoles.SupportAgent, cancellationToken);
        if (assignment != null)
        {
            _db.UserRoles.Remove(assignment);
            await _db.SaveChangesAsync(cancellationToken);
            TempData["SupportAgentsSuccess"] = "Đã thu hồi quyền Hỗ trợ viên. Tài khoản cần đăng xuất rồi đăng nhập lại để cập nhật quyền.";
        }
        return RedirectToAction(nameof(Index));
    }
}
