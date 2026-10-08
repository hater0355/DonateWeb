using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DonateWeb.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Hubs;

public sealed class PaymentHub : Hub
{
    private readonly AppDbContext _context;
    private readonly ILogger<PaymentHub> _logger;

    public PaymentHub(AppDbContext context, ILogger<PaymentHub> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task JoinTransactionGroup(string transactionCode)
    {
        var userId = GetAuthenticatedUserId();
        if (userId is null || string.IsNullOrWhiteSpace(transactionCode))
            throw new HubException("Authentication and a valid transaction are required.");

        var cleanCode = NormalizeTransactionCode(transactionCode);
        var parsedDonationId = int.TryParse(cleanCode, out var id) ? id : (int?)null;
        var parsedOrderCode = long.TryParse(cleanCode, out var orderCode) ? orderCode : (long?)null;
        var ownsWalletTransaction = await _context.WalletTransactions
            .AnyAsync(t => t.UserId == userId &&
                (t.TransactionCode == cleanCode || (parsedOrderCode.HasValue && t.OrderCode == parsedOrderCode.Value)));
        var ownsDonation = await _context.Donations
            .AnyAsync(d => d.DonorUserId == userId &&
                (d.TransactionCode == cleanCode || (parsedDonationId.HasValue && d.Id == parsedDonationId.Value)));

        if (!ownsWalletTransaction && !ownsDonation)
            throw new HubException("The transaction does not belong to the current user.");

        await Groups.AddToGroupAsync(Context.ConnectionId, "Tx_" + cleanCode);
    }

    public async Task LeaveTransactionGroup(string transactionCode)
    {
        if (!string.IsNullOrWhiteSpace(transactionCode))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Tx_" + NormalizeTransactionCode(transactionCode));
    }

    // Browser Source alerts are public by design; the group only receives public display data.
    public async Task JoinStreamerGroup(string streamerSlug)
    {
        if (string.IsNullOrWhiteSpace(streamerSlug)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, "Streamer_" + NormalizeSlug(streamerSlug));
    }

    public async Task JoinTtsGroup(string streamerSlug, string widgetToken)
    {
        if (string.IsNullOrWhiteSpace(streamerSlug) || string.IsNullOrWhiteSpace(widgetToken))
            throw new HubException("A streamer slug and widget token are required.");

        var cleanSlug = NormalizeSlug(streamerSlug);
        var storedToken = await _context.AlertBoxConfigs
            .Where(config => config.StreamerProfile != null && config.StreamerProfile.Slug.ToLower() == cleanSlug)
            .Select(config => config.WidgetToken)
            .FirstOrDefaultAsync();

        if (storedToken is null || !FixedTimeEquals(widgetToken, storedToken))
            throw new HubException("The widget token is invalid.");

        await Groups.AddToGroupAsync(Context.ConnectionId, "Tts_" + cleanSlug);
        _logger.LogInformation("TTS companion joined for streamer {StreamerSlug}.", cleanSlug);
    }

    public async Task JoinUserGroup(string userId)
    {
        var authenticatedUserId = GetAuthenticatedUserId();
        if (authenticatedUserId is null || !int.TryParse(userId, out var requestedUserId) || requestedUserId != authenticatedUserId)
            throw new HubException("You can only join your own user group.");

        await Groups.AddToGroupAsync(Context.ConnectionId, "User_" + authenticatedUserId.Value);
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private int? GetAuthenticatedUserId() =>
        Context.User?.Identity?.IsAuthenticated == true &&
        int.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    private static string NormalizeTransactionCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return normalized.StartsWith("TX_", StringComparison.Ordinal) ? normalized[3..] : normalized;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
