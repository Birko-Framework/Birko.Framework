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
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using LowLimitSqLiteConnector = Birko.Data.SQL.SqLite.Tests.Task833InListChunkingTests.LowLimitSqLiteConnector;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-849. TASK-833 never splits an update whose SET writes the column its IN list filters, because the chunks run one
/// after another and a row the first chunk moves into a later chunk's values is matched again. Every form an update can
/// write a column in, and every form a filter can name that column in, must be seen as "writes the filtered column": the
/// update stays one statement, and past the provider's limit it is refused. The connector allows 40 parameters in chunks
/// of 10, so a list of 30 values would be split into 3 statements if the check missed it.
/// </summary>
public sealed class Task849WrittenColumnChunkingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t849-{Guid.NewGuid():N}");
    private readonly List<SqLiteSettings> _settings = new();

    public Task849WrittenColumnChunkingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var settings in _settings) SqlitePool.ClearFor(settings);
        TestTempDirectory.Delete(_root);
    }

    public class WrittenRow : AbstractModel
    {
        public int Score { get; set; }
        public int Points { get; set; }
        public decimal Amount { get; set; }
        public decimal Price { get; set; }
    }

    /// <summary><see cref="WrittenRow.Points"/> is stored as <c>Pts</c>; a filter below reads it.</summary>
    private sealed class WrittenRowMapping : IModelMapping<WrittenRow>
    {
        public void Configure(ModelMap<WrittenRow> map)
        {
            map.ToTable("T849Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Score);
            map.Property(x => x.Points).HasColumnName("Pts");
            map.Property(x => x.Amount);
            map.Property(x => x.Price);
        }
    }

    private sealed class LowLimitStore : AsyncSQLiteStore<WrittenRow>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    private sealed class LowLimitSyncStore : SQLiteStore<WrittenRow>
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

    /// <summary>60 rows; row i has every column at i.</summary>
    private async Task<Fixture> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WrittenRowMapping());
        registry.ApplyToDatabase();
        var settings = new SqLiteSettings(_root, $"t849-{Guid.NewGuid():N}.db");
        _settings.Add(settings);

        var rows = Enumerable.Range(0, 60).Select(i => new WrittenRow
        {
            Guid = Guid.NewGuid(), Score = i, Points = i, Amount = i, Price = i,
        }).ToList();
        var seed = new AsyncSQLiteStore<WrittenRow>();
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
        command.CommandText = "SELECT COUNT(*) FROM T849Rows WHERE " + where;
        return (long)command.ExecuteScalar()!;
    }

    private static readonly List<int> Thirty = Enumerable.Range(0, 30).ToList();
    private static readonly List<long> ThirtyLong = Enumerable.Range(0, 30).Select(i => (long)i).ToList();
    private static readonly List<decimal> ThirtyDecimal = Enumerable.Range(0, 30).Select(i => (decimal)i).ToList();
    private static readonly List<int> FortyFive = Enumerable.Range(0, 45).ToList();
    private static readonly List<decimal> FortyFiveDecimal = Enumerable.Range(0, 45).Select(i => (decimal)i).ToList();

    /// <summary>
    /// Each form increments (or computes) the column its IN list is on. Applied once, rows 0..29 move up by one: no row is
    /// left at 0 and two rows sit at 30. Applied twice to a row, a chunk boundary row (9, 19) moves by two.
    /// </summary>
    public static IEnumerable<object[]> WritesTheFilteredColumn() => new[]
    {
        new object[] { "plain column, Increment" },
        new object[] { "column + 0, Increment" },
        new object[] { "column * 1, Increment" },
        new object[] { "converted column, Increment" },
        new object[] { "CASE over the column, Increment" },
        new object[] { "two columns, Increment of one" },
        new object[] { "CASE over the column, Compute" },
        new object[] { "column + 0, Set then Increment" },
        new object[] { "column + 0, sync Increment" },
        new object[] { "column + 0, connector expression update" },
        new object[] { "column + 0, async connector expression update" },
    };

    private static Func<Task> Run(Fixture f, string form) => form switch
    {
        "plain column, Increment" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Score), Inc(), CancellationToken.None),
        "column + 0, Increment" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Score + 0), Inc(), CancellationToken.None),
        "column * 1, Increment" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Score * 1), Inc(), CancellationToken.None),
        "converted column, Increment" => () => f.Store.UpdateAsync(x => ThirtyLong.Contains((long)x.Score), Inc(), CancellationToken.None),
        "CASE over the column, Increment" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Score > 1000 ? 0 : x.Score), Inc(), CancellationToken.None),
        "two columns, Increment of one" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Points + x.Score - x.Points), Inc(), CancellationToken.None),
        "CASE over the column, Compute" => () => f.Store.UpdateAsync(x => ThirtyDecimal.Contains(x.Amount > 1000m ? 0m : x.Amount), new PropertyUpdate<WrittenRow>().Compute(x => x.Amount, x => x.Price + 1m), CancellationToken.None),
        "column + 0, Set then Increment" => () => f.Store.UpdateAsync(x => Thirty.Contains(x.Score + 0), new PropertyUpdate<WrittenRow>().Set(x => x.Price, 0m).Increment(x => x.Score, 1), CancellationToken.None),
        "column + 0, sync Increment" => () => Task.Run(() => f.SyncStore.Update(x => Thirty.Contains(x.Score + 0), Inc())),
        "column + 0, connector expression update" => () => Task.Run(() => f.Connector.Update<WrittenRow, int>("T849Rows",
            new Dictionary<Expression<Func<WrittenRow, int>>, Expression<Func<WrittenRow, int>>> { { x => x.Score, x => x.Score + 1 } },
            DataBase.ParseConditionExpression((Expression<Func<WrittenRow, bool>>)(x => Thirty.Contains(x.Score + 0))))),
        "column + 0, async connector expression update" => () => f.Connector.UpdateAsync<WrittenRow, int>("T849Rows",
            new Dictionary<Expression<Func<WrittenRow, int>>, Expression<Func<WrittenRow, int>>> { { x => x.Score, x => x.Score + 1 } },
            DataBase.ParseConditionExpression((Expression<Func<WrittenRow, bool>>)(x => Thirty.Contains(x.Score + 0)))),
        _ => throw new ArgumentOutOfRangeException(nameof(form)),
    };

    private static PropertyUpdate<WrittenRow> Inc() => new PropertyUpdate<WrittenRow>().Increment(x => x.Score, 1);

    private static string WrittenColumn(string form)
        => form.Contains("Compute") ? "Amount" : "Score";

    [Theory]
    [MemberData(nameof(WritesTheFilteredColumn))]
    public async Task An_update_that_writes_the_filtered_column_is_one_statement_and_applies_once(string form)
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        await Run(f, form)();

        f.Updates.Should().ContainSingle("a list that the update rewrites is never chunked ({0})", form);
        var column = WrittenColumn(form);
        foreach (var value in new[] { 1, 10, 11, 20, 21, 29 }) RawCount(f, $"{column} = {value}").Should().Be(1, $"{column} {value}");
        RawCount(f, $"{column} = 0").Should().Be(0);
        RawCount(f, $"{column} = 30").Should().Be(2);
    }

    public static IEnumerable<object[]> PastTheLimit() => new[]
    {
        new object[] { "column + 0, Increment" },
        new object[] { "two columns, Increment of one" },
        new object[] { "CASE over the column, Compute" },
        new object[] { "column + 0, sync Increment" },
        new object[] { "column + 0, connector expression update" },
    };

    [Theory]
    [MemberData(nameof(PastTheLimit))]
    public async Task Past_the_limit_an_update_that_writes_the_filtered_column_is_refused(string form)
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        Func<Task> act = form switch
        {
            "column + 0, Increment" => () => f.Store.UpdateAsync(x => FortyFive.Contains(x.Score + 0), Inc(), CancellationToken.None),
            "two columns, Increment of one" => () => f.Store.UpdateAsync(x => FortyFive.Contains(x.Points + x.Score - x.Points), Inc(), CancellationToken.None),
            "CASE over the column, Compute" => () => f.Store.UpdateAsync(x => FortyFiveDecimal.Contains(x.Amount > 1000m ? 0m : x.Amount), new PropertyUpdate<WrittenRow>().Compute(x => x.Amount, x => x.Price + 1m), CancellationToken.None),
            "column + 0, sync Increment" => () => Task.Run(() => f.SyncStore.Update(x => FortyFive.Contains(x.Score + 0), Inc())),
            "column + 0, connector expression update" => () => Task.Run(() => f.Connector.Update<WrittenRow, int>("T849Rows",
                new Dictionary<Expression<Func<WrittenRow, int>>, Expression<Func<WrittenRow, int>>> { { x => x.Score, x => x.Score + 1 } },
                DataBase.ParseConditionExpression((Expression<Func<WrittenRow, bool>>)(x => FortyFive.Contains(x.Score + 0))))),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("writes the filtered column");
        f.Updates.Should().BeEmpty();
        RawCount(f, "Score = 60 OR Pts = 60 OR Amount = 60").Should().Be(0);
    }

    [Fact]
    public async Task An_update_that_writes_a_column_the_filter_expression_does_not_read_still_chunks()
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        await f.Store.UpdateAsync(x => Thirty.Contains(x.Score + 0), new PropertyUpdate<WrittenRow>().Increment(x => x.Amount, 100m), CancellationToken.None);

        RawCount(f, "Amount >= 100").Should().Be(30);
        f.Updates.Should().HaveCount(3);
    }

    [Fact]
    public async Task An_update_that_writes_none_of_the_columns_a_two_column_filter_reads_still_chunks()
    {
        var f = await SeedAsync();
        f.Executed.Clear();

        // Price is written; the filter reads Pts and Score only.
        await f.Store.UpdateAsync(x => Thirty.Contains(x.Points + x.Score - x.Score), new PropertyUpdate<WrittenRow>().Set(x => x.Price, -1m), CancellationToken.None);

        RawCount(f, "Price = -1").Should().Be(30);
        f.Updates.Should().HaveCount(3);
    }
    [Fact]
    public async Task An_expression_update_handed_in_already_rendered_is_never_chunked()
    {
        var f = await SeedAsync();
        var conditions = DataBase.ParseConditionExpression((Expression<Func<WrittenRow, bool>>)(x => Thirty.Contains(x.Score))).ToList();
        // A plain dictionary of rendered clauses names no columns, and is never parsed back to find them.
        var fields = new Dictionary<int, string> { { 0, "Score = Score + 1" } };
        f.Executed.Clear();

        var matched = f.Connector.Update("T849Rows", fields, new Dictionary<string, object>(), conditions, isExpressionValues: true);
        var asyncMatched = await f.Connector.UpdateAsync("T849Rows", fields, new Dictionary<string, object>(), conditions, isExpressionValues: true);

        matched.Should().Be(30);
        asyncMatched.Should().Be(29, "row 29 left the list after the first update");
        f.Updates.Should().HaveCount(2, "one statement each");
        RawCount(f, "Score = 0 OR Score = 1").Should().Be(0);
        RawCount(f, "Score = 30").Should().Be(3, "rows 28 and 29 moved there, row 30 was there");
    }

    [Fact]
    public void SetClauses_name_the_column_each_clause_writes_and_a_clause_added_without_one_makes_them_unknown()
    {
        var clauses = new SetClauses();
        clauses.Add(0, "Score", "Score + @SETScore");
        clauses.Add(1, "Amount", "@SETAmount");
        clauses.Values.Should().Equal("Score = Score + @SETScore", "Amount = @SETAmount");
        clauses.Columns.Should().Equal("Score", "Amount");

        clauses.Remove(0);
        clauses.Columns.Should().Equal("Amount");

        clauses.Add(2, "Price = 1");
        clauses.Columns.Should().BeNull();
        SetClauses.WrittenBy(clauses, isExpressionValues: true).Should().BeNull();
        SetClauses.WrittenBy(new Dictionary<int, string> { { 0, "Score = 1" } }, isExpressionValues: true).Should().BeNull();
        SetClauses.WrittenBy(new Dictionary<int, string> { { 0, "Score" } }, isExpressionValues: false).Should().Equal("Score");
    }

    [Theory]
    [InlineData("Score", new[] { "Score" })]
    [InlineData("T849Rows.Score", new[] { "T849Rows", "Score" })]
    [InlineData("\"T849Rows\".\"Score\"", new[] { "T849Rows", "Score" })]
    [InlineData("[dbo].[T849Rows].[Score]", new[] { "dbo", "T849Rows", "Score" })]
    [InlineData("`T849Rows`.`Score`", new[] { "T849Rows", "Score" })]
    [InlineData("\"Sco\"\"re\"", new[] { "Sco\"re" })]
    [InlineData("(Score + 0)", new[] { "Score" })]
    [InlineData("((Price - Score) + Score)", new[] { "Price", "Score", "Score" })]
    [InlineData("CASE WHEN (Score > 1000) THEN 0 ELSE Score END", new[] { "CASE", "WHEN", "Score", "THEN", "ELSE", "Score", "END" })]
    [InlineData("COALESCE(Score, @p0) + :p1 + $1 + 1.5e3", new[] { "COALESCE", "Score" })]
    [InlineData("LOWER(Name) || 'Score''s'", new[] { "LOWER", "Name" })]
    [InlineData("Score::bigint", new[] { "Score" })]
    public void Identifiers_names_every_column_a_rendered_operand_reads(string sql, string[] expected)
        => InListChunkPlanner.Identifiers(sql).Should().Equal(expected);
}
