using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Aggregation;
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
/// TASK-905, live against the Cosmos emulator: a filter over a <see cref="TimeSpan"/> member answers what C# answers over the
/// documents as read back, on a read (async and sync), a count (async and sync), an aggregate's WHERE and the reads behind
/// <c>Update(filter, Action)</c> and <c>Delete(filter)</c>; a sort by one, an aggregate of one and a shape no rendering answers
/// as C# are refused by name, before any request.
/// </summary>
/// <remarks>
/// <para>Birko's serializer (System.Text.Json defaults) writes a TimeSpan in the constant format, <c>[-][d.]hh:mm:ss[.fffffff]</c>:
/// the day count only when there is one, without padding, and seven fraction digits only when there is a fraction. Cosmos DB
/// compares that text, so <c>"1.02:03:04"</c> sorts before <c>"23:00:00"</c>, <c>"10.00:00:00"</c> before <c>"2.00:00:00"</c>,
/// and <c>"-00:00:01"</c> before <c>"-00:00:02"</c>.</para>
/// <para>Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c>); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.</para>
/// </remarks>
public class Task905TimeSpanLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task905TimeSpanLiveTests(ITestOutputHelper output) => _output = output;

    public class Row : AbstractModel
    {
        public string? Code { get; set; }
        public TimeSpan D { get; set; }
        public TimeSpan? DN { get; set; }
        public TimeSpan D2 { get; set; }
        public string? Mark { get; set; }
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    private static readonly TimeSpan Day1Hms = new(1, 2, 3, 4);

    /// <summary>The documents, and the members each omits (written before the member existed).</summary>
    internal static List<(Row Row, string[] Missing)> Seed() => new()
    {
        (new() { Code = "c01", D = TimeSpan.Zero, DN = null, D2 = S(1) }, Array.Empty<string>()),
        (new() { Code = "c02", D = S(1), DN = S(1.5), D2 = S(1) }, Array.Empty<string>()),
        (new() { Code = "c03", D = S(1.5), DN = TimeSpan.FromHours(23), D2 = S(1.25) }, Array.Empty<string>()),
        (new() { Code = "c04", D = S(1.25), D2 = S(1.5) }, new[] { "DN" }),
        (new() { Code = "c05", D = TimeSpan.FromHours(23), DN = Day1Hms, D2 = Day1Hms }, Array.Empty<string>()),
        (new() { Code = "c06", D = Day1Hms, DN = S(-1), D2 = TimeSpan.FromHours(23) }, Array.Empty<string>()),
        (new() { Code = "c07", D = TimeSpan.FromDays(10), DN = TimeSpan.FromDays(2), D2 = TimeSpan.FromDays(2) }, Array.Empty<string>()),
        (new() { Code = "c08", D = TimeSpan.FromDays(2), DN = TimeSpan.FromDays(10), D2 = TimeSpan.FromDays(10) }, Array.Empty<string>()),
        (new() { Code = "c09", D = S(-1), DN = S(-2), D2 = S(-2) }, Array.Empty<string>()),
        (new() { Code = "c10", D = S(-2), DN = S(-1), D2 = S(-1) }, Array.Empty<string>()),
        (new() { Code = "c11", D = TimeSpan.FromDays(-1), DN = TimeSpan.Zero, D2 = S(-0.5) }, Array.Empty<string>()),
        (new() { Code = "c12", D = S(-0.5), DN = TimeSpan.FromDays(-1), D2 = TimeSpan.FromDays(-1) }, Array.Empty<string>()),
        (new() { Code = "c13", D = TimeSpan.MaxValue, DN = TimeSpan.MinValue, D2 = TimeSpan.Zero }, Array.Empty<string>()),
        (new() { Code = "c14", D = TimeSpan.MinValue, DN = TimeSpan.MaxValue, D2 = TimeSpan.MinValue }, Array.Empty<string>()),
        (new() { Code = "c15", D = TimeSpan.FromTicks(1), DN = TimeSpan.FromTicks(-1), D2 = TimeSpan.Zero }, Array.Empty<string>()),
        (new() { Code = "c16", DN = S(1.5) }, new[] { "D", "D2" }),
        (new() { Code = "c17", D = new TimeSpan(-1, -2, -3, -4), DN = null, D2 = TimeSpan.FromHours(-23) }, Array.Empty<string>()),
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

    /// <summary>The values compared against: each side of every text-order trap.</summary>
    private static readonly (string Label, TimeSpan Value)[] Values =
    {
        ("0", TimeSpan.Zero), ("1s", S(1)), ("1.25s", S(1.25)), ("23h", TimeSpan.FromHours(23)), ("1d2h3m4s", Day1Hms),
        ("2d", TimeSpan.FromDays(2)), ("-1s", S(-1)), ("-1.5s", S(-1.5)), ("-1d", TimeSpan.FromDays(-1)), ("max", TimeSpan.MaxValue),
        ("min", TimeSpan.MinValue),
    };

    /// <summary>The filters that must answer as C#.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] Shapes()
    {
        var shapes = new List<(string, Expression<Func<Row, bool>>)>();
        foreach (var (name, op) in Ops)
        {
            shapes.Add(($"D {name} 23h (constant)", Compare("D", op, Expression.Constant(TimeSpan.FromHours(23)))));
            foreach (var (label, value) in Values)
            {
                shapes.Add(($"D {name} {label}", Compare("D", op, Captured(value))));
            }
            foreach (var (label, value) in Values.Where(v => v.Label is "0" or "23h" or "-1s" or "2d"))
            {
                shapes.Add(($"DN {name} {label}", Compare("DN", op, Captured(value))));
            }
            var x = Expression.Parameter(typeof(Row), "x");
            shapes.Add(($"D {name} D2", Expression.Lambda<Func<Row, bool>>(
                Expression.MakeBinary(op, Expression.Property(x, "D"), Expression.Property(x, "D2")), x)));
            shapes.Add(($"D {name} DN", Expression.Lambda<Func<Row, bool>>(
                Expression.MakeBinary(op, Expression.Convert(Expression.Property(x, "D"), typeof(TimeSpan?)), Expression.Property(x, "DN")), x)));
        }

        var h23 = TimeSpan.FromHours(23);
        var m1 = S(-1);
        var zero = TimeSpan.Zero;
        var list = new[] { h23, Day1Hms, m1 };
        var nullable = new TimeSpan?[] { null, m1 };
        shapes.AddRange(new (string, Expression<Func<Row, bool>>)[]
        {
            ("h23 > D (value on the left)", x => h23 > x.D),
            ("!(D > h23)", x => !(x.D > h23)),
            ("!(DN < m1)", x => !(x.DN < m1)),
            ("D > m1 && D < h23", x => x.D > m1 && x.D < h23),
            ("D == h23 || DN == m1", x => x.D == h23 || x.DN == m1),
            ("[h23, 1d2h3m4s, -1s].Contains(D)", x => list.Contains(x.D)),
            ("![h23, 1d2h3m4s, -1s].Contains(D)", x => !list.Contains(x.D)),
            ("[null, -1s].Contains(DN)", x => nullable.Contains(x.DN)),
            ("(DN ?? zero) > m1", x => (x.DN ?? zero) > m1),
            ("DN.GetValueOrDefault() >= h23", x => x.DN.GetValueOrDefault() >= h23),
            ("DN.HasValue && DN.Value < h23", x => x.DN.HasValue && x.DN.Value < h23),
            ("DN == null || DN > m1", x => x.DN == null || x.DN > m1),
            ("D.Equals(h23)", x => x.D.Equals(h23)),
            ("TimeSpan.Equals(D, D2)", x => TimeSpan.Equals(x.D, x.D2)),
            ("TimeSpan.Compare(D, h23) > 0", x => TimeSpan.Compare(x.D, h23) > 0),
            ("0 >= D.CompareTo(D2)", x => 0 >= x.D.CompareTo(x.D2)),
        });
        return shapes.ToArray();
    }

    /// <summary>Shapes no Cosmos DB rendering answers as C# does: refused by name, before any request.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] RefusedShapes()
    {
        var h23 = TimeSpan.FromHours(23);
        return new (string, Expression<Func<Row, bool>>)[]
        {
            ("D.Ticks > 0", x => x.D.Ticks > 0),
            ("D.TotalSeconds > 5", x => x.D.TotalSeconds > 5),
            ("D.Days == 1", x => x.D.Days == 1),
            ("D.Add(h23) > h23", x => x.D.Add(h23) > h23),
            ("D + h23 > h23", x => x.D + h23 > h23),
            ("-D > h23", x => -x.D > h23),
            ("D.Duration() > h23", x => x.D.Duration() > h23),
            ("TimeSpan.Compare(D, h23) > 1", x => TimeSpan.Compare(x.D, h23) > 1),
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
        var dbName = "birko_task905_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        await store.InitAsync();
        return (store, dbName);
    }

    private static async Task DropAsync(AsyncCosmosDBStore<Row> store, string dbName)
    {
        try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task The_serializer_writes_a_TimeSpan_in_the_constant_format()
    {
        var opened = await OpenAsync();
        if (opened == null) return;
        var (store, dbName) = opened.Value;
        try
        {
            await SeedAsync(store.Container!);
            var stored = new List<string>();
            var iterator = store.Container!.GetItemQueryIterator<JsonObject>("SELECT c.Code, c.D FROM c");
            while (iterator.HasMoreResults)
            {
                foreach (var doc in await iterator.ReadNextAsync())
                {
                    if (doc["D"] is { } d) stored.Add(d.GetValue<string>());
                }
            }
            foreach (var s in stored.OrderBy(s => s, StringComparer.Ordinal)) _output.WriteLine(s);
            stored.Should().Contain(new[]
            {
                "00:00:00", "00:00:01", "00:00:01.5000000", "23:00:00", "1.02:03:04", "10.00:00:00", "-00:00:01",
                "-1.00:00:00", "-00:00:00.5000000", "10675199.02:48:05.4775807", "-10675199.02:48:05.4775808", "00:00:00.0000001",
            });
        }
        finally
        {
            await DropAsync(store, dbName);
        }
    }

    /// <param name="refused">Each shape must be refused by name (TASK-905) on every path, and a refused write changes no row.</param>
    private async Task<List<string>> RunMatrixAsync((string Label, Expression<Func<Row, bool>> Filter)[] shapes, bool writes, bool refused)
    {
        var failures = new List<string>();
        var opened = await OpenAsync();
        if (opened == null) return failures;
        var (store, dbName) = opened.Value;
        var wrongShapes = new HashSet<string>();
        var wrongPerPath = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var container = store.Container!;
            var syncStore = new CosmosDBStore<Row>(container);
            var ids = await SeedAsync(container);
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(Seed().Count);

            foreach (var (label, filter) in shapes)
            {
                // C# overflows on some refused shapes (MaxValue + 23h): such a row is not matched.
                var compiled = filter.Compile();
                bool Matches(Row r) { try { return compiled(r); } catch (OverflowException) { return false; } }
                var want = Codes(all.Where(Matches));
                var wantCount = all.Count(Matches);

                async Task Check(string path, Func<Task<string>> run)
                {
                    try
                    {
                        var got = await run();
                        if (refused) failures.Add($"{label} [{path}]: not refused (C# {{{want}}}, got {{{got}}}{(got == want ? ", the same" : "")})");
                        else if (got != want) failures.Add($"{label} [{path}]: C# {{{want}}} got {{{got}}}");
                        else return;
                    }
                    catch (NotSupportedException e) when (refused && e.Message.Contains("TimeSpan") && e.Message.Contains("TASK-905"))
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{label} [{path}]: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                    }
                    wrongShapes.Add(label);
                    wrongPerPath[path] = wrongPerPath.TryGetValue(path, out var n) ? n + 1 : 1;
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
        _output.WriteLine($"{failures.Count} failures, {wrongShapes.Count} of {shapes.Length} shapes wrong on some path; per path: "
            + string.Join(", ", wrongPerPath.Select(p => $"{p.Key} {p.Value}")));
        return failures;
    }

    [Fact]
    public async Task A_TimeSpan_filter_answers_as_CSharp()
    {
        var failures = await RunMatrixAsync(Shapes(), writes: false, refused: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_TimeSpan_filter_answers_as_CSharp_in_writes()
    {
        var failures = await RunMatrixAsync(Shapes(), writes: true, refused: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_TimeSpan_shape_with_no_exact_rendering_is_refused_by_name_on_every_path()
    {
        var failures = await RunMatrixAsync(RefusedShapes(), writes: false, refused: true);
        failures.AddRange(await RunMatrixAsync(RefusedShapes(), writes: true, refused: true));
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    internal static readonly (int? Limit, int? Offset)[] Pages = { (null, null), (3, null), (3, 2), (5, 10) };

    /// <summary>
    /// A sort by a TimeSpan member is refused by name before any request, on both stores: the server sorts the stored text,
    /// which orders neither the day count nor a negative value as C# does, and only a document path can be sorted on.
    /// Measured before the refusal (<see cref="Sort_by_TimeSpan_before_numbers"/>).
    /// </summary>
    [Fact]
    public async Task A_sort_by_a_TimeSpan_member_is_refused_by_name()
    {
        var plan = () => CosmosDateTimeOrder.Plan(OrderBy<Row>.By(x => x.D), "Row");
        plan.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)");
        var nullable = () => CosmosDateTimeOrder.Plan(OrderBy<Row>.ByDescending(x => x.Code!).ThenBy(x => x.DN), "Row");
        nullable.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)");

        var opened = await OpenAsync();
        if (opened == null) return;
        var (store, dbName) = opened.Value;
        try
        {
            await SeedAsync(store.Container!);
            var syncStore = new CosmosDBStore<Row>(store.Container!);
            foreach (var (limit, offset) in Pages)
            {
                var read = async () => await store.ReadAsync(null, OrderBy<Row>.By(x => x.D), limit, offset);
                await read.Should().ThrowAsync<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)");
                var sync = () => syncStore.Read(null, OrderBy<Row>.ByDescending(x => x.DN), limit, offset);
                sync.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)");
            }
        }
        finally
        {
            await DropAsync(store, dbName);
        }
    }

    /// <summary>
    /// The server's order of a TimeSpan member against C#'s, read with the provider's own ORDER BY (the store's path before
    /// the refusal), recorded as the measurement the refusal rests on (rule 54).
    /// </summary>
    [Fact]
    public async Task Sort_by_TimeSpan_before_numbers()
    {
        var opened = await OpenAsync();
        if (opened == null) return;
        var (store, dbName) = opened.Value;
        try
        {
            await SeedAsync(store.Container!);
            var all = (await store.ReadAsync(x => true)).ToList();
            var cases = new (string Label, Func<IQueryable<Row>, IQueryable<Row>> Server, Func<IEnumerable<Row>, IEnumerable<Row>> Oracle)[]
            {
                ("D asc", q => q.OrderBy(x => x.D), r => r.OrderBy(x => x.D)),
                ("D desc", q => q.OrderByDescending(x => x.D), r => r.OrderByDescending(x => x.D)),
                ("DN asc", q => q.OrderBy(x => x.DN), r => r.OrderBy(x => x.DN)),
                ("DN desc", q => q.OrderByDescending(x => x.DN), r => r.OrderByDescending(x => x.DN)),
            };
            var wrong = 0;
            foreach (var (label, server, oracle) in cases)
            {
                var rows = new List<Row>();
                using var iterator = server(store.Container!.GetItemLinqQueryable<Row>()).ToFeedIterator();
                while (iterator.HasMoreResults) rows.AddRange(await iterator.ReadNextAsync());
                var got = string.Join(",", rows.Select(r => r.Code));
                // A document without the member is not returned by an ORDER BY on it; C# reads it as the default.
                var want = string.Join(",", oracle(all.Where(r => rows.Any(x => x.Code == r.Code))).Select(r => r.Code));
                _output.WriteLine($"{label}: server {got}");
                _output.WriteLine($"{label}: C#     {want}");
                if (got != want) wrong++;
            }
            _output.WriteLine($"{wrong} of {cases.Length} sorts ordered otherwise than C#");
            wrong.Should().BeGreaterThan(0, "the stored text does not order as the value: what the refusal rests on");
        }
        finally
        {
            await DropAsync(store, dbName);
        }
    }

    [Theory]
    [InlineData(AggregateFunction.Min)]
    [InlineData(AggregateFunction.Max)]
    [InlineData(AggregateFunction.Sum)]
    [InlineData(AggregateFunction.Avg)]
    public void An_aggregate_of_a_TimeSpan_is_refused_before_any_request(AggregateFunction function)
    {
        var act = () => CosmosAggregationHelper.BuildAggregateQuery(new AggregateQuery<Row>
        {
            Aggregates = new[] { new AggregateField(function, "D", "v") },
        }, null);
        act.Should().Throw<Exception>().Which.Should().Match<Exception>(e => e is NotSupportedException || e is InvalidOperationException);
    }

    [Fact]
    public void A_group_by_a_TimeSpan_is_refused_before_any_request()
    {
        var act = () => CosmosAggregationHelper.BuildAggregateQuery(new AggregateQuery<Row>
        {
            GroupByFields = new[] { "D" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Code", "n") },
        }, null);
        act.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*");
    }
}
