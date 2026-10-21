namespace DonateWeb.Services;

public enum SupportAiReplyStatus
{
    Answered,
    OutOfScope,
    Handoff,
    NotConfigured,
    MonthlyLimitReached,
    Unavailable
}

public sealed record SupportAiReplyResult(SupportAiReplyStatus Status, string? Message = null);

public interface ISupportAiService
{
    Task<SupportAiReplyResult> GenerateReplyAsync(int conversationId, CancellationToken cancellationToken);
}
