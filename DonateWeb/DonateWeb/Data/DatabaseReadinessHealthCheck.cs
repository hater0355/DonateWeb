using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DonateWeb.Data;

/// <summary>
/// Readiness yêu cầu SQL Server truy cập được và schema đã ở migration mới nhất.
/// Không gọi các nhà cung cấp ngoài để tránh phát sinh chi phí hoặc phụ thuộc mạng.
/// </summary>
public sealed class DatabaseReadinessHealthCheck(
    AppDbContext context,
    ILogger<DatabaseReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await context.Database.CanConnectAsync(cancellationToken))
                return HealthCheckResult.Unhealthy("SQL Server không truy cập được.");

            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            if (pendingMigrations.Any())
                return HealthCheckResult.Unhealthy("Database cần được cập nhật bằng migration script.");

            return HealthCheckResult.Healthy("SQL Server truy cập được và schema đã cập nhật.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Readiness check thất bại khi kiểm tra SQL Server hoặc migration history.");
            return HealthCheckResult.Unhealthy("SQL Server hoặc migration history chưa sẵn sàng.");
        }
    }
}
