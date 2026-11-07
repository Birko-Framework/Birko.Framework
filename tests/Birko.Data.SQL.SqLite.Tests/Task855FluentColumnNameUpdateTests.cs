using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using LowLimitSqLiteConnector = Birko.Data.SQL.SqLite.Tests.Task833InListChunkingTests.LowLimitSqLiteConnector;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-855. A column renamed by a fluent <c>HasColumnName</c> exists only on the table the build produced; the lambda
/// resolver (<c>DataBase.GetFieldFromLambda</c> / <c>GetField</c>) built its own field from the property and wrote the
/// C# name, so every <c>PropertyUpdate</c> kind on a renamed column failed with "no such column" (rules 16, 48). The
/// update path, the connector's expression update and the IN-list planner's written column now read the table's field.
/// </summary>
public sealed class Task855FluentColumnNameUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t855-{Guid.NewGuid():N}");
    private readonly List<SqLiteSettings> _settings = new();

    public Task855FluentColumnNameUpdateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var settings in _settings) SqlitePool.ClearFor(settings);
        TestTempDirectory.Delete(_root);
    }

    public class RenamedRow : AbstractModel
    {
        public int Score { get; set; }
        public int Points { get; set; }
        public decimal Amount { get; set; }
        public decimal Price { get; set; }
    }

    /// <summary>Points is stored as <c>Pts</c>, Amount as <c>Amt</c>, Price as <c>Prc</c>.</summary>
    private sealed class RenamedRowMapping : IModelMapping<RenamedRow>
    {
        public void Configure(ModelMap<RenamedRow> map)
        {
            map.ToTable("T855Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Score);
            map.Property(x => x.Points).HasColumnName("Pts");
            map.Property(x => x.Amount).HasColumnName("Amt");
            map.Property(x => x.Price).HasColumnName("Prc");
        }
    }

    private sealed class LowLimitStore : AsyncSQLiteStore<RenamedRow>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    private sealed class LowLimitSyncStore : SQLiteStore<RenamedRow>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitSyncStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    private sealed class Fixture
    {
        public required SqLiteSettings Settings { get; init; }
        public required LowLimitSqLiteConnector Connector { get; init; }
        public required LowLimitStore Store { get; init; }
        public required LowLimitSyncStore SyncStore { get; init; }
        public List<string> Executed { get; } = new();

        public IEnumerable<string> Updates => Executed.Where(s => s.StartsWith("UPDATE", StringComparison.Ordinal));
    }

    /// <summary>60 rows; row i has every column at i. The connector allows 40 parameters in chunks of 10.</summary>
    private async Task<Fixture> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RenamedRowMapping());
        registry.ApplyToDatabase();
        var settings = new SqLiteSettings(_root, $"t855-{Guid.NewGuid():N}.db");
        _settings.Add(settings);

        var rows = Enumerable.Range(0, 60).Select(i => new RenamedRow
        {
            Guid = Guid.NewGuid(), Score = i, Points = i, Amount = i, Price = i,
        }).ToList();
        var seed = new AsyncSQLiteStore<RenamedRow>();
        seed.SetSettings(settings);
        await seed.CreateAsync(rows, ct: CancellationToken.None);

        var connector = new LowLimitSqLiteConnector(settings);
        var store = new LowLimitStore(connector);
        store.SetSettings(settings);
        var syncStore = new LowLimitSyncStore(connector);
        syncStore.SetSettings(settings);
        await store.CountAsync(x => x.Score < 0, CancellationToken.None);
        syncStore.Count(x => x.Score < 0);

        var fixture = new Fixture { Settings = settings, Connector = connector, Store = store, SyncStore = syncStore };
        connector.OnExecute += text => fixture.Executed.Add(text);
        return fixture;
    }

    private static long RawCount(Fixture f, string where)
    {
        using var connection = new SqliteConnection(f.Settings.GetConnectionString());
        SqLiteDecimal.Register(connection);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM T855Rows WHERE " + where;
        return (long)command.ExecuteScalar()!;
    }

    private static readonly List<int> Thirty = Enumerable.Range(0, 30).ToList();
    private static readonly List<int> FortyFive = Enumerable.Range(0, 45).ToList();

    [Fact]
    public async Task Set_on_a_renamed_column_writes_the_mapped_column()
    {
        var f = await SeedAsync();

        await f.Store.UpdateAsync(x => x.Score == 5, new PropertyUpdate<RenamedRow>().Set(x => x.Points, 500), CancellationToken.None);

        RawCount(f, "Pts = 500 AND Score = 5").Should().Be(1);
    }

    [Fact]
    public async Task Increment_on_a_renamed_column_writes_the_mapped_column()
    {
        var f = await SeedAsync();

        await f.Store.UpdateAsync(x => x.Score == 5, new PropertyUpdate<RenamedRow>().Increment(x => x.Points, 100), CancellationToken.None);

        RawCount(f, "Pts = 105 AND Score = 5").Should().Be(1);
    }

    [Fact]
    public async Task Compute_on_a_renamed_column_from_a_renamed_column_writes_the_mapped_column()
    {
        var f = await SeedAsync();

        await f.Store.UpdateAsync(x => x.Score == 5,
            new PropertyUpdate<RenamedRow>().Compute(x => x.Amount, x => x.Price * 2m + 1m), CancellationToken.None);

        RawCount(f, "Amt = 11 AND Score = 5").Should().Be(1);
    }

    [Fact]
    public async Task The_sync_store_writes_the_mapped_column()
    {
        var f = await SeedAsync();

        await Task.Run(() => f.SyncStore.Update(x => x.Score == 5,
            new PropertyUpdate<RenamedRow>().Set(x => x.Price, 7m).Increment(x => x.Points, 1)));

        RawCount(f, "Prc = 7 AND Pts = 6 AND Score = 5").Should().Be(1);
    }

    [Fact]
    public async Task The_connector_expression_update_writes_the_mapped_column()
    {
        var f = await SeedAsync();
        var conditions = DataBase.ParseConditionExpression((Expression<Func<RenamedRow, bool>>)(x => x.Score == 5));
        var updates = new Dictionary<Expression<Func<RenamedRow, int>>, Expression<Func<RenamedRow, int>>> { { x => x.Points, x => x.Points + 1 } };

        f.Connector.Update<RenamedRow, int>("T855Rows", updates, conditions);
        await f.Connector.UpdateAsync<RenamedRow, int>("T855Rows", updates, conditions);

        RawCount(f, "Pts = 7 AND Score = 5").Should().Be(1);
    }

    [Fact]
    public async Task A_filter_and_an_order_on_a_renamed_column_read_the_mapped_column()
    {
        var f = await SeedAsync();

        var matched = await f.Store.CountAsync(x => x.Points == 5, CancellationToken.None);
        var ordered = (await f.Store.ReadAsync(x => x.Score < 5, OrderBy<RenamedRow>.ByDescending(x => x.Points), ct: CancellationToken.None)).ToList();

        matched.Should().Be(1);
        ordered.Select(x => x.Points).Should().Equal(4, 3, 2, 1, 0);
    }

    [Fact]
    public async Task An_increment_of_a_renamed_column_its_in_list_filters_is_one_statement_and_applies_once()
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        await f.Store.UpdateAsync(x => Thirty.Contains(x.Points + 0), new PropertyUpdate<RenamedRow>().Increment(x => x.Points, 1), CancellationToken.None);

        f.Updates.Should().ContainSingle("the planner must see that the update writes Pts, which the list reads");
        foreach (var value in new[] { 1, 10, 11, 20, 21, 29 }) RawCount(f, $"Pts = {value}").Should().Be(1, $"Pts {value}");
        RawCount(f, "Pts = 0").Should().Be(0);
        RawCount(f, "Pts = 30").Should().Be(2);
    }

    [Fact]
    public async Task Past_the_limit_an_increment_of_a_renamed_column_its_in_list_filters_is_refused()
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        var act = () => f.Store.UpdateAsync(x => FortyFive.Contains(x.Points + 0), new PropertyUpdate<RenamedRow>().Increment(x => x.Points, 1), CancellationToken.None);

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("writes the filtered column");
        f.Updates.Should().BeEmpty();
        RawCount(f, "Pts = 60").Should().Be(0);
    }

    [Fact]
    public async Task The_lambda_resolvers_answer_the_tables_field()
    {
        await SeedAsync();
        var fromTable = DataBase.LoadTable(typeof(RenamedRow)).GetFieldByPropertyName(nameof(RenamedRow.Points));

        var generic = DataBase.GetField<RenamedRow, int>(x => x.Points);
        var erased = DataBase.GetFieldFromLambda((Expression<Func<RenamedRow, int>>)(x => x.Points));

        fromTable!.Name.Should().Be("Pts");
        generic.Should().BeSameAs(fromTable);
        erased.Should().BeSameAs(fromTable);
    }
}
