using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Security.RateLimiting;

namespace DonateWeb.Controllers;

[Route("SupportChat")]
public sealed class SupportChatController : Controller
{
    private const string GuestCookieName = "DonateWeb.SupportGuest";
    private const int MaximumMessageLength = 2000;
    private readonly AppDbContext _db;

    public SupportChatController(AppDbContext db) => _db = db;

    [HttpGet("Current")]
    [AllowAnonymous]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var conversation = await FindCurrentConversationAsync(cancellationToken);
        return Json(new { conversationId = conversation?.Id, status = conversation?.Status });
    }

    [HttpPost("Start")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [DonateRateLimit(10, 60, "support_chat_start")]
    public async Task<IActionResult> Start([FromForm] string? visitorName, CancellationToken cancellationToken)
    {
        var isStaff = IsSupportStaff();
        if (isStaff) return Forbid();

        var userId = GetUserId();
        string? guestHash = null;
        if (userId == null)
            guestHash = GetOrCreateGuestTokenHash();

        var existing = await FindCurrentConversationAsync(cancellationToken);
        if (existing != null)
            return Json(new { conversationId = existing.Id, status = existing.Status });

        var signedInUser = userId.HasValue
            ? await _db.Users.Where(u => u.Id == userId.Value).Select(u => new { u.FullName, u.Username }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var displayName = (signedInUser?.FullName ?? visitorName ?? signedInUser?.Username ?? "Khách").Trim();
        if (displayName.Length > 100) displayName = displayName[..100];
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Khách";

        var conversation = new SupportConversation
        {
            UserId = userId,
            GuestTokenHash = guestHash,
            VisitorName = displayName,
            Status = "Open",
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };
        _db.SupportConversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);
        return Json(new { conversationId = conversation.Id, status = conversation.Status });
    }

    [HttpGet("Messages/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Messages(int id, long afterMessageId = 0, CancellationToken cancellationToken = default)
    {
        var status = await _db.SupportConversations.Where(c => c.Id == id).Select(c => c.Status).FirstOrDefaultAsync(cancellationToken);
        if (status == null) return NotFound();
        if (!await CanAccessConversationAsync(id, cancellationToken)) return NotFound();

        var isStaff = IsSupportStaff();
        var unread = _db.SupportChatMessages.Where(m => m.ConversationId == id && (isStaff
            ? !m.IsFromSupport && !m.IsReadBySupport
            : m.IsFromSupport && !m.IsReadByVisitor));
        if (isStaff)
            await unread.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsReadBySupport, true), cancellationToken);
        else
            await unread.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.IsReadByVisitor, true), cancellationToken);

        var query = _db.SupportChatMessages.Where(m => m.ConversationId == id && m.Id > afterMessageId);
        var messages = afterMessageId > 0
            ? await query.OrderBy(m => m.Id).Take(100)
                .Select(m => new { id = m.Id, senderName = m.SenderName, isFromSupport = m.IsFromSupport, content = m.Content, createdAt = m.CreatedAt })
                .ToListAsync(cancellationToken)
            : await query.OrderByDescending(m => m.Id).Take(100)
                .Select(m => new { id = m.Id, senderName = m.SenderName, isFromSupport = m.IsFromSupport, content = m.Content, createdAt = m.CreatedAt })
                .ToListAsync(cancellationToken);

        if (afterMessageId == 0) messages.Reverse();
        return Json(new { messages, status });
    }

    [HttpPost("Send")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [DonateRateLimit(30, 60, "support_chat_send")]
    public async Task<IActionResult> Send([FromForm] int conversationId, [FromForm] string? message, CancellationToken cancellationToken)
    {
        if (!await CanAccessConversationAsync(conversationId, cancellationToken)) return NotFound();
        var conversation = await _db.SupportConversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.Status == "Open", cancellationToken);
        if (conversation == null) return Conflict(new { error = "Cuộc trò chuyện đã đóng." });

        var content = (message ?? string.Empty).Trim();
        if (content.Length == 0 || content.Length > MaximumMessageLength)
            return BadRequest(new { error = $"Tin nhắn phải có từ 1 đến {MaximumMessageLength} ký tự." });

        var isStaff = IsSupportStaff();
        var userId = GetUserId();
        var senderName = isStaff
            ? User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Hỗ trợ viên"
            : userId.HasValue
                ? await _db.Users.Where(u => u.Id == userId.Value).Select(u => u.FullName).FirstOrDefaultAsync(cancellationToken) ?? conversation.VisitorName
                : conversation.VisitorName;
        if (senderName.Length > 100) senderName = senderName[..100];

        _db.SupportChatMessages.Add(new SupportChatMessage
        {
            ConversationId = conversationId,
            SenderUserId = userId,
            SenderName = senderName,
            IsFromSupport = isStaff,
            Content = content,
            IsReadBySupport = isStaff,
            IsReadByVisitor = !isStaff,
            CreatedAt = DateTime.UtcNow
        });
        conversation.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { sent = true });
    }

    [HttpGet("Inbox")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.SupportAgent)]
    public IActionResult Inbox() => View();

    [HttpGet("List")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.SupportAgent)]
    public async Task<IActionResult> List(string status = "Open", CancellationToken cancellationToken = default)
    {
        status = status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "Closed" : "Open";
        var rows = await _db.SupportConversations
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(100)
            .Select(c => new
            {
                id = c.Id,
                visitorName = c.VisitorName,
                userEmail = c.User == null ? null : c.User.Email,
                status = c.Status,
                lastMessageAt = c.LastMessageAt,
                lastMessage = c.Messages.OrderByDescending(m => m.Id).Select(m => m.Content).FirstOrDefault() ?? "Chưa có tin nhắn",
                unreadCount = c.Messages.Count(m => !m.IsFromSupport && !m.IsReadBySupport)
            })
            .ToListAsync(cancellationToken);
        return Json(rows);
    }

    [HttpPost("Close")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.SupportAgent)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([FromForm] int conversationId, CancellationToken cancellationToken)
    {
        var conversation = await _db.SupportConversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
        if (conversation == null) return NotFound();
        conversation.Status = "Closed";
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { closed = true });
    }

    private async Task<SupportConversation?> FindCurrentConversationAsync(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        IQueryable<SupportConversation> query = _db.SupportConversations.Where(c => c.Status == "Open");
        if (userId.HasValue)
            query = query.Where(c => c.UserId == userId.Value);
        else if (Request.Cookies.TryGetValue(GuestCookieName, out var token) && !string.IsNullOrWhiteSpace(token))
            query = query.Where(c => c.GuestTokenHash == HashToken(token));
        else
            return null;
        return await query.OrderByDescending(c => c.LastMessageAt).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> CanAccessConversationAsync(int id, CancellationToken cancellationToken)
    {
        if (IsSupportStaff()) return true;
        var userId = GetUserId();
        if (userId.HasValue)
            return await _db.SupportConversations.AnyAsync(c => c.Id == id && c.UserId == userId.Value, cancellationToken);
        if (!Request.Cookies.TryGetValue(GuestCookieName, out var token) || string.IsNullOrWhiteSpace(token)) return false;
        var hash = HashToken(token);
        return await _db.SupportConversations.AnyAsync(c => c.Id == id && c.GuestTokenHash == hash, cancellationToken);
    }

    private bool IsSupportStaff() => User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.SupportAgent);

    private int? GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string GetOrCreateGuestTokenHash()
    {
        if (!Request.Cookies.TryGetValue(GuestCookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Response.Cookies.Append(GuestCookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps || !HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(),
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(180)
            });
        }
        return HashToken(token);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
