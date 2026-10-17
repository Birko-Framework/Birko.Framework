using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

[Table("T649Rows")]
public class T649Row : AbstractModel, ISoftDeletable
{
    public string? Name { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>TASK-649, live: Save of a soft-deleted row refuses, Restore brings it back (SQLite, and PostgreSQL when BIRKO_PG_HOST is set).</summary>
public abstract class Task649SoftDeletedSaveLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task649SoftDeletedSaveLiveTestsBase(ITestOutputHelper output) => Output = output;
    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();
    private AsyncDataBaseBulkStore<DB, T649Row>? _raw;

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public void Dispose()
    {
        try { _raw?.Connector?.DropTable(new[] { typeof(T649Row) }); } catch { }
    }

    [Fact]
    public async Task Save_of_a_soft_deleted_row_refuses_and_Restore_brings_it_back()
    {
        if (!Live()) return;
        _raw = new AsyncDataBaseBulkStore<DB, T649Row>();
        _raw.SetSettings(Settings());
        try { _raw.Connector?.DropTable(new[] { typeof(T649Row) }); } catch { }
        await _raw.InitAsync();
        var store = new AsyncSoftDeleteBulkStoreWrapper<IAsyncBulkStore<T649Row>, T649Row>(_raw, new SystemDateTimeProvider());
        var row = new T649Row { Name = "a" };
        await store.CreateAsync(row);
        await store.DeleteAsync(row);
        row.Name = "b";

        var save = () => store.SaveAsync(row);
        await save.Should().ThrowAsync<EntitySoftDeletedException>();
        (await _raw.ReadAsync(row.Guid!.Value))!.Name.Should().Be("a");

        await store.RestoreAsync(row);
        var back = (await store.ReadAsync(row.Guid!.Value))!;
        back.Name.Should().Be("b");
        back.DeletedAt.Should().BeNull();
        (await _raw.CountAsync()).Should().Be(1);
    }
}

public sealed class SqliteTask649SoftDeletedSaveLiveTests : Task649SoftDeletedSaveLiveTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t649-{Guid.NewGuid():N}.db");
    public SqliteTask649SoftDeletedSaveLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override Birko.Configuration.Settings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
}

public sealed class PostgreSqlTask649SoftDeletedSaveLiveTests : Task649SoftDeletedSaveLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask649SoftDeletedSaveLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override Birko.Configuration.Settings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}
