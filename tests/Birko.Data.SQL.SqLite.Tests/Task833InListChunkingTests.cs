using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Expressions;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using Birko.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-833 (FIELD-051, Symbio): an <c>ids.Contains(x.Col)</c> list past the provider's parameter limit is split into
/// chunks by the connector, transparently, on every funnel: read, count, delete, update. The connector here is SQLite with
/// a limit of 40 and a chunk size of 10, so a list of a few dozen values exercises what 32,766 would in production. The
/// oracle is the same filter through an ordinary store on the same file, which sends one statement.
/// </summary>
public sealed class Task833InListChunkingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t833-{Guid.NewGuid():N}");
    private readonly List<SqLiteSettings> _settings = new();

    public Task833InListChunkingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var settings in _settings) SqlitePool.ClearFor(settings);
        TestTempDirectory.Delete(_root);
    }

    public class ChunkRow : AbstractModel, ISoftDeletable
    {
        public string? Name { get; set; }
        public int Score { get; set; }
        public Guid TenantGuid { get; set; }
        public DateTime? DeletedAt { get; set; }
        public decimal Amount { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class ChunkRowMapping : IModelMapping<ChunkRow>
    {
        public void Configure(ModelMap<ChunkRow> map)
        {
            map.ToTable("T833Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name);
            map.Property(x => x.Score);
            map.Property(x => x.TenantGuid);
            map.Property(x => x.DeletedAt);
            map.Property(x => x.Amount);
            map.Property(x => x.Price);
        }
    }

    /// <summary>SQLite with a limit of 40 parameters and chunks of 10, and a write that can be made to fail at statement k.</summary>
    public sealed class LowLimitSqLiteConnector : SqLiteConnector
    {
        public LowLimitSqLiteConnector(Birko.Configuration.PasswordSettings settings) : base(settings) { }

        public override int MaxCommandParameters => 40;

        public override int PreferredInListChunkSize => 10;

        private int _writes;
        private int _failAt;

        /// <summary>The <paramref name="k"/>-th write statement from now throws before it runs; 0 disarms.</summary>
        public void FailAtWrite(int k)
        {
            _writes = 0;
            _failAt = k;
        }

        private void Tick()
        {
            if (_failAt > 0 && ++_writes == _failAt) throw new InvalidOperationException($"injected at write {_failAt}");
        }

        public override void DoCommandWithTransaction(Action<DbCommand> createCommand, Action<DbCommand> executeCommand, bool isLock = false)
        {
            Tick();
            base.DoCommandWithTransaction(createCommand, executeCommand, isLock);
        }

        public override Task DoCommandWithTransactionAsync(Func<DbCommand, Task> createCommand, Func<DbCommand, Task> executeCommand, bool isLock = false, CancellationToken ct = default)
        {
            Tick();
            return base.DoCommandWithTransactionAsync(createCommand, executeCommand, isLock, ct);
        }
    }

    private sealed class LowLimitStore : AsyncSQLiteStore<ChunkRow>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    private sealed class LowLimitSyncStore : SQLiteStore<ChunkRow>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitSyncStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        public DateTimeOffset OffsetUtcNow => new(UtcNow);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class Fixture
    {
        public required SqLiteSettings Settings { get; init; }
        public required LowLimitSqLiteConnector Connector { get; init; }
        public required LowLimitStore Store { get; init; }
        public required LowLimitSyncStore SyncStore { get; init; }
        public required AsyncSQLiteStore<ChunkRow> Oracle { get; init; }
        public required List<ChunkRow> Rows { get; init; }
        public required Guid TenantA { get; init; }
        public List<string> Executed { get; } = new();
    }

    /// <summary>60 rows: 40 of tenant A (5 of them soft-deleted), 20 of tenant B.</summary>
    private async Task<Fixture> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new ChunkRowMapping());
        registry.ApplyToDatabase();
        var settings = new SqLiteSettings(_root, $"t833-{Guid.NewGuid():N}.db");
        _settings.Add(settings);

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rows = Enumerable.Range(0, 60).Select(i => new ChunkRow
        {
            Guid = Guid.NewGuid(),
            Name = $"r{i}",
            Score = i,
            Amount = i,
            Price = i,
            TenantGuid = i < 40 ? tenantA : tenantB,
            DeletedAt = i < 5 ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        }).ToList();

        var oracle = new AsyncSQLiteStore<ChunkRow>();
        oracle.SetSettings(settings);
        await oracle.CreateAsync(rows, ct: CancellationToken.None);

        var connector = new LowLimitSqLiteConnector(settings);
        var store = new LowLimitStore(connector);
        store.SetSettings(settings);
        var syncStore = new LowLimitSyncStore(connector);
        syncStore.SetSettings(settings);
        // Warm both stores so their lazy schema-ensure is not counted as a statement below.
        await store.CountAsync(x => x.Score < 0, CancellationToken.None);
        syncStore.Count(x => x.Score < 0);

        var fixture = new Fixture
        {
            Settings = settings, Connector = connector, Store = store, SyncStore = syncStore, Oracle = oracle,
            Rows = rows, TenantA = tenantA,
        };
        connector.OnExecute += text => fixture.Executed.Add(text);
        return fixture;
    }

    /// <summary>Every seeded id, 5 unknown ones, and the first 10 again: 75 values, 65 distinct.</summary>
    private static List<Guid> Ids(Fixture f)
    {
        var ids = f.Rows.Select(r => r.Guid!.Value).ToList();
        ids.AddRange(Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()));
        ids.AddRange(ids.Take(10));
        return ids;
    }

    /// <summary>The caller's filter AND the tenant predicate, composed the way <c>ModelByTenant</c> composes it.</summary>
    private static Expression<Func<ChunkRow, bool>> ForTenant(Expression<Func<ChunkRow, bool>> filter, Guid tenant)
        => ExpressionParameterReplacer.AndAlso(filter, x => x.TenantGuid == tenant);

    private long RawCount(Fixture f, string where = "1 = 1")
    {
        using var connection = new SqliteConnection(f.Settings.GetConnectionString());
        SqLiteDecimal.Register(connection);   // the decimal columns compare through the framework's collation
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T833Rows WHERE " + where;
        return (long)command.ExecuteScalar()!;
    }

    private static readonly Regex InList = new(@"\bIN \(([^)]*)\)", RegexOptions.Compiled);

    /// <summary>Each executed statement's IN list holds between 1 and the chunk size of values.</summary>
    private static void EachStatementHasANonEmptyChunk(IEnumerable<string> statements, int expectedStatements)
    {
        var list = statements.ToList();
        list.Should().HaveCount(expectedStatements);
        foreach (var sql in list)
        {
            var match = InList.Match(sql);
            match.Success.Should().BeTrue(sql);
            var values = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            values.Length.Should().BeInRange(1, 10, sql);
        }
    }

    // ── read ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_read_past_the_limit_returns_what_the_single_statement_returns()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);
        var soft = new AsyncSoftDeleteBulkStoreWrapper<LowLimitStore, ChunkRow>(f.Store, new Clock());
        var oracleSoft = new AsyncSoftDeleteBulkStoreWrapper<AsyncSQLiteStore<ChunkRow>, ChunkRow>(f.Oracle, new Clock());

        var expected = (await oracleSoft.ReadAsync(filter, ct: CancellationToken.None)).Select(x => x.Guid).ToList();
        f.Executed.Clear();
        var actual = (await soft.ReadAsync(filter, ct: CancellationToken.None)).Select(x => x.Guid).ToList();

        expected.Should().HaveCount(35);
        actual.Should().BeEquivalentTo(expected).And.OnlyHaveUniqueItems();
        EachStatementHasANonEmptyChunk(f.Executed, 7);   // 65 distinct values in chunks of 10
    }

    [Fact]
    public async Task A_sync_read_past_the_limit_returns_what_the_single_statement_returns()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        var expected = (await f.Oracle.ReadAsync(filter, ct: CancellationToken.None)).Select(x => x.Guid).ToList();
        f.Executed.Clear();
        var actual = f.SyncStore.Read(filter).Select(x => x.Guid).ToList();

        expected.Should().HaveCount(40);
        actual.Should().BeEquivalentTo(expected).And.OnlyHaveUniqueItems();
        EachStatementHasANonEmptyChunk(f.Executed, 7);
    }

    // ── count ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_count_past_the_limit_is_the_single_statement_s_count()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);
        var soft = new AsyncSoftDeleteBulkStoreWrapper<LowLimitStore, ChunkRow>(f.Store, new Clock());
        var oracleSoft = new AsyncSoftDeleteBulkStoreWrapper<AsyncSQLiteStore<ChunkRow>, ChunkRow>(f.Oracle, new Clock());

        var expected = await oracleSoft.CountAsync(filter, CancellationToken.None);
        f.Executed.Clear();
        var actual = await soft.CountAsync(filter, CancellationToken.None);

        expected.Should().Be(35);
        actual.Should().Be(expected, "duplicates are dropped, so no row is counted in two chunks");
        EachStatementHasANonEmptyChunk(f.Executed, 7);

        f.SyncStore.Count(filter).Should().Be(40);
    }

    // ── delete ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_delete_past_the_limit_removes_exactly_the_matching_rows()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);
        var expected = await f.Oracle.CountAsync(filter, CancellationToken.None);

        f.Executed.Clear();
        await f.Store.DeleteAsync(filter, CancellationToken.None);

        expected.Should().Be(40);
        RawCount(f).Should().Be(60 - expected);
        (await f.Oracle.CountAsync(x => x.TenantGuid == f.TenantA, CancellationToken.None)).Should().Be(0);
        EachStatementHasANonEmptyChunk(f.Executed.Where(s => s.StartsWith("DELETE")), 7);
    }

    [Fact]
    public async Task A_sync_delete_past_the_limit_removes_exactly_the_matching_rows()
    {
        var f = await SeedAsync();
        var ids = f.Rows.Skip(20).Select(r => r.Guid!.Value).Concat(Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())).ToList();
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        f.SyncStore.Delete(filter);

        RawCount(f).Should().Be(40, "rows 20..39 are tenant A's, and only they go");
        RawCount(f, "Score BETWEEN 20 AND 39").Should().Be(0);
    }

    // ── update ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_update_past_the_limit_changes_exactly_the_matching_rows()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        f.Executed.Clear();
        await f.Store.UpdateAsync(filter, new PropertyUpdate<ChunkRow>().Increment(x => x.Score, 1000), CancellationToken.None);

        RawCount(f, "Score >= 1000").Should().Be(40);
        RawCount(f, "Score >= 2000").Should().Be(0, "a row in two chunks would have been incremented twice");
        EachStatementHasANonEmptyChunk(f.Executed.Where(s => s.StartsWith("UPDATE")), 7);

        f.SyncStore.Update(filter, new PropertyUpdate<ChunkRow>().Increment(x => x.Score, 1000));
        RawCount(f, "Score >= 2000").Should().Be(40);
        RawCount(f, "Score >= 3000").Should().Be(0);
    }

    [Fact]
    public async Task An_action_update_past_the_limit_changes_exactly_the_matching_rows()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        await f.Store.UpdateAsync(filter, row => row.Name = "async", CancellationToken.None);
        RawCount(f, "Name = 'async'").Should().Be(40);

        f.SyncStore.Update(filter, row => row.Name = "sync");
        RawCount(f, "Name = 'sync'").Should().Be(40);
    }

    [Fact]
    public async Task The_matched_count_of_a_chunked_update_is_the_sum_of_its_chunks()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var conditions = DataBase.ParseConditionExpression(ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA)).ToList();
        var fields = new Dictionary<int, string> { { 0, "Score" } };
        var values = new Dictionary<string, object> { { "Score", 7 } };

        var matched = await f.Connector.UpdateAsync("T833Rows", fields, values, conditions);
        var syncMatched = f.Connector.Update("T833Rows", fields, values, conditions);

        matched.Should().Be(40);
        syncMatched.Should().Be(40);
    }

    // ── an update that writes the column it is filtered on (review finding) ──────────────────────────────

    /// <summary>
    /// Chunks run one after another, so a row the first chunk moves INTO a later chunk's values is matched again. Measured
    /// before the fix: scores 0..29 incremented through chunks of 10 left rows 9 and 19 incremented twice.
    /// </summary>
    [Fact]
    public async Task An_increment_of_the_filtered_column_applies_once()
    {
        var f = await SeedAsync();
        var scores = Enumerable.Range(0, 30).ToList();

        f.Executed.Clear();
        await f.Store.UpdateAsync(x => scores.Contains(x.Score), new PropertyUpdate<ChunkRow>().Increment(x => x.Score, 1), CancellationToken.None);

        // Each of 0..29 moved up by exactly one; row 30, not in the list, kept its score.
        foreach (var score in new[] { 1, 10, 11, 20, 21, 29 }) RawCount(f, $"Score = {score}").Should().Be(1, $"score {score}");
        RawCount(f, "Score = 0").Should().Be(0);
        RawCount(f, "Score = 30").Should().Be(2);
        f.Executed.Where(s => s.StartsWith("UPDATE")).Should().ContainSingle("a list that the update rewrites is never chunked");
    }

    [Fact]
    public async Task A_sync_increment_of_the_filtered_column_applies_once()
    {
        var f = await SeedAsync();
        var scores = Enumerable.Range(0, 30).ToList();

        f.SyncStore.Update(x => scores.Contains(x.Score), new PropertyUpdate<ChunkRow>().Increment(x => x.Score, 1));

        foreach (var score in new[] { 1, 10, 11, 20, 21, 29 }) RawCount(f, $"Score = {score}").Should().Be(1, $"score {score}");
        RawCount(f, "Score = 30").Should().Be(2);
    }

    [Fact]
    public async Task A_compute_of_the_filtered_column_applies_once()
    {
        var f = await SeedAsync();
        var amounts = Enumerable.Range(0, 30).Select(i => (decimal)i).ToList();

        f.Executed.Clear();
        await f.Store.UpdateAsync(x => amounts.Contains(x.Amount), new PropertyUpdate<ChunkRow>().Compute(x => x.Amount, x => x.Price + 1m), CancellationToken.None);

        // Chunked, a row the first chunk moved into the second chunk's values was rewritten by it too.
        f.Executed.Where(s => s.StartsWith("UPDATE")).Should().ContainSingle();

        var stored = (await f.Oracle.ReadAsync(ct: CancellationToken.None)).OrderBy(r => r.Score).Select(r => r.Amount).ToList();
        stored.Take(30).Should().Equal(Enumerable.Range(1, 30).Select(i => (decimal)i));
        stored.Skip(30).Should().Equal(Enumerable.Range(30, 30).Select(i => (decimal)i));
    }

    [Fact]
    public async Task An_update_of_the_filtered_column_past_the_limit_is_refused()
    {
        var f = await SeedAsync();
        var scores = Enumerable.Range(0, 45).ToList();
        f.Executed.Clear();

        var act = async () => await f.Store.UpdateAsync(x => scores.Contains(x.Score), new PropertyUpdate<ChunkRow>().Set(x => x.Name, "n").Increment(x => x.Score, 1), CancellationToken.None);

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("writes the filtered column");
        f.Executed.Should().BeEmpty();
        RawCount(f, "Score = 60").Should().Be(0);
    }

    [Fact]
    public async Task An_update_that_writes_a_different_column_still_chunks()
    {
        var f = await SeedAsync();
        var scores = Enumerable.Range(0, 30).ToList();
        f.Executed.Clear();

        await f.Store.UpdateAsync(x => scores.Contains(x.Score), new PropertyUpdate<ChunkRow>().Increment(x => x.Amount, 100m), CancellationToken.None);

        RawCount(f, "Amount >= 100").Should().Be(30);
        EachStatementHasANonEmptyChunk(f.Executed.Where(s => s.StartsWith("UPDATE")), 3);
    }

    // ── one boundary ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public async Task A_failure_at_any_chunk_rolls_back_every_chunk(int k)
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        f.Connector.FailAtWrite(k);
        var delete = async () => await f.Store.DeleteAsync(filter, CancellationToken.None);
        await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage($"injected at write {k}");
        RawCount(f).Should().Be(60, "counted on another connection: no chunk survived");

        f.Connector.FailAtWrite(k);
        var update = () => f.SyncStore.Update(filter, new PropertyUpdate<ChunkRow>().Set(x => x.Score, -1));
        update.Should().Throw<InvalidOperationException>();
        RawCount(f, "Score = -1").Should().Be(0);

        f.Connector.FailAtWrite(k);
        var syncDelete = () => f.SyncStore.Delete(filter);
        syncDelete.Should().Throw<InvalidOperationException>();
        RawCount(f).Should().Be(60);
        f.Connector.FailAtWrite(0);
    }

    [Fact]
    public async Task Inside_a_unit_of_work_that_rolls_back_no_chunk_survives()
    {
        var f = await SeedAsync();
        var ids = Ids(f);
        var filter = ForTenant(x => ids.Contains(x.Guid!.Value), f.TenantA);

        await using (var uow = new SqlUnitOfWork(f.Connector, f.Settings))
        {
            await uow.BeginAsync();
            await f.Store.DeleteAsync(filter, CancellationToken.None);
            await f.Store.UpdateAsync(x => x.TenantGuid != f.TenantA, new PropertyUpdate<ChunkRow>().Set(x => x.Score, -1), CancellationToken.None);
            await uow.RollbackAsync();
        }

        RawCount(f).Should().Be(60);
        RawCount(f, "Score = -1").Should().Be(0);
    }

    // ── refusals: NotSupportedException, and nothing ran ─────────────────────────────────────────────────

    public static IEnumerable<object[]> Refusals() => new[]
    {
        new object[] { "NOT IN read" }, new object[] { "NOT IN delete" }, new object[] { "NOT IN update" },
        new object[] { "string list count" }, new object[] { "OR read" }, new object[] { "sorted read" },
        new object[] { "paged read" }, new object[] { "sync NOT IN delete" }, new object[] { "sync sorted read" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_list_past_the_limit_that_cannot_be_split_is_refused_before_anything_runs(string shape)
    {
        var f = await SeedAsync();
        var ids = f.Rows.Select(r => r.Guid!.Value).Take(45).ToList();
        var names = f.Rows.Select(r => r.Name!).Take(45).ToList();
        f.Executed.Clear();

        Func<Task> act = shape switch
        {
            "NOT IN read" => () => f.Store.ReadAsync(x => !ids.Contains(x.Guid!.Value), ct: CancellationToken.None),
            "NOT IN delete" => () => f.Store.DeleteAsync(x => !ids.Contains(x.Guid!.Value), CancellationToken.None),
            "NOT IN update" => () => f.Store.UpdateAsync(x => !ids.Contains(x.Guid!.Value), new PropertyUpdate<ChunkRow>().Set(x => x.Score, -1), CancellationToken.None),
            "string list count" => () => f.Store.CountAsync(x => names.Contains(x.Name!), CancellationToken.None),
            "OR read" => () => f.Store.ReadAsync(x => ids.Contains(x.Guid!.Value) || x.Score == 1, ct: CancellationToken.None),
            "sorted read" => () => f.Store.ReadAsync(x => ids.Contains(x.Guid!.Value), OrderBy<ChunkRow>.By(x => x.Score), ct: CancellationToken.None),
            "paged read" => () => f.Store.ReadAsync(x => ids.Contains(x.Guid!.Value), null, 5, 0, CancellationToken.None),
            "sync NOT IN delete" => () => Task.Run(() => f.SyncStore.Delete(x => !ids.Contains(x.Guid!.Value))),
            "sync sorted read" => () => Task.Run(() => f.SyncStore.Read(x => ids.Contains(x.Guid!.Value), OrderBy<ChunkRow>.By(x => x.Score)).ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("TASK-833").And.Contain("40");
        f.Executed.Should().BeEmpty();
        RawCount(f).Should().Be(60);
        RawCount(f, "Score = -1").Should().Be(0);
    }

    [Fact]
    public async Task A_sorted_read_within_the_limit_is_one_statement_and_works()
    {
        var f = await SeedAsync();
        var ids = f.Rows.Select(r => r.Guid!.Value).Skip(10).Take(30).ToList();

        var rows = (await f.Store.ReadAsync(x => ids.Contains(x.Guid!.Value), OrderBy<ChunkRow>.ByDescending(x => x.Score), ct: CancellationToken.None)).ToList();

        rows.Select(r => r.Score).Should().Equal(Enumerable.Range(10, 30).Reverse());
        f.Executed.Should().ContainSingle();
    }

    [Fact]
    public async Task A_not_in_within_the_limit_is_one_statement_and_works()
    {
        var f = await SeedAsync();
        var ids = f.Rows.Select(r => r.Guid!.Value).Take(30).ToList();

        await f.Store.DeleteAsync(x => !ids.Contains(x.Guid!.Value), CancellationToken.None);

        RawCount(f).Should().Be(30);
        f.Executed.Should().ContainSingle();
    }

    // ── the provider's ceiling ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SQLite_binds_at_least_the_parameters_the_connector_claims()
    {
        var settings = new SqLiteSettings(_root, "limit.db");
        _settings.Add(settings);
        using var connection = new SqliteConnection(settings.GetConnectionString());
        connection.Open();

        const int SQLITE_LIMIT_VARIABLE_NUMBER = 9;
        var limit = SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLITE_LIMIT_VARIABLE_NUMBER, -1);

        limit.Should().BeGreaterThanOrEqualTo(new SqLiteConnector(settings).MaxCommandParameters);
        new SqLiteConnector(settings).MaxCommandParameters.Should().Be(32766);
    }
}
