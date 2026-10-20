using Birko.Configuration;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.IndexManagement;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.IndexManagement;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.IndexManagement;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.SQL;
using Birko.Security.Grants.SQL.Mappings;
using Birko.Security.Grants.SQL.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Security.Grants.SQL.Tests;

/// <summary>
/// TASK-693, live: the grant lookup indexes on the server providers. Measured before the fix: created only through the
/// index manager, they left ResourceType and Email unbounded, LONGTEXT on MySQL (ERROR 1170) and NVARCHAR(MAX) on
/// SQL Server (Msg 1919), and <c>GrantSqlIndexes.EnsureAsync</c> threw on both. PostgreSQL indexed them. They run when
/// their <c>BIRKO_*_HOST</c> is set (live-tests.yml); <c>BIRKO_REQUIRE_LIVE</c> makes a missing host a failure.
/// </summary>
public abstract class ProviderIndexLiveTests<DB> where DB : AbstractConnector
{
    private readonly ITestOutputHelper _output;
    protected ProviderIndexLiveTests(ITestOutputHelper output) => _output = output;

    protected abstract string HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IIndexManager Indexes(AbstractConnectorBase connector);
    protected abstract string CurrentSchemaSql { get; }

    /// <summary>The provider bounds an index-key string (rule 37): MySQL and SQL Server, whose TEXT types cannot be keys.</summary>
    protected abstract bool BoundsIndexKeys { get; }

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    [Fact]
    public async Task Every_lookup_index_is_created()
    {
        if (!RequireServer()) return;
        SqliteGrantsFixture.EnsureMapped();
        var grants = new SqlResourceGrantStore<DB>();
        var invitations = new SqlInvitationStore<DB>();
        grants.SetSettings(Settings());
        invitations.SetSettings(Settings());
        // A fresh shape: a table from an earlier run keeps whatever types it was created with.
        try { grants.Connector!.DropTable(new[] { typeof(ResourceGrant) }); } catch { }
        try { invitations.Connector!.DropTable(new[] { typeof(Invitation) }); } catch { }

        // Schema-ensure alone: the mappings declare the indexes, so the store's own init creates them.
        await grants.InitAsync();
        await invitations.InitAsync();

        var types = new Dictionary<string, string>();
        foreach (var table in new[] { ResourceGrantMapping.Table, InvitationMapping.Table })
        {
            foreach (var (column, type) in await ColumnTypesAsync(grants.Connector!, table))
            {
                types[$"{table}.{column}"] = type;
                _output.WriteLine($"{table}.{column}: {type}");
            }
        }
        var manager = Indexes(grants.Connector!);
        (await manager.ExistsAsync(GrantSqlIndexes.GrantsByUser, ResourceGrantMapping.Table)).Should().BeTrue();
        (await manager.ExistsAsync(GrantSqlIndexes.GrantsByResource, ResourceGrantMapping.Table)).Should().BeTrue();
        (await manager.ExistsAsync(GrantSqlIndexes.InvitationsByEmail, InvitationMapping.Table)).Should().BeTrue();
        grants.Connector!.IndexCreationFailures.Where(f => f.TableName == ResourceGrantMapping.Table || f.TableName == InvitationMapping.Table)
            .Should().BeEmpty();
        if (BoundsIndexKeys)
        {
            // Rule 63: the stored side read from the catalogue, never from the driver.
            types[$"{ResourceGrantMapping.Table}.ResourceType"].Should().EndWith("(255)");
            types[$"{InvitationMapping.Table}.Email"].Should().EndWith("(255)");
        }

        // The startup call verifies and finds them there.
        await GrantSqlIndexes.EnsureAsync(grants, invitations, Indexes);

        // A restart: fresh stores ensure the schema again over the existing indexes, and nothing is reported.
        var again = new SqlResourceGrantStore<DB>();
        var invitationsAgain = new SqlInvitationStore<DB>();
        again.SetSettings(Settings());
        invitationsAgain.SetSettings(Settings());
        await again.InitAsync();
        await invitationsAgain.InitAsync();
        await GrantSqlIndexes.EnsureAsync(again, invitationsAgain, Indexes);
        again.Connector!.IndexCreationFailures.Where(f => f.TableName == ResourceGrantMapping.Table || f.TableName == InvitationMapping.Table)
            .Should().BeEmpty();
    }

    /// <summary>Each column's type as the catalogue reports it (rule 63), from information_schema.</summary>
    private async Task<List<(string Column, string Type)>> ColumnTypesAsync(AbstractConnectorBase connector, string table)
    {
        await using var connection = connector.CreateConnection(connector.Settings);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH FROM information_schema.columns "
            + $"WHERE LOWER(TABLE_NAME) = LOWER(@table) AND TABLE_SCHEMA = {CurrentSchemaSql} ORDER BY ORDINAL_POSITION";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        var result = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), reader.GetString(1) + (reader.IsDBNull(2) ? "" : $"({reader.GetValue(2)})")));
        return result;
    }
}

public sealed class PostgreSqlProviderIndexLiveTests : ProviderIndexLiveTests<PostgreSQLConnector>
{
    public PostgreSqlProviderIndexLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_PG_HOST";
    protected override string CurrentSchemaSql => "current_schema()";
    protected override bool BoundsIndexKeys => false;
    protected override PasswordSettings Settings() => new PostgreSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new PostgreSqlIndexManager(connector);
}

public sealed class MySqlProviderIndexLiveTests : ProviderIndexLiveTests<MySQLConnector>
{
    public MySqlProviderIndexLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_MYSQL_HOST";
    protected override string CurrentSchemaSql => "DATABASE()";
    protected override bool BoundsIndexKeys => true;
    protected override PasswordSettings Settings() => new MySqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new MySqlIndexManager(connector);
}

public sealed class MSSqlProviderIndexLiveTests : ProviderIndexLiveTests<MSSqlConnector>
{
    public MSSqlProviderIndexLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_MSSQL_HOST";
    protected override string CurrentSchemaSql => "SCHEMA_NAME()";
    protected override bool BoundsIndexKeys => true;
    protected override PasswordSettings Settings() => new MSSqlSettings(
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IIndexManager Indexes(AbstractConnectorBase connector) => new MSSqlIndexManager(connector);
}
