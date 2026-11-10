using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-902, live against the Cosmos emulator: a filter or a sort over a <see cref="DateTime"/> member answers what C# answers
/// over the documents as read back (C# compares the ticks, whatever the kind), on a read (async and sync), a count (async and
/// sync), an aggregate's WHERE, the reads behind <c>Update(filter, Action)</c> and <c>Delete(filter)</c>, and a sorted read
/// with and without paging.
/// </summary>
/// <remarks>
/// <para>The documents are written as Birko's serializer writes them (System.Text.Json defaults): ISO 8601 with as many
/// fraction digits as the value needs (none for a whole second), then <c>Z</c> for a UTC value, the writer's offset for a
/// local one, nothing for an unspecified one. Cosmos DB compared and sorted that text, so <c>"…00.5Z"</c> sorted before
/// <c>"…00Z"</c> and <c>"…00"</c> before <c>"…00+01:00"</c>.</para>
/// <para>Local values are written and read on this machine, so a stored offset is the reader's own: the case the wall-clock
/// key is exact for (TASK-897).</para>
/// <para>Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c>); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.</para>
/// </remarks>
public class Task902DateTimeLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task902DateTimeLiveTests(ITestOutputHelper output) => _output = output;

    public class Row : AbstractModel
    {
        public string? Code { get; set; }
        public DateTime T { get; set; }
        public DateTime? TN { get; set; }
        public DateTime T2 { get; set; }
        public DateTimeOffset O { get; set; }
        public string? Mark { get; set; }
    }

    private static DateTime U(int h, int m, int s, long ticks = 0) => new DateTime(2026, 1, 1, h, m, s, DateTimeKind.Utc).AddTicks(ticks);
    private static DateTime L(int h, int m, int s, long ticks = 0) => new DateTime(2026, 1, 1, h, m, s, DateTimeKind.Local).AddTicks(ticks);
    private static DateTime N(int h, int m, int s, long ticks = 0) => new DateTime(2026, 1, 1, h, m, s, DateTimeKind.Unspecified).AddTicks(ticks);
    private static DateTimeOffset Dto(int h, int m, int s, int offsetHours, long ticks = 0)
        => new DateTimeOffset(2026, 1, 1, h, m, s, TimeSpan.FromHours(offsetHours)).AddTicks(ticks);

    /// <summary>The documents, and the members each omits (written before the member existed).</summary>
    internal static List<(Row Row, string[] Missing)> Seed() => new()
    {
        (new() { Code = "c01", T = U(10, 0, 0), TN = null, T2 = L(10, 0, 0, 5_000_000), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
        (new() { Code = "c02", T = U(10, 0, 0, 5_000_000), TN = L(10, 0, 0), T2 = U(10, 0, 0), O = Dto(11, 0, 0, 1) }, Array.Empty<string>()),
        (new() { Code = "c03", T = L(10, 0, 0), TN = N(10, 0, 0, 5_000_000), T2 = N(10, 0, 0), O = Dto(10, 0, 0, 0, 5_000_000) }, Array.Empty<string>()),
        (new() { Code = "c04", T = N(10, 0, 0), T2 = U(9, 59, 59, 9_999_999), O = Dto(10, 30, 0, 1) }, new[] { "TN" }),
        (new() { Code = "c05", T = N(10, 0, 0, 1_234_567), TN = U(10, 0, 0, 1), T2 = L(10, 0, 0, 1_234_567), O = Dto(9, 0, 0, -1) }, Array.Empty<string>()),
        (new() { Code = "c06", T = U(10, 0, 0, 1), TN = U(10, 0, 0), T2 = U(10, 0, 0, 1), O = Dto(10, 0, 0, 0, 1) }, Array.Empty<string>()),
        (new() { Code = "c07", T = L(9, 59, 59, 9_999_999), TN = L(9, 59, 59, 9_999_999), T2 = U(10, 0, 1), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
        (new() { Code = "c08", T = U(10, 0, 1), TN = new DateTime(2026, 1, 2), T2 = N(10, 0, 0, 5_000_000), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
        (new() { Code = "c09", T = new DateTime(2026, 7, 1, 10, 0, 0, 250, DateTimeKind.Local), TN = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Local),
                 T2 = new DateTime(2026, 7, 1, 10, 0, 0, 250, DateTimeKind.Local), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
        (new() { Code = "c10", T = new DateTime(2026, 1, 2), TN = N(23, 59, 59, 9_999_999), T2 = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
        (new() { Code = "c11", TN = U(10, 0, 0, 5_000_000), O = Dto(10, 0, 0, 0) }, new[] { "T", "T2" }),
        (new() { Code = "c12", T = DateTime.MinValue, TN = null, T2 = U(10, 0, 0), O = Dto(10, 0, 0, 0) }, Array.Empty<string>()),
    };

    private static readonly (string Name, ExpressionType Op)[] Ops =
    {
        ("<", ExpressionType.LessThan), ("<=", ExpressionType.LessThanOrEqual), (">", ExpressionType.GreaterThan),
        (">=", ExpressionType.GreaterThanOrEqual), ("==", ExpressionType.Equal), ("!=", ExpressionType.NotEqual),
    };

    private static Expression<Func<Row, bool>> Compare(string member, ExpressionType op, Expression value)
    {
        var x = Expression.Parameter(typeof(Row), "x");
        Expression left = Expression.Property(x, member);
        if (left.Type != value.Type)
        {
            if (Nullable.GetUnderlyingType(left.Type) == null) left = Expression.Convert(left, value.Type);
            else value = Expression.Convert(value, left.Type);
        }
        return Expression.Lambda<Func<Row, bool>>(Expression.MakeBinary(op, left, value), x);
    }

    private sealed class Box<TValue>
    {
        public TValue Value = default!;
    }

    /// <summary>A captured variable, as a caller's closure holds it.</summary>
    private static Expression Captured<TValue>(TValue value)
        => Expression.Field(Expression.Constant(new Box<TValue> { Value = value }), nameof(Box<TValue>.Value));

    /// <summary>The filters that must answer as C#.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] Shapes()
    {
        var u = U(10, 0, 0);
        var uh = U(10, 0, 0, 5_000_000);
        var l = L(10, 0, 0);
        var n = N(10, 0, 0);
        var n7 = N(10, 0, 0, 1_234_567);
        var t1 = U(10, 0, 0, 1);
        var day1 = new DateTime(2026, 1, 1);
        var lj = new DateTime(2026, 7, 1, 10, 0, 0, 250, DateTimeKind.Local);
        var min = DateTime.MinValue;
        var shapes = new List<(string, Expression<Func<Row, bool>>)>();

        foreach (var (name, op) in Ops)
        {
            shapes.Add(($"T {name} u (constant)", Compare("T", op, Expression.Constant(u))));
            shapes.Add(($"T {name} uh", Compare("T", op, Captured(uh))));
            shapes.Add(($"T {name} l", Compare("T", op, Captured(l))));
            shapes.Add(($"T {name} n", Compare("T", op, Captured(n))));
            shapes.Add(($"T {name} n7", Compare("T", op, Captured(n7))));
            shapes.Add(($"T {name} t1", Compare("T", op, Captured(t1))));
            shapes.Add(($"TN {name} u", Compare("TN", op, Captured(u))));
            shapes.Add(($"TN {name} uh", Compare("TN", op, Captured(uh))));
            var x = Expression.Parameter(typeof(Row), "x");
            shapes.Add(($"T {name} T2", Expression.Lambda<Func<Row, bool>>(
                Expression.MakeBinary(op, Expression.Property(x, "T"), Expression.Property(x, "T2")), x)));
            shapes.Add(($"T {name} TN", Expression.Lambda<Func<Row, bool>>(
                Expression.MakeBinary(op, Expression.Convert(Expression.Property(x, "T"), typeof(DateTime?)), Expression.Property(x, "TN")), x)));
        }

        var list = new[] { u, uh };
        var localList = new List<DateTime> { l };
        var nullable = new DateTime?[] { null, uh };
        shapes.AddRange(new (string, Expression<Func<Row, bool>>)[]
        {
            ("uh > T (value on the left)", x => uh > x.T),
            ("!(T > u)", x => !(x.T > u)),
            ("!(T >= uh)", x => !(x.T >= uh)),
            ("!(TN < u)", x => !(x.TN < u)),
            ("!(T == l)", x => !(x.T == l)),
            ("T > u && T < uh", x => x.T > u && x.T < uh),
            ("T == u || TN == u", x => x.T == u || x.TN == u),
            ("T == min", x => x.T == min),
            ("T < lj", x => x.T < lj),
            ("T.Date == day1", x => x.T.Date == day1),
            ("T.Date != day1", x => x.T.Date != day1),
            ("T.Date > day1", x => x.T.Date > day1),
            ("TN.Value.Date <= day1", x => x.TN.HasValue && x.TN.Value.Date <= day1),
            ("T.Date == T2.Date", x => x.T.Date == x.T2.Date),
            ("T.Date < T2.Date", x => x.T.Date < x.T2.Date),
            ("[u, uh].Contains(T)", x => list.Contains(x.T)),
            ("[l].Contains(T)", x => localList.Contains(x.T)),
            ("![u, uh].Contains(T)", x => !list.Contains(x.T)),
            ("[null, uh].Contains(TN)", x => nullable.Contains(x.TN)),
            ("!([null, uh].Contains(TN))", x => !nullable.Contains(x.TN)),
            ("(TN ?? n) > u", x => (x.TN ?? n) > u),
            ("TN.GetValueOrDefault() >= l", x => x.TN.GetValueOrDefault() >= l),
            ("TN.HasValue && TN.Value < uh", x => x.TN.HasValue && x.TN.Value < uh),
            ("TN == null || TN > u", x => x.TN == null || x.TN > u),
            ("T.Equals(u)", x => x.T.Equals(u)),
            ("DateTime.Equals(T, T2)", x => DateTime.Equals(x.T, x.T2)),
            ("DateTime.Compare(T, u) > 0", x => DateTime.Compare(x.T, u) > 0),
            ("0 >= T.CompareTo(T2)", x => 0 >= x.T.CompareTo(x.T2)),
        });
        return shapes.ToArray();
    }

    /// <summary>Shapes no Cosmos DB rendering answers as C# does: refused by name, before any request.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] RefusedShapes()
    {
        var u = U(10, 0, 0);
        var o = Dto(10, 0, 0, 0);
        return new (string, Expression<Func<Row, bool>>)[]
        {
            ("O == o (DateTimeOffset)", x => x.O == o),
            ("O > o (DateTimeOffset)", x => x.O > o),
            ("O < o (DateTimeOffset)", x => x.O < o),
            ("T.Year == 2026", x => x.T.Year == 2026),
            ("T.Hour == 10", x => x.T.Hour == 10),
            ("T.Ticks > 0", x => x.T.Ticks > 0),
            ("T.TimeOfDay > 0", x => x.T.TimeOfDay > TimeSpan.Zero),
            ("DateTime.Compare(T, u) > 1", x => DateTime.Compare(x.T, u) > 1),
            ("T.AddDays(1) > u", x => x.T.AddDays(1) > u),
        };
    }

    private string? ResolveConnection()
    {
        var conn = Environment.GetEnvironmentVariable(ConnEnv);
        if (!string.IsNullOrWhiteSpace(conn)) return conn;
        const string message = "SKIPPED: no live Cosmos. Set " + ConnEnv + " (and " + ModeEnv
                             + "=Gateway for the emulator); set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return null;
    }

    private static Settings LiveSettings()
    {
        var settings = new Settings();
        if (string.Equals(Environment.GetEnvironmentVariable(ModeEnv), "Gateway", StringComparison.OrdinalIgnoreCase))
        {
            settings.ConnectionMode = ConnectionMode.Gateway;
        }
        return settings;
    }

    /// <summary>Writes the seed as Birko's serializer writes it, each document without the members it omits.</summary>
    private static async Task<Dictionary<string, Guid>> SeedAsync(Container container, Dictionary<string, Guid>? ids = null)
    {
        ids ??= new Dictionary<string, Guid>();
        var writes = new List<Task>();
        foreach (var (row, missing) in Seed())
        {
            if (!ids.TryGetValue(row.Code!, out var guid))
            {
                guid = Guid.NewGuid();
                ids[row.Code!] = guid;
            }
            row.Guid = guid;
            var node = (JsonObject)JsonSerializer.SerializeToNode(row)!;
            foreach (var name in missing) node.Remove(name);
            node["id"] = guid.ToString();
            writes.Add(container.UpsertItemAsync(node, new PartitionKey(guid.ToString())));
        }
        await Task.WhenAll(writes);
        return ids;
    }

    private static string Codes(IEnumerable<Row> rows)
        => string.Join(",", rows.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal));

    private async Task<(AsyncCosmosDBStore<Row> Store, string Db)?> OpenAsync()
    {
        var conn = ResolveConnection();
        if (conn == null) return null;
        var dbName = "birko_task902_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        await store.InitAsync();
        return (store, dbName);
    }

    private static async Task DropAsync(AsyncCosmosDBStore<Row> store, string dbName)
    {
        try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
    }

    /// <param name="refused">Each shape must be refused by name (TASK-902) on every path, and a refused write changes no row.</param>
    private async Task<List<string>> RunMatrixAsync((string Label, Expression<Func<Row, bool>> Filter)[] shapes, bool writes, bool refused)
    {
        var failures = new List<string>();
        var opened = await OpenAsync();
        if (opened == null) return failures;
        var (store, dbName) = opened.Value;
        try
        {
            var container = store.Container!;
            var syncStore = new CosmosDBStore<Row>(container);
            var ids = await SeedAsync(container);
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(Seed().Count);

            foreach (var (label, filter) in shapes)
            {
                var want = Codes(all.Where(filter.Compile()));
                var wantCount = all.Count(filter.Compile());

                async Task Check(string path, Func<Task<string>> run)
                {
                    try
                    {
                        var got = await run();
                        if (refused) failures.Add($"{label} [{path}]: not refused (C# {{{want}}}, got {{{got}}}{(got == want ? ", the same" : "")})");
                        else if (got != want) failures.Add($"{label} [{path}]: C# {{{want}}} got {{{got}}}");
                    }
                    catch (NotSupportedException e) when (refused && e.Message.Contains("TASK-902"))
                    {
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{label} [{path}]: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                    }
                }

                if (!writes)
                {
                    await Check("read", async () => Codes(await store.ReadAsync(filter)));
                    await Check("sync read", () => Task.FromResult(Codes(syncStore.Read(filter))));
                    await Check("count", async () => { var c = await store.CountAsync(filter); return c == wantCount ? want : "count " + c; });
                    await Check("sync count", () => { var c = syncStore.Count(filter); return Task.FromResult(c == wantCount ? want : "count " + c); });
                    await Check("aggregate", async () =>
                    {
                        var rows = await store.AggregateAsync(new AggregateQuery<Row>
                        {
                            Filter = filter,
                            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Code", "cnt") },
                        });
                        var c = rows.Count == 0 ? 0 : Convert.ToInt64(rows[0].Values["cnt"]);
                        return c == wantCount ? want : "count " + c;
                    });
                    continue;
                }

                await Check("update", async () =>
                {
                    await SeedAsync(container, ids);
                    try { await store.UpdateAsync(filter, r => r.Mark = "hit"); }
                    finally
                    {
                        if (refused && (await store.ReadAsync(x => true)).Any(r => r.Mark == "hit")) failures.Add($"{label} [update]: a refused write changed rows");
                    }
                    return Codes((await store.ReadAsync(x => true)).Where(r => r.Mark == "hit"));
                });
                await Check("delete", async () =>
                {
                    await SeedAsync(container, ids);
                    try { await store.DeleteAsync(filter); }
                    finally
                    {
                        if (refused && (await store.ReadAsync(x => true)).Count() != all.Count) failures.Add($"{label} [delete]: a refused write changed rows");
                    }
                    var left = (await store.ReadAsync(x => true)).Select(r => r.Code).ToHashSet();
                    return Codes(all.Where(r => !left.Contains(r.Code)));
                });
            }
        }
        finally
        {
            await DropAsync(store, dbName);
        }

        foreach (var f in failures) _output.WriteLine(f);
        _output.WriteLine($"{failures.Count} failures over {shapes.Length} shapes");
        return failures;
    }

    [Fact]
    public async Task A_DateTime_filter_answers_as_CSharp()
    {
        var failures = await RunMatrixAsync(Shapes(), writes: false, refused: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_DateTime_filter_answers_as_CSharp_in_writes()
    {
        var failures = await RunMatrixAsync(Shapes(), writes: true, refused: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_shape_with_no_exact_rendering_is_refused_by_name_on_every_path()
    {
        var failures = await RunMatrixAsync(RefusedShapes(), writes: false, refused: true);
        failures.AddRange(await RunMatrixAsync(RefusedShapes(), writes: true, refused: true));
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    /// <summary>The sorts: each member, both directions, with and without paging, filtered or not.</summary>
    internal static IEnumerable<(string Label, OrderBy<Row> Order, Func<IEnumerable<Row>, IOrderedEnumerable<Row>> Oracle,
        Func<Row, object?> Key)> Sorts()
    {
        yield return ("T asc", OrderBy<Row>.By(x => x.T), r => r.OrderBy(x => x.T), r => r.T);
        yield return ("T desc", OrderBy<Row>.ByDescending(x => x.T), r => r.OrderByDescending(x => x.T), r => r.T);
        yield return ("TN asc", OrderBy<Row>.By(x => x.TN), r => r.OrderBy(x => x.TN), r => r.TN);
        yield return ("TN desc", OrderBy<Row>.ByDescending(x => x.TN), r => r.OrderByDescending(x => x.TN), r => r.TN);
        yield return ("T2 asc", OrderBy<Row>.By(x => x.T2), r => r.OrderBy(x => x.T2), r => r.T2);
    }

    internal static readonly (int? Limit, int? Offset)[] Pages =
    {
        (null, null), (3, null), (3, 0), (3, 1), (2, 2), (4, 3), (1, 4), (5, 7), (20, 0), (2, 11), (3, 12), (null, 2),
    };

    private static string Ticks(IEnumerable<Row> rows, Func<Row, object?> key)
        => string.Join(",", rows.Select(r => key(r) is DateTime d ? d.Ticks.ToString() : "null"));

    [Fact]
    public async Task A_sort_by_a_DateTime_member_orders_as_CSharp_with_and_without_paging()
    {
        var failures = new List<string>();
        var opened = await OpenAsync();
        if (opened == null) return;
        var (store, dbName) = opened.Value;
        try
        {
            var container = store.Container!;
            var syncStore = new CosmosDBStore<Row>(container);
            await SeedAsync(container);
            var all = (await store.ReadAsync(x => true)).ToList();
            var filters = new (string, Expression<Func<Row, bool>>?)[] { ("all", null), ("Code != c05", x => x.Code != "c05") };
            foreach (var (label, order, oracle, key) in Sorts())
            foreach (var (filterLabel, filter) in filters)
            foreach (var (limit, offset) in Pages)
            {
                IEnumerable<Row> expected = oracle(filter == null ? all : all.Where(filter.Compile()));
                if (offset.HasValue) expected = expected.Skip(offset.Value);
                if (limit.HasValue) expected = expected.Take(limit.Value);
                var want = Ticks(expected, key);
                var name = $"{label} {filterLabel} limit {limit?.ToString() ?? "-"} offset {offset?.ToString() ?? "-"}";
                try
                {
                    var got = Ticks(await store.ReadAsync(filter, order, limit, offset), key);
                    if (got != want) failures.Add($"{name} [read]: C# {want} got {got}");
                    var gotSync = Ticks(syncStore.Read(filter, order, limit, offset), key);
                    if (gotSync != want) failures.Add($"{name} [sync read]: C# {want} got {gotSync}");
                }
                catch (Exception e)
                {
                    failures.Add($"{name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                }
            }
        }
        finally
        {
            await DropAsync(store, dbName);
        }

        foreach (var f in failures) _output.WriteLine(f);
        _output.WriteLine($"{failures.Count} failures");
        failures.Should().BeEmpty(string.Join("\n", failures));
    }
}

/// <summary>TASK-902, offline: what the store's LINQ provider is handed for a DateTime comparison, and how it is planned.</summary>
public class Task902DateTimeRenderingTests
{
    private static readonly CosmosClient Client = new("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");

    private static IQueryable<Task902DateTimeLiveTests.Row> Queryable() => Client.GetContainer("db", "c").GetItemLinqQueryable<Task902DateTimeLiveTests.Row>();

    private static string Where(Expression<Func<Task902DateTimeLiveTests.Row, bool>> filter)
    {
        var text = Queryable().Where(CosmosFilterRewrite.Prepare(filter)!).ToQueryDefinition().QueryText;
        return text.Substring(text.IndexOf(" WHERE ", StringComparison.Ordinal) + 7);
    }

    private const string Key = "CONCAT(SUBSTRING(root[\"T\"], 0, 19), SUBSTRING(CONCAT(SUBSTRING(CONCAT(REPLACE(REPLACE(REPLACE("
        + "SUBSTRING(root[\"T\"], 19, 20), \"+\", \"Z\"), \"-\", \"Z\"), \".\", \"\"), \"Z\"), 0, INDEX_OF(CONCAT(REPLACE(REPLACE("
        + "REPLACE(SUBSTRING(root[\"T\"], 19, 20), \"+\", \"Z\"), \"-\", \"Z\"), \".\", \"\"), \"Z\"), \"Z\")), \"0000000\"), 0, 7))";

    [Fact]
    public void A_comparison_with_a_value_is_a_text_range_on_the_member_and_the_wall_clock_key()
    {
        var u = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(5_000_000);
        Where(x => x.T > u).Should().Be($"((root[\"T\"] >= \"2026-01-01T10:00:00\") AND ({Key} > \"2026-01-01T10:00:005000000\"))");
        // A document without T reads default(DateTime), which is below u (TASK-899).
        Where(x => x.T < u).Should().Be(
            $"((NOT IS_DEFINED(root[\"T\"])) OR ((root[\"T\"] < \"2026-01-01T10:00:01\") AND ({Key} < \"2026-01-01T10:00:005000000\")))");
        Where(x => x.T != u).Should().Be($"((NOT IS_DEFINED(root[\"T\"])) OR ({Key} != \"2026-01-01T10:00:005000000\"))", "!= has no range");
    }

    [Fact]
    public void The_kind_of_a_value_is_not_part_of_its_key()
    {
        var local = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Local);
        var utc = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        Where(x => x.T == local).Should().Be(Where(x => x.T == utc));
    }

    [Fact]
    public void Preparing_twice_changes_nothing()
    {
        var u = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var list = new DateTime?[] { null, u };
        Expression<Func<Task902DateTimeLiveTests.Row, bool>> filter = x => !(x.T > u) && x.T.Date == x.T2.Date && list.Contains(x.TN) && (x.TN ?? u) > x.T;
        var once = CosmosFilterRewrite.Prepare(filter)!;
        var twice = CosmosFilterRewrite.Prepare(once)!;
        Queryable().Where(twice).ToQueryDefinition().QueryText.Should().Be(Queryable().Where(once).ToQueryDefinition().QueryText);
    }

    [Fact]
    public void A_long_DateTime_list_holding_the_default_splits_with_the_missing_leg_in_one_chunk()
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var list = Enumerable.Range(1, 35).Select(i => day.AddSeconds(i)).Append(default).ToList();
        var prepared = CosmosFilterRewrite.Prepare<Task902DateTimeLiveTests.Row>(x => list.Contains(x.T))!;
        var chunks = CosmosInListChunking.Plan(prepared, "read", 10, 40_000, false, f => Queryable().Where(f).ToQueryDefinition().QueryText.Length)!;
        chunks.Should().HaveCount(4);
        chunks.Count(c => c.ToString().Contains("IsDefined")).Should().Be(1);
        foreach (var chunk in chunks)
        {
            var again = CosmosFilterRewrite.Prepare(chunk)!;
            again.ToString().Contains("IsDefined").Should().Be(chunk.ToString().Contains("IsDefined"), "re-preparing a chunk adds no leg elsewhere");
        }
    }

    [Theory]
    [InlineData("member")]
    [InlineData("offset")]
    [InlineData("computed")]
    public void A_shape_with_no_exact_rendering_is_refused_before_any_request(string shape)
    {
        var o = DateTimeOffset.UnixEpoch;
        var u = DateTime.UnixEpoch;
        Expression<Func<Task902DateTimeLiveTests.Row, bool>> filter = shape switch
        {
            "member" => x => x.T.Month == 1,
            "offset" => x => x.O >= o,
            _ => x => x.T.AddHours(1) > u,
        };
        var act = () => CosmosFilterRewrite.Prepare(filter);
        act.Should().Throw<NotSupportedException>().WithMessage("*(TASK-902)");
    }

    [Fact]
    public void A_sort_by_a_DateTimeOffset_is_refused_by_name()
    {
        var act = () => CosmosDateTimeOrder.Plan(OrderBy<Task902DateTimeLiveTests.Row>.By(x => x.O), "Row");
        act.Should().Throw<NotSupportedException>().WithMessage("*DateTimeOffset*(TASK-902)");
        CosmosDateTimeOrder.Plan(OrderBy<Task902DateTimeLiveTests.Row>.By(x => x.Code!), "Row").Should().BeNull("a sort with no DateTime is the server's");
    }
}
