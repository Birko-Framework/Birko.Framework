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
/// TASK-899, live against the Cosmos emulator: the filter shapes TASK-896 left uncovered return the rows the compiled C#
/// expression returns over the documents as read back, on a read (async and sync), a count, an aggregate's WHERE and the
/// reads behind <c>Update(filter, Action)</c> and <c>Delete(filter)</c>:
/// <list type="bullet">
/// <item>a NON-nullable member missing from a document (written before the member existed), which every reader sees as the
/// type's default;</item>
/// <item>nested predicates (<c>Any</c> / <c>All</c>) over nullable and missing members of the array's elements.</item>
/// </list>
/// </summary>
/// <remarks>
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the emulator); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.
/// </remarks>
public class Task899FilterLeftoversLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task899FilterLeftoversLiveTests(ITestOutputHelper output) => _output = output;

    public enum Status { New, Active, Closed }

    public class Item
    {
        public int? Score { get; set; }
        public int N { get; set; }
        public string? S { get; set; }
    }

    public class Row : AbstractModel
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public bool Del { get; set; }
        public Status E { get; set; }
        public DateTime D { get; set; }
        public decimal M { get; set; }
        public double Dbl { get; set; }
        public long L { get; set; }
        public int? Score { get; set; }
        public List<Item> Items { get; set; } = new();
        public string? Mark { get; set; }
    }

    private static readonly DateTime D1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Item It(int? score, int n, string? s) => new() { Score = score, N = n, S = s };

    /// <summary>
    /// The documents, each with the top-level members it OMITS from the stored JSON and, per element of <c>Items</c>, the
    /// members that element omits. Every reader sees an omitted member as its type's default.
    /// </summary>
    internal static List<(Row Row, string[] Missing, string[][] ItemMissing)> Seed() => new()
    {
        (new() { Code = "c1", A = 1, Del = false, E = Status.New, D = D1, M = 1.5m, Dbl = 0.5, L = 1, Score = 10,
                 Items = new() { It(10, 1, "a"), It(null, 2, null) } },
         Array.Empty<string>(), new[] { Array.Empty<string>(), Array.Empty<string>() }),
        (new() { Code = "c2", A = 0, Del = true, E = Status.Active, D = D2, M = 0m, Dbl = 2, L = 0, Score = null,
                 Items = new() { It(20, 0, "b") } },
         Array.Empty<string>(), new[] { Array.Empty<string>() }),
        (new() { Code = "c3", A = 5, Del = true, E = Status.Closed, D = default, M = 3m, Dbl = 0, L = 7, Score = 30,
                 Items = new() { It(5, 3, "c"), It(30, 4, "d") } },
         Array.Empty<string>(), new[] { Array.Empty<string>(), Array.Empty<string>() }),
        // Written before A, Del, E, D, M, Dbl and L existed.
        (new() { Code = "c4", Score = 20, Items = new() { It(null, 0, null) } },
         new[] { "A", "Del", "E", "D", "M", "Dbl", "L" }, new[] { new[] { "Score", "N", "S" } }),
        (new() { Code = "c5", Score = null, Items = new() { It(7, 0, null), It(null, 5, "e") } },
         new[] { "A", "Del", "E", "D", "M", "Dbl", "L", "Score" }, new[] { new[] { "N", "S" }, new[] { "Score" } }),
        (new() { Code = "c6", A = 3, Del = false, E = Status.New, D = D1, M = 2m, Dbl = 1, L = 3, Score = 5,
                 Items = new() },
         Array.Empty<string>(), Array.Empty<string[]>()),
        (new() { Code = "c7", A = 0, Del = false, E = Status.New, D = D2, M = 0m, Dbl = 0, L = 0, Score = 0,
                 Items = new() { It(0, 0, "") } },
         new[] { "Del" }, new[] { new[] { "S" } }),
    };

    /// <summary>A non-nullable member missing from a document: C# reads its default.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] MissingMemberShapes()
    {
        var withZero = new[] { 0, 5 };
        var withoutZero = new[] { 1, 5 };
        var statuses = new[] { Status.New, Status.Closed };
        var zero = 0;
        return new (string, Expression<Func<Row, bool>>)[]
        {
            ("A == 0", x => x.A == 0),
            ("A == zero", x => x.A == zero),
            ("0 == A", x => 0 == x.A),
            ("A != 5", x => x.A != 5),
            ("A != 0", x => x.A != 0),
            ("A < 3", x => x.A < 3),
            ("A > 3", x => x.A > 3),
            ("A >= 0", x => x.A >= 0),
            ("!(A > 3)", x => !(x.A > 3)),
            ("!(A == 0)", x => !(x.A == 0)),
            ("!(A < 3)", x => !(x.A < 3)),
            ("A == 5", x => x.A == 5),
            ("!(A == 5) && Code != null", x => !(x.A == 5) && x.Code != null),
            ("A < 3 || Code == \"c3\"", x => x.A < 3 || x.Code == "c3"),
            ("withZero.Contains(A)", x => withZero.Contains(x.A)),
            ("!withZero.Contains(A)", x => !withZero.Contains(x.A)),
            ("withoutZero.Contains(A)", x => withoutZero.Contains(x.A)),
            ("!withoutZero.Contains(A)", x => !withoutZero.Contains(x.A)),
            ("Del", x => x.Del),
            ("!Del", x => !x.Del),
            ("Del == false", x => x.Del == false),
            ("Del != true", x => x.Del != true),
            ("!Del && A >= 0", x => !x.Del && x.A >= 0),
            ("Del || A == 0", x => x.Del || x.A == 0),
            ("!(Del || A > 3)", x => !(x.Del || x.A > 3)),
            ("E == New", x => x.E == Status.New),
            ("E != Active", x => x.E != Status.Active),
            ("!(E > New)", x => !(x.E > Status.New)),
            ("statuses.Contains(E)", x => statuses.Contains(x.E)),
            ("D == default", x => x.D == default(DateTime)),
            ("D < D2", x => x.D < D2),
            ("!(D >= D1)", x => !(x.D >= D1)),
            ("M == 0", x => x.M == 0m),
            ("M < 1", x => x.M < 1m),
            ("Dbl <= 0.5", x => x.Dbl <= 0.5),
            ("L != 7", x => x.L != 7),
            ("A > Score", x => x.A > x.Score),
            ("!(A >= Score)", x => !(x.A >= x.Score)),
            ("A == L", x => x.A == x.L),
            ("A + 1 == 1", x => x.A + 1 == 1),
            ("A * 2 < 4", x => x.A * 2 < 4),
            ("!(A + 1 > 1)", x => !(x.A + 1 > 1)),
            ("(A == 0) == true", x => (x.A == 0) == true),
            ("(A > 0 ? 1 : 0) == 0", x => (x.A > 0 ? 1 : 0) == 0),
        };
    }

    /// <summary>Nested predicates over the elements' nullable and missing members.</summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] NestedShapes()
    {
        int? nul = null;
        var withNull = new int?[] { 7, null };
        return new (string, Expression<Func<Row, bool>>)[]
        {
            ("Any(Score > 5)", x => x.Items.Any(i => i.Score > 5)),
            ("Any(!(Score > 5))", x => x.Items.Any(i => !(i.Score > 5))),
            ("Any(Score == null)", x => x.Items.Any(i => i.Score == null)),
            ("Any(Score == nul)", x => x.Items.Any(i => i.Score == nul)),
            ("Any(Score != null)", x => x.Items.Any(i => i.Score != null)),
            ("Any(Score.HasValue)", x => x.Items.Any(i => i.Score.HasValue)),
            ("Any(!Score.HasValue)", x => x.Items.Any(i => !i.Score.HasValue)),
            ("Any(Score != 5)", x => x.Items.Any(i => i.Score != 5)),
            ("Any(S == null)", x => x.Items.Any(i => i.S == null)),
            ("Any(S != \"a\")", x => x.Items.Any(i => i.S != "a")),
            ("Any(N == 0)", x => x.Items.Any(i => i.N == 0)),
            ("Any(!(N > 0))", x => x.Items.Any(i => !(i.N > 0))),
            ("Any(withNull.Contains(Score))", x => x.Items.Any(i => withNull.Contains(i.Score))),
            ("Any((Score ?? 0) > 5)", x => x.Items.Any(i => (i.Score ?? 0) > 5)),
            ("Any(Score > A)", x => x.Items.Any(i => i.Score > x.A)),
            ("!Any(Score > 5)", x => !x.Items.Any(i => i.Score > 5)),
            ("!Any(!(Score > 5))", x => !x.Items.Any(i => !(i.Score > 5))),
            ("Any()", x => x.Items.Any()),
            ("All(Score > 5)", x => x.Items.All(i => i.Score > 5)),
            ("All(!(Score > 5))", x => x.Items.All(i => !(i.Score > 5))),
            ("All(Score == null)", x => x.Items.All(i => i.Score == null)),
            ("All(Score != null)", x => x.Items.All(i => i.Score != null)),
            ("All(Score.HasValue)", x => x.Items.All(i => i.Score.HasValue)),
            ("All(S != \"a\")", x => x.Items.All(i => i.S != "a")),
            ("All(N == 0)", x => x.Items.All(i => i.N == 0)),
            ("All(N > 0)", x => x.Items.All(i => i.N > 0)),
            ("!All(Score > 5)", x => !x.Items.All(i => i.Score > 5)),
            ("Count(Score > 5) > 0", x => x.Items.Count(i => i.Score > 5) > 0),
            ("Count(!(Score > 5)) == 1", x => x.Items.Count(i => !(i.Score > 5)) == 1),
            ("Where(Score == null).Any()", x => x.Items.Where(i => i.Score == null).Any()),
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

    /// <summary>Writes the seed as raw JSON, each document and element without the members it omits.</summary>
    private static async Task<Dictionary<string, Guid>> SeedAsync(Container container, Dictionary<string, Guid>? ids = null)
    {
        ids ??= new Dictionary<string, Guid>();
        var writes = new List<Task>();
        foreach (var (row, missing, itemMissing) in Seed())
        {
            if (!ids.TryGetValue(row.Code!, out var guid))
            {
                guid = Guid.NewGuid();
                ids[row.Code!] = guid;
            }
            row.Guid = guid;
            var node = (JsonObject)JsonSerializer.SerializeToNode(row)!;
            foreach (var name in missing) node.Remove(name);
            var items = (JsonArray)node["Items"]!;
            for (var i = 0; i < itemMissing.Length; i++)
            {
                foreach (var name in itemMissing[i]) ((JsonObject)items[i]!).Remove(name);
            }
            node["id"] = guid.ToString();
            writes.Add(container.UpsertItemAsync(node, new PartitionKey(guid.ToString())));
        }
        await Task.WhenAll(writes);
        return ids;
    }

    private static string Codes(IEnumerable<Row> rows)
        => string.Join(",", rows.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal));

    /// <param name="aggregateRefused">The aggregate path refuses the shapes by name (TASK-876): a nested predicate renders
    /// as a JOIN, not a plain WHERE the aggregation can be placed behind.</param>
    private async Task<List<string>> RunMatrixAsync(
        (string Label, Expression<Func<Row, bool>> Filter)[] shapes, bool writes, bool aggregateRefused = false)
    {
        var failures = new List<string>();
        var conn = ResolveConnection();
        if (conn == null) return failures;

        var dbName = "birko_task899_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            await store.InitAsync();
            var container = store.Container!;
            var syncStore = new CosmosDBStore<Row>(container);
            var ids = await SeedAsync(container);
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(Seed().Count);

            foreach (var (label, filter) in shapes)
            {
                List<Row> expected;
                try { expected = all.Where(filter.Compile()).ToList(); }
                catch (Exception e) { failures.Add($"{label}: the oracle throws {e.GetType().Name}"); continue; }
                var want = Codes(expected);

                async Task Check(string path, Func<Task<string>> run)
                {
                    try
                    {
                        var got = await run();
                        if (got != want) failures.Add($"{label} [{path}]: C# {{{want}}} got {{{got}}}");
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
                    await Check("count", async () =>
                    {
                        var n = await store.CountAsync(filter);
                        return n == expected.Count ? want : "count " + n;
                    });
                    await Check("sync count", () =>
                    {
                        var n = syncStore.Count(filter);
                        return Task.FromResult(n == expected.Count ? want : "count " + n);
                    });
                    await Check("aggregate", async () =>
                    {
                        if (aggregateRefused)
                        {
                            try
                            {
                                await store.AggregateAsync(new AggregateQuery<Row>
                                {
                                    Filter = filter,
                                    Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Code", "cnt") },
                                });
                                return "not refused";
                            }
                            catch (NotSupportedException e) when (e.Message.Contains("(TASK-876)"))
                            {
                                return want;
                            }
                        }
                        var rows = await store.AggregateAsync(new AggregateQuery<Row>
                        {
                            Filter = filter,
                            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Code", "cnt") },
                        });
                        var n = rows.Count == 0 ? 0 : rows[0].GetValue<long>("cnt");
                        return n == expected.Count ? want : "count " + n;
                    });
                    continue;
                }

                await Check("update", async () =>
                {
                    await SeedAsync(container, ids);
                    await store.UpdateAsync(filter, r => r.Mark = "hit");
                    var marked = (await store.ReadAsync(x => true)).Where(r => r.Mark == "hit");
                    return Codes(marked);
                });
                await Check("delete", async () =>
                {
                    await SeedAsync(container, ids);
                    await store.DeleteAsync(filter);
                    var left = (await store.ReadAsync(x => true)).Select(r => r.Code).ToHashSet();
                    return Codes(all.Where(r => !left.Contains(r.Code)));
                });
            }
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }

        foreach (var f in failures) _output.WriteLine(f);
        return failures;
    }

    [Fact]
    public async Task A_missing_non_nullable_member_reads_as_its_default()
    {
        var failures = await RunMatrixAsync(MissingMemberShapes(), writes: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_missing_non_nullable_member_reads_as_its_default_in_writes()
    {
        var failures = await RunMatrixAsync(MissingMemberShapes(), writes: true);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task Nested_predicates_answer_null_and_missing_members_as_CSharp()
    {
        var failures = await RunMatrixAsync(NestedShapes(), writes: false, aggregateRefused: true);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task Nested_predicates_answer_null_and_missing_members_as_CSharp_in_writes()
    {
        var failures = await RunMatrixAsync(NestedShapes(), writes: true);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }
}

/// <summary>
/// TASK-899, live: a list past Cosmos DB's query-text limit (524,288 characters) that holds <c>null</c>, or a non-nullable
/// member's default, is split into chunks like any other, with the "null or missing" leg in one chunk only, and answers
/// what C# answers on reads, counts, updates and deletes. Before, TASK-896's <c>null OR IN (..)</c> put the IN under an OR,
/// which the planner refuses past the limit.
/// </summary>
public class Task899AbsentLegChunkingLiveTests
{
    private readonly ITestOutputHelper _output;

    public Task899AbsentLegChunkingLiveTests(ITestOutputHelper output) => _output = output;

    public class Doc : AbstractModel
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public int N { get; set; }
        public string? Mark { get; set; }
    }

    private static List<(Doc Doc, string[] Missing)> Seed() => new()
    {
        (new() { Code = "d1", Name = "n1", N = 0 }, Array.Empty<string>()),
        (new() { Code = "d2", Name = "n2", N = 3 }, Array.Empty<string>()),
        (new() { Code = "d3", Name = null, N = 5 }, Array.Empty<string>()),
        (new() { Code = "d4" }, new[] { "Name", "N" }),
        (new() { Code = "d5", Name = "n5", N = 7 }, Array.Empty<string>()),
        (new() { Code = "d6", Name = "n6", N = 0 }, Array.Empty<string>()),
        (new() { Code = "d7", Name = null }, new[] { "N" }),
    };

    /// <summary>The lists: each far past the limit as one query, holding a few stored values and null or the default.</summary>
    internal static (string Label, Expression<Func<Doc, bool>> Filter)[] Shapes()
    {
        var names = Enumerable.Range(0, 30_000).Select(i => "filler-name-" + i.ToString("D6")).ToList<string?>();
        names.Insert(15_000, "n1");
        names.Add("n5");
        names.Insert(20_000, null);
        var ints = Enumerable.Range(1_000_000, 100_000).ToList();
        ints.Insert(50_000, 3);
        ints.Add(0);
        var intsNoDefault = Enumerable.Range(1_000_000, 100_000).Append(3).ToList();
        return new (string, Expression<Func<Doc, bool>>)[]
        {
            ("names.Contains(Name)", x => names.Contains(x.Name)),
            ("Code != null && names.Contains(Name)", x => x.Code != null && names.Contains(x.Name)),
            ("ints.Contains(N)", x => ints.Contains(x.N)),
            ("ints.Contains(N) && Code != \"d6\"", x => ints.Contains(x.N) && x.Code != "d6"),
            ("intsNoDefault.Contains(N)", x => intsNoDefault.Contains(x.N)),
        };
    }

    [Fact]
    public async Task A_list_holding_null_or_the_default_is_split_past_the_limit_and_answers_as_CSharp()
    {
        var live = new CosmosLive(_output);
        if (!live.Ready()) return;
        var (conn, dbName, settings) = live.Next();
        var store = new AsyncCosmosDBStore<Doc>(conn, dbName, null, settings);
        var failures = new List<string>();
        try
        {
            await store.InitAsync();
            var container = store.Container!;
            var sync = new CosmosDBStore<Doc>(container);
            var ids = new Dictionary<string, Guid>();

            async Task SeedAsync()
            {
                foreach (var (doc, missing) in Seed())
                {
                    if (!ids.TryGetValue(doc.Code!, out var guid)) ids[doc.Code!] = guid = Guid.NewGuid();
                    doc.Guid = guid;
                    var node = (JsonObject)JsonSerializer.SerializeToNode(doc)!;
                    foreach (var name in missing) node.Remove(name);
                    node["id"] = guid.ToString();
                    await container.UpsertItemAsync(node, new PartitionKey(guid.ToString()));
                }
            }

            static string Codes(IEnumerable<Doc> rows) => string.Join(",", rows.Select(r => r.Code).OrderBy(c => c, StringComparer.Ordinal));

            await SeedAsync();
            var all = (await store.ReadAsync(x => true)).ToList();
            foreach (var (label, filter) in Shapes())
            {
                var expected = all.Where(filter.Compile()).ToList();
                var want = Codes(expected);
                async Task Check(string path, Func<Task<string>> run)
                {
                    try
                    {
                        var got = await run();
                        if (got != want) failures.Add($"{label} [{path}]: C# {{{want}}} got {{{got}}}");
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{label} [{path}]: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                    }
                }

                await Check("read", async () => Codes(await store.ReadAsync(filter)));
                await Check("sync read", () => Task.FromResult(Codes(sync.Read(filter))));
                await Check("count", async () => (await store.CountAsync(filter)) == expected.Count ? want : "wrong count");
                await Check("sync count", () => Task.FromResult(sync.Count(filter) == expected.Count ? want : "wrong count"));
                await Check("update", async () =>
                {
                    await SeedAsync();
                    await store.UpdateAsync(filter, r => r.Mark = (r.Mark ?? "") + "hit");
                    var now = (await store.ReadAsync(x => true)).ToList();
                    if (now.Any(r => r.Mark != null && r.Mark != "hit")) return "a row updated twice";
                    return Codes(now.Where(r => r.Mark == "hit"));
                });
                await Check("delete", async () =>
                {
                    await SeedAsync();
                    await store.DeleteAsync(filter);
                    var left = (await store.ReadAsync(x => true)).Select(r => r.Code).ToHashSet();
                    return Codes(all.Where(r => !left.Contains(r.Code)));
                });
                await SeedAsync();
            }
        }
        finally
        {
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }

        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public void A_negated_list_holding_null_is_still_refused_past_the_limit()
    {
        var names = Enumerable.Range(0, 30_000).Select(i => "filler-name-" + i.ToString("D6")).Append(null).ToList();
        Expression<Func<Doc, bool>> filter = x => !names.Contains(x.Name);
        var prepared = CosmosFilterRewrite.Prepare(filter)!;
        using var client = new CosmosClient("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");
        var queryable = client.GetContainer("db", "c").GetItemLinqQueryable<Doc>();
        var act = () => CosmosInListChunking.Plan(prepared, "read", 1000, CosmosInListChunking.DefaultMaxQueryTextLength, false,
            f => queryable.Where(f).ToQueryDefinition().QueryText.Length);
        act.Should().Throw<NotSupportedException>();
    }
}

/// <summary>TASK-899, offline: what the store's LINQ provider is handed, and how a list with an absent leg is planned.</summary>
public class Task899FilterRenderingTests
{
    private static readonly CosmosClient Client = new("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");

    private static string Where<T>(Expression<Func<T, bool>> filter)
    {
        var text = Client.GetContainer("db", "c").GetItemLinqQueryable<T>().Where(CosmosFilterRewrite.Prepare(filter)!).ToQueryDefinition().QueryText;
        return text.Substring(text.IndexOf(" WHERE ", StringComparison.Ordinal) + 7);
    }

    [Fact]
    public void A_missing_member_answers_as_its_default_and_a_test_the_default_fails_keeps_its_rendering()
    {
        Where<Task899FilterLeftoversLiveTests.Row>(x => x.A == 0).Should().Be("((NOT IS_DEFINED(root[\"A\"])) OR (root[\"A\"] = 0))");
        Where<Task899FilterLeftoversLiveTests.Row>(x => x.A > 3).Should().Be("(root[\"A\"] > 3)", "the default fails it: a missing member is undefined, which is false");
        Where<Task899FilterLeftoversLiveTests.Row>(x => !(x.A > 3)).Should().Be("(NOT (IS_DEFINED(root[\"A\"]) AND (root[\"A\"] > 3)))");
        Where<Task899FilterLeftoversLiveTests.Row>(x => !x.Del).Should().Be("(NOT (IS_DEFINED(root[\"Del\"]) AND root[\"Del\"]))");
        Where<Task899FilterLeftoversLiveTests.Row>(x => x.A + 1 == 1).Should().Be("(((IS_DEFINED(root[\"A\"]) ? root[\"A\"] : 0) + 1) = 1)");
    }

    [Fact]
    public void All_is_rendered_as_not_any_of_the_negation_and_a_nested_predicate_takes_the_null_semantics()
    {
        Where<Task899FilterLeftoversLiveTests.Row>(x => x.Items.All(i => i.Score > 5)).Should().Contain(
            "(NOT ((IS_DEFINED(i0[\"Score\"]) AND (NOT IS_NULL(i0[\"Score\"]))) AND (i0[\"Score\"] > 5))))) AS v0 WHERE (NOT v0)");
        Where<Task899FilterLeftoversLiveTests.Row>(x => x.Items.Any(i => i.S == null)).Should().Contain(
            "((NOT IS_DEFINED(i0[\"S\"])) OR IS_NULL(i0[\"S\"]))");
    }

    [Fact]
    public void Preparing_twice_keeps_the_markers_and_answers_the_same()
    {
        var ints = new[] { 0, 5 };
        Expression<Func<Task899FilterLeftoversLiveTests.Row, bool>> filter = x => ints.Contains(x.A) && !(x.A + 1 > 1) && !x.Del;
        var once = CosmosFilterRewrite.Prepare(filter)!;
        var twice = CosmosFilterRewrite.Prepare(once)!;
        var queryable = Client.GetContainer("db", "c").GetItemLinqQueryable<Task899FilterLeftoversLiveTests.Row>();
        queryable.Where(twice).ToQueryDefinition().QueryText.Should().NotContain("IS_DEFINED((IS_DEFINED", "a marker's operand is the member");
    }

    private static IReadOnlyList<Expression<Func<Task899AbsentLegChunkingLiveTests.Doc, bool>>>? Plan(Expression<Func<Task899AbsentLegChunkingLiveTests.Doc, bool>> filter)
    {
        var queryable = Client.GetContainer("db", "c").GetItemLinqQueryable<Task899AbsentLegChunkingLiveTests.Doc>();
        return CosmosInListChunking.Plan(CosmosFilterRewrite.Prepare(filter)!, "read", 10, 4000, false,
            f => queryable.Where(f).ToQueryDefinition().QueryText.Length);
    }

    [Fact]
    public void A_list_holding_null_is_split_with_the_null_leg_in_the_first_chunk_only()
    {
        var names = Enumerable.Range(0, 35).Select(i => "n" + i).Append(null).ToList();
        var chunks = Plan(x => names.Contains(x.Name))!;
        chunks.Should().HaveCount(4);
        chunks.Count(c => c.ToString().Contains("IsNull")).Should().Be(1);
        chunks[0].ToString().Should().Contain("IsNull");
    }

    [Fact]
    public void A_list_holding_the_default_is_split_with_the_missing_leg_in_the_chunk_holding_the_default()
    {
        var ints = Enumerable.Range(1, 35).Append(0).ToList();
        var chunks = Plan(x => ints.Contains(x.N))!;
        chunks.Should().HaveCount(4);
        chunks.Count(c => c.ToString().Contains("IsDefined")).Should().Be(1);
        var withLeg = chunks.Single(c => c.ToString().Contains("IsDefined"));
        withLeg.ToString().Should().Contain("value(System.Collections.Generic.List`1[System.Int32])", "the leg goes with the chunk holding 0");
        var leading = (MethodCallExpression)((BinaryExpression)withLeg.Body).Right;
        ((System.Collections.Generic.List<int>)((ConstantExpression)leading.Object!).Value!).Should().Contain(0);
        foreach (var other in chunks.Where(c => c != withLeg))
        {
            CosmosFilterRewrite.Prepare(other)!.ToString().Should().NotContain("IsDefined", "re-preparing a chunk without the default adds no leg");
        }
    }
}
