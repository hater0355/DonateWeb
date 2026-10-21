using System.Security.Claims;
using System.Globalization;
using System.Text;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Security.RateLimiting;

namespace DonateWeb.Controllers;

[Route("SupportChat")]
[Authorize]
public sealed class SupportChatController : Controller
{
    private const int MaximumMessageLength = 2000;
    private readonly AppDbContext _db;
    private readonly ISupportAiService _supportAiService;

    public SupportChatController(AppDbContext db, ISupportAiService supportAiService)
    {
        _db = db;
        _supportAiService = supportAiService;
    }

    [HttpGet("Current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var conversation = await FindCurrentConversationAsync(cancellationToken);
        return Json(new { conversationId = conversation?.Id, status = conversation?.Status });
    }

    [HttpPost("Start")]
    [ValidateAntiForgeryToken]
    [DonateRateLimit(10, 60, "support_chat_start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        var isStaff = IsSupportStaff();
        if (isStaff) return Forbid();

        var userId = GetUserId();
        if (!userId.HasValue) return Challenge();

        var existing = await FindCurrentConversationAsync(cancellationToken);
        if (existing != null)
            return Json(new { conversationId = existing.Id, status = existing.Status });

        var signedInUser = userId.HasValue
            ? await _db.Users.Where(u => u.Id == userId.Value).Select(u => new { u.FullName, u.Username }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var displayName = (signedInUser?.FullName ?? signedInUser?.Username ?? "Người dùng").Trim();
        if (displayName.Length > 100) displayName = displayName[..100];
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Khách";

        var conversation = new SupportConversation
        {
            UserId = userId.Value,
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
    public async Task<IActionResult> Messages(int id, long afterMessageId = 0, CancellationToken cancellationToken = default)
    {
        var conversationState = await _db.SupportConversations
            .Where(c => c.Id == id)
            .Select(c => new { c.Status, c.AiPaused })
            .FirstOrDefaultAsync(cancellationToken);
        if (conversationState == null) return NotFound();
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
                .Select(m => new { id = m.Id, senderName = m.SenderName, isFromSupport = m.IsFromSupport, isAi = m.IsAi, content = m.Content, createdAt = m.CreatedAt })
                .ToListAsync(cancellationToken)
            : await query.OrderByDescending(m => m.Id).Take(100)
                .Select(m => new { id = m.Id, senderName = m.SenderName, isFromSupport = m.IsFromSupport, isAi = m.IsAi, content = m.Content, createdAt = m.CreatedAt })
                .ToListAsync(cancellationToken);

        if (afterMessageId == 0) messages.Reverse();
        return Json(new { messages, status = conversationState.Status, aiPaused = conversationState.AiPaused });
    }

    [HttpPost("Send")]
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

        var wasAiPaused = conversation.AiPaused;
        var requestsHuman = !isStaff && IsHumanHandoffRequest(content);

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
        if (isStaff || requestsHuman)
            conversation.AiPaused = true;
        conversation.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        if (isStaff)
            return Ok(new { sent = true, aiStatus = "paused" });

        if (requestsHuman)
        {
            await AddAiMessageAsync(conversation, "Mình đã chuyển cuộc trò chuyện cho Hỗ trợ viên. Bạn vui lòng chờ nhân viên phản hồi nhé.", cancellationToken);
            return Ok(new { sent = true, aiStatus = "handoff" });
        }

        if (wasAiPaused)
            return Ok(new { sent = true, aiStatus = "paused" });

        var aiReply = await _supportAiService.GenerateReplyAsync(conversationId, cancellationToken);
        await _db.Entry(conversation).ReloadAsync(cancellationToken);

        if (conversation.Status != "Open" || conversation.AiPaused)
            return Ok(new { sent = true, aiStatus = "paused" });

        if (aiReply.Status is SupportAiReplyStatus.Answered or SupportAiReplyStatus.OutOfScope or SupportAiReplyStatus.Handoff
            && !string.IsNullOrWhiteSpace(aiReply.Message))
        {
            if (aiReply.Status == SupportAiReplyStatus.Handoff)
                conversation.AiPaused = true;
            await AddAiMessageAsync(conversation, aiReply.Message, cancellationToken);
            var status = aiReply.Status switch
            {
                SupportAiReplyStatus.Handoff => "handoff",
                SupportAiReplyStatus.OutOfScope => "outOfScope",
                _ => "answered"
            };
            return Ok(new { sent = true, aiStatus = status });
        }

        // AI chưa cấu hình, đã chạm ngân sách hoặc gặp lỗi: chuyển quyền xử lý cho nhân viên để không thử gọi lặp.
        conversation.AiPaused = true;
        await _db.SaveChangesAsync(cancellationToken);
        var statusMessage = aiReply.Status switch
        {
            SupportAiReplyStatus.NotConfigured => "notConfigured",
            SupportAiReplyStatus.MonthlyLimitReached => "monthlyLimitReached",
            _ => "unavailable"
        };
        return Ok(new { sent = true, aiStatus = statusMessage });
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
                aiPaused = c.AiPaused,
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
        if (!userId.HasValue) return null;
        return await _db.SupportConversations
            .Where(c => c.Status == "Open" && c.UserId == userId.Value)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> CanAccessConversationAsync(int id, CancellationToken cancellationToken)
    {
        if (IsSupportStaff()) return true;
        var userId = GetUserId();
        return userId.HasValue
            && await _db.SupportConversations.AnyAsync(c => c.Id == id && c.UserId == userId.Value, cancellationToken);
    }

    private bool IsSupportStaff() => User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.SupportAgent);

    private async Task AddAiMessageAsync(SupportConversation conversation, string content, CancellationToken cancellationToken)
    {
        _db.SupportChatMessages.Add(new SupportChatMessage
        {
            ConversationId = conversation.Id,
            SenderName = "Trợ lý AI DonateWeb",
            IsFromSupport = true,
            IsAi = true,
            Content = content.Length > MaximumMessageLength ? content[..MaximumMessageLength] : content,
            IsReadBySupport = true,
            IsReadByVisitor = false,
            CreatedAt = DateTime.UtcNow
        });
        conversation.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsHumanHandoffRequest(string message)
    {
        var decomposed = message.Normalize(NormalizationForm.FormD);
        var withoutMarks = new string(decomposed.Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        var normalized = withoutMarks.Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();

        return new[]
        {
            "gap ho tro vien", "gap nhan vien", "noi chuyen voi nhan vien", "noi chuyen voi nguoi that",
            "gap nguoi that", "chuyen cho nhan vien", "can nhan vien ho tro", "muon gap nhan vien"
        }.Any(normalized.Contains);
    }

    private int? GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

}
