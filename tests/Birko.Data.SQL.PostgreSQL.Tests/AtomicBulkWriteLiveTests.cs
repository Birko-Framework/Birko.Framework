using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.SQL.Stores;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// SH-M028 (TASK-629) on PostgreSQL: a bulk collection write outside a boundary is one transaction. Measured by rows, and on
/// the provider's own bulk paths, which a portable test double would not exercise.
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class AtomicBulkWriteLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_PG_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public AtomicBulkWriteLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live PostgreSQL. Set BIRKO_PG_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static PostgreSqlSettings Settings() => new(Host!, Database, User, Password) { Port = Port };

    private static void Exec(string sql)
    {
        using var connection = new NpgsqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("PgAtomic")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
    }

    private DataBaseBulkStore<PostgreSQLConnector, Row> Store()
    {
        Exec("DROP TABLE IF EXISTS \"PgAtomic\"");
        var store = new DataBaseBulkStore<PostgreSQLConnector, Row>();
        store.SetSettings(Settings());
        return store;
    }

    private AsyncDataBaseBulkStore<PostgreSQLConnector, Row> AsyncStore()
    {
        Exec("DROP TABLE IF EXISTS \"PgAtomic\"");
        var store = new AsyncDataBaseBulkStore<PostgreSQLConnector, Row>();
        store.SetSettings(Settings());
        return store;
    }

    [Fact]
    public void A_bulk_create_that_fails_part_way_leaves_nothing_written()
    {
        if (!RequireServer()) return;
        var store = Store();
        var stored = new Row { Name = "stored" };
        store.Create(stored);

        var act = () => store.Create(new[] { new Row { Name = "first" }, new Row { Name = "second" }, new Row { Guid = stored.Guid, Name = "dup" } });

        act.Should().Throw<StoreException>();
        store.Read(x => x.Name != null, null, null, null).Select(r => r.Name).Should().Equal("stored");
    }

    [Fact]
    public async Task An_async_bulk_create_that_fails_part_way_leaves_nothing_written()
    {
        if (!RequireServer()) return;
        var store = AsyncStore();
        var stored = new Row { Name = "stored" };
        await store.CreateAsync(stored);

        var act = () => store.CreateAsync(new[] { new Row { Name = "first" }, new Row { Name = "second" }, new Row { Guid = stored.Guid, Name = "dup" } });

        await act.Should().ThrowAsync<StoreException>();
        (await store.ReadAsync(x => x.Name != null, null, null, null)).Select(r => r.Name).Should().Equal("stored");
    }

    /// <summary>TASK-555 kept: the stored items are written, then the missing ones reported; the transaction commits.</summary>
    [Fact]
    public async Task A_bulk_update_with_a_missing_item_still_writes_the_stored_ones()
    {
        if (!RequireServer()) return;
        var store = AsyncStore();
        var a = new Row { Name = "a" };
        await store.CreateAsync(a);
        a.Name = "a2";

        var act = () => store.UpdateAsync(new[] { a, new Row { Guid = Guid.NewGuid(), Name = "missing" } });

        await act.Should().ThrowAsync<EntityNotFoundException>();
        (await store.ReadFirstAsync(x => x.Guid == a.Guid))!.Name.Should().Be("a2");
    }
}
