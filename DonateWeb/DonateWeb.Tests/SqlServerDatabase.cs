using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;

namespace DonateWeb.Tests;

internal sealed class SqlServerDatabase : IAsyncDisposable
{
    private readonly string _databaseName;

    private SqlServerDatabase(string connectionString, string databaseName, AppDbContext context)
    {
        ConnectionString = connectionString;
        _databaseName = databaseName;
        Context = context;
    }

    public string ConnectionString { get; }
    public AppDbContext Context { get; }

    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(ConnectionString)
        .Options);

    public static async Task<SqlServerDatabase> CreateAsync()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("DONATEWEB_TEST_SQLSERVER")
            ?? throw new InvalidOperationException("DONATEWEB_TEST_SQLSERVER is not set.");
        var databaseName = $"DonateWebTest_{Guid.NewGuid():N}";
        var masterBuilder = new SqlConnectionStringBuilder(configuredConnection) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new SqlConnectionStringBuilder(configuredConnection) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(databaseBuilder.ConnectionString)
            .Options;
        var context = new AppDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new SqlServerDatabase(databaseBuilder.ConnectionString, databaseName, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        var masterBuilder = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(masterBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]";
        await command.ExecuteNonQueryAsync();
    }
}
