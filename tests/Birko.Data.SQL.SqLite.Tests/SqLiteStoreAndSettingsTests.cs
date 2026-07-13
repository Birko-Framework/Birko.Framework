using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// CR-M135 (SQL store CRUD + SqlUnitOfWork), CR-M144 (bulk ops retry SQLITE_BUSY/LOCKED — exercised
/// functionally + via the transient detector), and CR-M145 (native bulk CRUD round-trips, settings,
/// Guid conversion). Runs against a real on-disk SQLite database via the store/connector.
/// </summary>
public class SqLiteStoreCrudTests : IDisposable
{
    private readonly string _root;

    public SqLiteStoreCrudTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-crud-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private (AsyncSQLiteStore<Widget> store, SqLiteConnector connector) NewStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        // Create the schema up front against the same db file the store will open.
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "crud.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Widget) });

        var store = new AsyncSQLiteStore<Widget>();
        store.SetSettings(new SqLiteSettings(_root, "crud.db"));
        return (store, connector);
    }

    [Fact]
    public async Task BulkCrud_RoundTrips_ThroughRetryWrappedNativeOps()
    {
        var (store, _) = NewStore();

        var widgets = Enumerable.Range(0, 5)
            .Select(i => new Widget { Name = "w" + i, Amount = i })
            .ToList();

        // CreateAsync(IEnumerable) → BulkInsertAsync (CR-M144 retry-wrapped path).
        await store.CreateAsync(widgets);

        var all = (await store.ReadAsync(x => true)).ToList();
        all.Should().HaveCount(5);
        all.Select(w => w.Name).Should().BeEquivalentTo(new[] { "w0", "w1", "w2", "w3", "w4" });

        // Guid round-trips (CR-M145 Guid→string conversion): the store assigned each a Guid on insert.
        var first = widgets[0];
        first.Guid.Should().NotBeNull();
        var reread = await store.ReadAsync(first.Guid!.Value);
        reread.Should().NotBeNull();
        reread!.Name.Should().Be("w0");

        // UpdateAsync(IEnumerable) → BulkUpdateAsync.
        foreach (var w in widgets) w.Name = w.Name + "-x";
        await store.UpdateAsync(widgets);
        (await store.ReadFirstAsync(x => x.Amount == 2))!.Name.Should().Be("w2-x");

        // DeleteAsync(IEnumerable) → BulkDeleteAsync.
        await store.DeleteAsync(widgets.Take(2).ToList());
        (await store.ReadAsync(x => true)).Should().HaveCount(3);
    }

    [Fact]
    public async Task SingleCrud_RoundTrips()
    {
        var (store, _) = NewStore();

        var w = new Widget { Name = "solo", Amount = 42 };
        var id = await store.CreateAsync(w);
        id.Should().NotBe(Guid.Empty);

        var loaded = await store.ReadAsync(id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("solo");
        loaded.Amount.Should().Be(42);

        loaded.Name = "renamed";
        await store.UpdateAsync(loaded);
        (await store.ReadAsync(id))!.Name.Should().Be("renamed");

        (await store.CountAsync()).Should().Be(1);

        await store.DeleteAsync(loaded);
        (await store.ReadAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task SqlUnitOfWork_CommitPersists_RollbackDiscards()
    {
        var (_, connector) = NewStore();
        var settings = new SqLiteSettings(_root, "crud.db");

        // Rollback: an insert inside a rolled-back transaction must not persist.
        await using (var uow = new SqlUnitOfWork(connector, settings))
        {
            await uow.BeginAsync();
            uow.IsActive.Should().BeTrue();
            var cmd = uow.Context!.Connection.CreateCommand();
            cmd.Transaction = uow.Context.Transaction;
            cmd.CommandText = "INSERT INTO Widgets (Guid, Name, Amount) VALUES (@g, 'rolled', 1)";
            AddParam(cmd, "@g", Guid.NewGuid().ToString());
            cmd.ExecuteNonQuery();
            await uow.RollbackAsync();
        }
        CountRows(settings).Should().Be(0);

        // Commit: the insert persists.
        await using (var uow = new SqlUnitOfWork(connector, settings))
        {
            await uow.BeginAsync();
            var cmd = uow.Context!.Connection.CreateCommand();
            cmd.Transaction = uow.Context.Transaction;
            cmd.CommandText = "INSERT INTO Widgets (Guid, Name, Amount) VALUES (@g, 'committed', 2)";
            AddParam(cmd, "@g", Guid.NewGuid().ToString());
            cmd.ExecuteNonQuery();
            await uow.CommitAsync();
        }
        CountRows(settings).Should().Be(1);
    }

    [Fact]
    public async Task SqlUnitOfWork_StateMachine_Guards()
    {
        var (_, connector) = NewStore();
        var settings = new SqLiteSettings(_root, "crud.db");

        var uow = new SqlUnitOfWork(connector, settings);

        // Commit/Rollback with no active transaction.
        await Assert.ThrowsAsync<Birko.Data.Patterns.UnitOfWork.NoActiveTransactionException>(() => uow.CommitAsync());
        await Assert.ThrowsAsync<Birko.Data.Patterns.UnitOfWork.NoActiveTransactionException>(() => uow.RollbackAsync());

        await uow.BeginAsync();
        // Double-begin.
        await Assert.ThrowsAsync<Birko.Data.Patterns.UnitOfWork.TransactionAlreadyActiveException>(() => uow.BeginAsync());
        await uow.CommitAsync();

        await uow.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => uow.BeginAsync());
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static long CountRows(SqLiteSettings settings)
    {
        using var conn = new SqliteConnection(settings.GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Widgets";
        return (long)cmd.ExecuteScalar()!;
    }
}

/// <summary>CR-M145: SqLiteSettings pure logic — connection string assembly, Path, LoadFrom.</summary>
public class SqLiteSettingsTests
{
    [Fact]
    public void GetConnectionString_IncludesDataSourceAndTimeout()
    {
        var s = new SqLiteSettings("C:\\data", "app.db") { CommandTimeout = 45 };
        var cs = s.GetConnectionString();
        cs.Should().Contain("Data Source=" + Path.Combine("C:\\data", "app.db"));
        cs.Should().Contain("Default Timeout=45");
        cs.Should().NotContain("Password=");
    }

    [Fact]
    public void GetConnectionString_IncludesPasswordWhenSet()
    {
        var s = new SqLiteSettings("C:\\data", "app.db", "secret");
        s.GetConnectionString().Should().Contain("Password=secret");
    }

    [Fact]
    public void Path_IsNullWhenLocationOrNameMissing()
    {
        new SqLiteSettings().Path.Should().BeNull();
        new SqLiteSettings("C:\\data", "app.db").Path.Should().Be(Path.Combine("C:\\data", "app.db"));
    }

    [Fact]
    public void LoadFrom_CopiesCommandTimeout()
    {
        var src = new SqLiteSettings("C:\\data", "app.db") { CommandTimeout = 99 };
        var dst = new SqLiteSettings();
        dst.LoadFrom(src);
        dst.CommandTimeout.Should().Be(99);
        dst.Location.Should().Be("C:\\data");
        dst.Name.Should().Be("app.db");
    }

    [Fact]
    public void LoadFrom_ForeignSettings_UsesBase_DoesNotThrow()
    {
        var dst = new SqLiteSettings { CommandTimeout = 7 };
        var foreign = new Settings("loc", "nm");
        dst.LoadFrom(foreign); // base copy path — must not throw, timeout untouched
        dst.CommandTimeout.Should().Be(7);
        dst.Location.Should().Be("loc");
    }

    [Fact]
    public void LoadFrom_Null_IsNoOp()
    {
        var dst = new SqLiteSettings { CommandTimeout = 5 };
        dst.LoadFrom((SqLiteSettings)null!);
        dst.CommandTimeout.Should().Be(5);
    }
}

/// <summary>CR-M144: the transient detector flags exactly SQLITE_BUSY(5)/SQLITE_LOCKED(6) (plus base cases).</summary>
public class SqLiteTransientDetectionTests
{
    private static SqLiteConnector NewConnector()
        => new SqLiteConnector(new SqLiteSettings(Path.Combine(Path.GetTempPath(), "unused"), "x.db"));

    [Theory]
    [InlineData(5, true)]   // SQLITE_BUSY
    [InlineData(6, true)]   // SQLITE_LOCKED
    [InlineData(1, false)]  // SQLITE_ERROR
    [InlineData(19, false)] // SQLITE_CONSTRAINT
    public void IsTransientException_FlagsBusyAndLockedOnly(int errorCode, bool expected)
    {
        var connector = NewConnector();
        var ex = new SqliteException("boom", errorCode);
        connector.IsTransientException(ex).Should().Be(expected);
    }

    [Fact]
    public void IsTransientException_TimeoutIsTransient_GenericIsNot()
    {
        var connector = NewConnector();
        connector.IsTransientException(new TimeoutException()).Should().BeTrue();
        connector.IsTransientException(new InvalidOperationException()).Should().BeFalse();
    }
}
