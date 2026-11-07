using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-859 — the SQLite-only half of the nullable-filter fix: the SQL each shape now renders (a non-nullable column keeps
/// the SQL it always had), a comparison against a null closure value, and TASK-833's chunking of a list that holds
/// <c>null</c>. The per-provider answers are <c>NullFilterContract</c>.
/// </summary>
public sealed class Task859NullComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t859-{Guid.NewGuid():N}");
    private readonly List<SqLiteSettings> _settings = new();

    public Task859NullComparisonTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var settings in _settings) SqlitePool.ClearFor(settings);
        TestTempDirectory.Delete(_root);
    }

    [Table("T859Rows")]
    public class Row : AbstractModel
    {
        public int? Score { get; set; }
        public int Amount { get; set; }
        public int Hits { get; set; }
    }

    private sealed class LowLimitStore : AsyncSQLiteStore<Row>
    {
        private readonly SqLiteConnector _connector;
        public LowLimitStore(SqLiteConnector connector) => _connector = connector;

        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = _connector;
        }
    }

    /// <summary>60 rows: Score 0..49 on the first 50, NULL on the last 10; Amount = index.</summary>
    private async Task<(LowLimitStore Store, List<Row> Rows, List<string> Executed)> SeedAsync()
    {
        var settings = new SqLiteSettings(_root, $"t859-{Guid.NewGuid():N}.db");
        _settings.Add(settings);
        var rows = Enumerable.Range(0, 60).Select(i => new Row { Guid = Guid.NewGuid(), Score = i < 50 ? i : null, Amount = i }).ToList();

        var seeder = new AsyncSQLiteStore<Row>();
        seeder.SetSettings(settings);
        await seeder.CreateAsync(rows, ct: CancellationToken.None);

        // SQLite with a limit of 40 parameters and chunks of 10 (TASK-833's test connector).
        var connector = new Task833InListChunkingTests.LowLimitSqLiteConnector(settings);
        var store = new LowLimitStore(connector);
        store.SetSettings(settings);
        await store.CountAsync(x => x.Amount < 0, CancellationToken.None);
        var executed = new List<string>();
        connector.OnExecute += text => executed.Add(text);
        return (store, rows, executed);
    }

    // ── the rendered SQL ─────────────────────────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> RenderedShapes()
    {
        var cases = new (Expression<Func<Row, bool>> Filter, string Where)[]
        {
            // A column that cannot be NULL keeps its SQL: no leg, no noise.
            (x => x.Amount != 2, "WHERE T859Rows.Amount <> 2"),
            (x => !(x.Amount == 2), "WHERE NOT (T859Rows.Amount = 2)"),
            // A nullable column gains a leg only where C# and SQL would answer differently.
            (x => x.Score == 5, "WHERE T859Rows.Score = 5"),
            (x => x.Score != 5, "WHERE (T859Rows.Score <> 5 OR T859Rows.Score IS NULL)"),
            (x => !(x.Score == 5), "WHERE NOT ((T859Rows.Score = 5 AND T859Rows.Score IS NOT NULL))"),
        };
        foreach (var (filter, where) in cases) yield return new object[] { filter, where };
    }

    [Theory]
    [MemberData(nameof(RenderedShapes))]
    public async Task Each_shape_renders_its_leg_only_where_needed(Expression<Func<Row, bool>> filter, string where)
    {
        var (store, _, executed) = await SeedAsync();
        executed.Clear();
        await store.CountAsync(filter, CancellationToken.None);
        executed.Should().ContainSingle().Which.Should().Contain(where);
    }

    [Fact]
    public async Task A_nullable_not_equal_renders_its_IS_NULL_leg()
    {
        var (store, _, executed) = await SeedAsync();
        executed.Clear();
        (await store.CountAsync(x => x.Score != 5, CancellationToken.None)).Should().Be(59, "49 non-NULL scores other than 5, and 10 NULLs");
        executed.Single().Should().MatchRegex(@"\(T859Rows\.Score <> 5 OR T859Rows\.Score IS NULL\)");
    }

    [Fact]
    public async Task A_negated_equality_renders_IS_NOT_NULL_inside_the_negation()
    {
        var (store, _, executed) = await SeedAsync();
        executed.Clear();
        (await store.CountAsync(x => !(x.Score == 5), CancellationToken.None)).Should().Be(59);
        executed.Single().Should().MatchRegex(@"NOT \(\(T859Rows\.Score = 5 AND T859Rows\.Score IS NOT NULL\)\)");
    }

    // ── an ordering against a null value ─────────────────────────────────────────────────────────────────

    private static readonly int? NoScore = null;

    [Fact]
    public async Task An_ordering_against_a_null_value_matches_no_row_as_in_CSharp()
    {
        // Before: the closure's null made the comparison `Score IS NULL`, 10 rows where C# matches none.
        var (store, rows, _) = await SeedAsync();
        rows.Count(x => x.Score > NoScore).Should().Be(0);
        (await store.CountAsync(x => x.Score > NoScore, CancellationToken.None)).Should().Be(0);
        (await store.CountAsync(x => x.Score <= NoScore, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Its_negation_matches_every_row_and_a_delete_with_it_is_refused_as_whole_table()
    {
        var (store, rows, _) = await SeedAsync();
        (await store.CountAsync(x => !(x.Score > NoScore), CancellationToken.None)).Should().Be(rows.Count);

        var delete = () => store.DeleteAsync(x => !(x.Score > NoScore), CancellationToken.None);
        await delete.Should().ThrowAsync<WholeTableWriteException>("a filter that constrains nothing never deletes every row");
        (await store.CountAsync(x => x.Amount >= 0, CancellationToken.None)).Should().Be(rows.Count);
    }

    // ── TASK-833 chunking with a null in the list ────────────────────────────────────────────────────────

    private static List<int?> ThirtyAndNull() => Enumerable.Range(0, 30).Select(i => (int?)i).Append(null).ToList();

    [Fact]
    public async Task A_list_holding_null_past_the_chunk_size_reads_each_NULL_row_once()
    {
        var (store, rows, executed) = await SeedAsync();
        var list = ThirtyAndNull();
        var expected = rows.Where(x => list.Contains(x.Score)).Select(x => x.Guid).ToList();
        expected.Should().HaveCount(40, "scores 0..29 and the 10 NULLs");

        executed.Clear();
        var read = (await store.ReadAsync(x => list.Contains(x.Score), ct: CancellationToken.None)).Select(x => x.Guid).ToList();
        read.Should().BeEquivalentTo(expected).And.OnlyHaveUniqueItems();
        executed.Should().HaveCount(3, "30 values in chunks of 10");
        executed.Count(sql => sql.Contains("IS NULL")).Should().Be(1, "the NULL leg goes with the first chunk only");

        (await store.CountAsync(x => list.Contains(x.Score), CancellationToken.None)).Should().Be(40);
    }

    [Fact]
    public async Task A_chunked_increment_over_a_list_holding_null_applies_once_to_each_row()
    {
        var (store, rows, _) = await SeedAsync();
        var list = ThirtyAndNull();

        await store.UpdateAsync(x => list.Contains(x.Score), new PropertyUpdate<Row>().Increment(x => x.Hits, 1), CancellationToken.None);

        var after = (await store.ReadAsync(x => x.Amount >= 0, ct: CancellationToken.None)).ToList();
        after.Where(x => list.Contains(x.Score)).Should().HaveCount(40).And.OnlyContain(x => x.Hits == 1);
        after.Where(x => !list.Contains(x.Score)).Should().OnlyContain(x => x.Hits == 0);
    }

    [Fact]
    public async Task A_chunked_delete_over_a_list_holding_null_removes_exactly_the_matching_rows()
    {
        var (store, _, _) = await SeedAsync();
        var list = ThirtyAndNull();

        await store.DeleteAsync(x => list.Contains(x.Score), CancellationToken.None);

        var left = (await store.ReadAsync(x => x.Amount >= 0, ct: CancellationToken.None)).ToList();
        left.Should().HaveCount(20).And.OnlyContain(x => x.Score >= 30);
    }

    [Fact]
    public async Task A_not_in_holding_null_past_the_limit_is_still_refused()
    {
        var (store, _, _) = await SeedAsync();
        var list = Enumerable.Range(0, 45).Select(i => (int?)i).Append(null).ToList();

        var delete = () => store.DeleteAsync(x => !list.Contains(x.Score), CancellationToken.None);
        (await delete.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("NOT IN");
    }
}
