using Xunit;

namespace DonateWeb.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DONATEWEB_TEST_SQLSERVER")))
        {
            Skip = "Set DONATEWEB_TEST_SQLSERVER to a SQL Server connection string to run integration tests.";
        }
    }
}
