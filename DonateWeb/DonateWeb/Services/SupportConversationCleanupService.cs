using System.Data;
using DonateWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Services;

/// <summary>
/// Tự dọn những cuộc trò chuyện đã chuyển sang hỗ trợ viên hoặc đã đóng
/// sau 10 phút không có tin nhắn mới.
/// </summary>
public sealed class SupportConversationCleanupService : BackgroundService
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupportConversationCleanupService> _logger;

    public SupportConversationCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<SupportConversationCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        do
        {
            try
            {
                await DeleteExpiredConversationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Lỗi DB tạm thời không được làm dừng lịch dọn ở các lượt sau.
                _logger.LogError(exception, "Không thể dọn các cuộc trò chuyện hỗ trợ đã hết hạn.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeleteExpiredConversationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.Subtract(IdleTimeout);

        // Khóa bản ghi trong lúc dọn để không xóa một phiên đúng lúc có tin nhắn mới.
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var expiredIds = await db.SupportConversations
            .Where(conversation =>
                (conversation.AiPaused || conversation.Status == "Closed")
                && conversation.LastMessageAt <= cutoff)
            .OrderBy(conversation => conversation.LastMessageAt)
            .Select(conversation => conversation.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // Đánh dấu đóng trước, sau đó xóa; khóa ngoại sẽ xóa toàn bộ tin nhắn cùng phiên.
        await db.SupportConversations
            .Where(conversation => expiredIds.Contains(conversation.Id) && conversation.Status != "Closed")
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(conversation => conversation.Status, "Closed"),
                cancellationToken);

        var deletedCount = await db.SupportConversations
            .Where(conversation =>
                expiredIds.Contains(conversation.Id)
                && (conversation.AiPaused || conversation.Status == "Closed")
                && conversation.LastMessageAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Đã tự đóng và xóa {ConversationCount} cuộc trò chuyện hỗ trợ không hoạt động quá {IdleMinutes} phút.",
                deletedCount,
                (int)IdleTimeout.TotalMinutes);
        }
    }
}
