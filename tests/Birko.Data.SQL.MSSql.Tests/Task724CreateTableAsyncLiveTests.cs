using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-724, live on SQL Server. Measured before the fix: the async <c>CreateTableAsync</c> sent its own
/// <c>CREATE TABLE IF NOT EXISTS</c> past the provider's statement, which T-SQL does not have, so it failed on SQL Server
/// every time. It is the sync path now, which SQL Server's connector renders.
/// </summary>
public sealed class Task724CreateTableAsyncLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task724CreateTableAsyncLiveTests(ITestOutputHelper output) => _output = output;

    [Table("T724MsRows")]
    public class T724MsRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }

        public string? Name { get; set; }
    }

    [Fact]
    public async Task CreateTableAsync_creates_the_table_and_is_an_ensure()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            _output.WriteLine("SKIPPED: set BIRKO_MSSQL_HOST.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException("BIRKO_REQUIRE_LIVE");
            return;
        }
        var connector = new MSSqlConnector(new MSSqlSettings(host,
            Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
            Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
            Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
            int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
        { TrustServerCertificate = true });
        try
        {
            await connector.CreateTableAsync(new[] { typeof(T724MsRow) });
            await connector.CreateTableAsync(new[] { typeof(T724MsRow) });

            connector.TablesCreated.Should().ContainKey("T724MsRows");
            (await connector.SelectCountAsync(new[] { typeof(T724MsRow) }, (System.Linq.Expressions.LambdaExpression?)null)).Should().Be(0);
        }
        finally
        {
            try { connector.DropTable(new[] { typeof(T724MsRow) }); } catch { }
        }
    }
}
