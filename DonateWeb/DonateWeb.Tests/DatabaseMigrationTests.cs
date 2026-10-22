using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using Xunit;

namespace DonateWeb.Tests;

public sealed class DatabaseMigrationTests
{
    [SqlServerFact]
    public async Task NewDatabaseRequiresExplicitMigrationAndThenPassesReadiness()
    {
        await using var database = await SqlServerDatabase.CreateAsync(applyMigrations: false);
        var guardException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSchemaGuard.EnsureMigrationsAppliedAsync(database.Context));
        Assert.Contains("migration", guardException.Message, StringComparison.OrdinalIgnoreCase);

        var healthCheck = new DatabaseReadinessHealthCheck(
            database.Context,
            NullLogger<DatabaseReadinessHealthCheck>.Instance);
        var beforeMigration = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, beforeMigration.Status);

        await database.Context.Database.MigrateAsync();
        await DatabaseSchemaGuard.EnsureMigrationsAppliedAsync(database.Context);
        await database.Context.Database.MigrateAsync();

        var afterMigration = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, afterMigration.Status);
        Assert.Empty(await database.Context.Database.GetPendingMigrationsAsync());
    }

    [SqlServerFact]
    public async Task ExistingSchemaCanBeBaselinedAndMissingMigrationIndexesAreAddedWithoutRecreatingTables()
    {
        await using var database = await SqlServerDatabase.CreateAsync(applyMigrations: false);
        await database.Context.Database.EnsureCreatedAsync();
        var usersBefore = await database.Context.Users.CountAsync();
        await database.Context.Database.ExecuteSqlRawAsync("DROP INDEX IX_ShopOrders_DiscountCouponId ON dbo.ShopOrders;");
        await database.Context.Database.ExecuteSqlRawAsync("DROP INDEX IX_StreamerEffectActivations_SpecialEffectCampaignId ON dbo.StreamerEffectActivations;");

        var baselinePath = Path.Combine(AppContext.BaseDirectory, "database", "baseline-existing-database.sql");
        var baselineSql = await File.ReadAllTextAsync(baselinePath);
        baselineSql = RetargetBaselineToTestDatabase(baselineSql, database.Name);
        await database.Context.Database.ExecuteSqlRawAsync(baselineSql);

        await DatabaseSchemaGuard.EnsureMigrationsAppliedAsync(database.Context);
        Assert.Equal(usersBefore, await database.Context.Users.CountAsync());
        Assert.Empty(await database.Context.Database.GetPendingMigrationsAsync());

        var indexCount = await database.Context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS [Value]
            FROM sys.indexes AS i
            WHERE (i.object_id = OBJECT_ID(N'dbo.ShopOrders') AND i.name = N'IX_ShopOrders_DiscountCouponId')
               OR (i.object_id = OBJECT_ID(N'dbo.StreamerEffectActivations') AND i.name = N'IX_StreamerEffectActivations_SpecialEffectCampaignId')
            """).SingleAsync();
        Assert.Equal(2, indexCount);
    }

    [SqlServerFact]
    public async Task BaselineScriptRefusesAnIncompleteSchemaWithoutCreatingMigrationHistory()
    {
        await using var database = await SqlServerDatabase.CreateAsync(applyMigrations: false);
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "database", "baseline-existing-database.sql");
        var baselineSql = await File.ReadAllTextAsync(baselinePath);
        baselineSql = RetargetBaselineToTestDatabase(baselineSql, database.Name);

        await Assert.ThrowsAnyAsync<Exception>(
            () => database.Context.Database.ExecuteSqlRawAsync(baselineSql));

        var historyTableExists = await database.Context.Database
            .SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID('dbo.__EFMigrationsHistory', 'U') IS NULL THEN 0 ELSE 1 END AS [Value]")
            .SingleAsync();
        Assert.Equal(0, historyTableExists);
    }

    private static string RetargetBaselineToTestDatabase(string baselineSql, string databaseName) => baselineSql.Replace(
        "DECLARE @ExpectedDatabaseName sysname = N'DonateWebDB';",
        $"DECLARE @ExpectedDatabaseName sysname = N'{databaseName.Replace("'", "''", StringComparison.Ordinal)}';",
        StringComparison.Ordinal);
}
