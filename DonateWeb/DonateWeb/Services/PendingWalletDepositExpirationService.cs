using DonateWeb.Data;
using DonateWeb.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DonateWeb.Services;

public sealed class PendingWalletDepositExpirationService : BackgroundService
{
    private static readonly TimeSpan ExpirationAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingWalletDepositExpirationService> _logger;

    public PendingWalletDepositExpirationService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingWalletDepositExpirationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpirePendingDepositsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to expire pending wallet deposits.");
            }
        }
    }

    private async Task ExpirePendingDepositsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTime.UtcNow.Subtract(ExpirationAge);

        var expiredCount = await db.WalletTransactions
            .Where(transaction => transaction.TransactionType == "DEPOSIT"
                && transaction.Status == WalletTransaction.StatusPending
                && transaction.CreatedAt <= cutoff)
            .ExecuteUpdateAsync(update => update
                .SetProperty(transaction => transaction.Status, WalletTransaction.StatusFailed)
                .SetProperty(transaction => transaction.FailedAt, DateTime.UtcNow), cancellationToken);

        if (expiredCount > 0)
        {
            _logger.LogInformation("Marked {Count} unpaid wallet deposits as failed after 10 minutes.", expiredCount);
        }
    }
}
