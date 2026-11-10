using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
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
/// TASK-896, live against the Cosmos emulator: a store filter over nullable members returns the rows the compiled C#
/// expression returns, on every path that renders a filter: a read (async and sync), a count, the read behind
/// <c>Update(filter, Action)</c> and <c>Delete(filter)</c>, and an aggregate's WHERE.
/// </summary>
/// <remarks>
/// <para>The oracle is the compiled expression over the documents as read back, so a property MISSING from a stored
/// document (written by older code, or by a serializer that omits nulls) is null to it, as it is to every caller.</para>
/// <para>Measured before the fix (vnext-preview, 2026-11-10), 93 shapes: 53 silently answered other rows and 14 threw the
/// SDK's <c>DocumentQueryException</c>, identically on all seven paths. <see cref="Refused"/> lists what is now refused by
/// name instead; every other shape answers as C#.</para>
/// <para>Gated on <c>BIRKO_COSMOS_CONNECTION</c> (+ <c>BIRKO_COSMOS_CONNECTION_MODE=Gateway</c> for the emulator); with
/// <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.</para>
/// </remarks>
public class Task896NullSemanticsLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";
    private const string ModeEnv = "BIRKO_COSMOS_CONNECTION_MODE";

    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public Task896NullSemanticsLiveTests(ITestOutputHelper output) => _output = output;

    public enum Status { New, Active, Closed }

    public class Row : AbstractModel
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
        public string? S { get; set; }
        public bool? Flag { get; set; }
        public Status? St { get; set; }
        public Status St2 { get; set; }
        public DateTime? When { get; set; }
        public decimal? Price { get; set; }
        public string? Mark { get; set; }
    }

    private static readonly DateTime D1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D3 = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The documents, each with the members it OMITS from the stored JSON (null to every reader).</summary>
    internal static List<(Row Row, string[] Missing)> Seed() => new()
    {
        (new() { Code = "c1", A = 1, Score = 10, S = "a", Flag = true, St = Status.New, St2 = Status.New, When = D1, Price = 1.5m }, Array.Empty<string>()),
        (new() { Code = "c2", A = 2, Score = null, S = null, Flag = null, St = null, St2 = Status.Active, When = null, Price = null }, Array.Empty<string>()),
        (new() { Code = "c3", A = 3, Score = 20, S = "b", Flag = false, St = Status.Active, St2 = Status.Closed, When = D2, Price = 2m }, Array.Empty<string>()),
        (new() { Code = "c4", A = 4, Score = null, S = "", Flag = null, St = Status.Closed, St2 = Status.New, When = null, Price = null }, Array.Empty<string>()),
        (new() { Code = "c5", A = 5, Score = 30, S = " ", Flag = true, St = null, St2 = Status.Active, When = D3, Price = null }, Array.Empty<string>()),
        (new() { Code = "c6", A = 6, Score = null, S = "\uFF21", Flag = false, St = Status.New, St2 = Status.Closed, When = null, Price = 3m }, Array.Empty<string>()),
        (new() { Code = "C7", A = 7, Score = 25, S = "B", Flag = null, St = Status.Active, St2 = Status.New, When = D1, Price = 4m }, Array.Empty<string>()),
        (new() { Code = "c8", A = 8, Score = 5, S = "\U0001F600", Flag = true, St = Status.Closed, St2 = Status.Active, When = D2, Price = null }, Array.Empty<string>()),
        (new() { Code = "c9", A = 9 }, new[] { "Score", "S", "Flag", "St", "When", "Price", "Mark" }),
        (new() { Code = "c10", A = 10, S = "c", Flag = true, St = Status.New, When = D3, Price = 5m }, new[] { "Score", "Mark" }),
    };

    internal static (string Label, Expression<Func<Row, bool>> Filter)[] Shapes()
    {
        int? ten = 10;
        int? nul = null;
        string? nulS = null;
        var scores = new int?[] { 10, null };
        var plainScores = new int?[] { 10, 20 };
        var names = new[] { "a", "b" };
        var namesWithNull = new[] { "a", null };
        return new (string, Expression<Func<Row, bool>>)[]
        {
            // HasValue and null tests
            ("Score.HasValue", x => x.Score.HasValue),
            ("!Score.HasValue", x => !x.Score.HasValue),
            ("Score.HasValue == false", x => x.Score.HasValue == false),
            ("Score != null", x => x.Score != null),
            ("Score == null", x => x.Score == null),
            ("!(Score == null)", x => !(x.Score == null)),
            ("Score == nul", x => x.Score == nul),
            ("Score.HasValue && Score > 15", x => x.Score.HasValue && x.Score > 15),
            ("Score.HasValue && Score.Value > 15", x => x.Score.HasValue && x.Score.Value > 15),
            ("!Score.HasValue || A > 8", x => !x.Score.HasValue || x.A > 8),
            ("S == null", x => x.S == null),
            ("S != null", x => x.S != null),
            ("S == nulS", x => x.S == nulS),
            ("When.HasValue", x => x.When.HasValue),
            ("When == null", x => x.When == null),
            ("Price.HasValue", x => x.Price.HasValue),
            ("Flag.HasValue", x => x.Flag.HasValue),
            ("St.HasValue", x => x.St.HasValue),
            ("St == null", x => x.St == null),

            // Orderings and equality over a nullable member, plain and negated
            ("Score > 15", x => x.Score > 15),
            ("!(Score > 20)", x => !(x.Score > 20)),
            ("!(Score >= 20)", x => !(x.Score >= 20)),
            ("!(Score < 20)", x => !(x.Score < 20)),
            ("!(Score <= 20)", x => !(x.Score <= 20)),
            ("!(20 < Score)", x => !(20 < x.Score)),
            ("!(Score > ten)", x => !(x.Score > ten)),
            ("!(Score > 20) && A > 2", x => !(x.Score > 20) && x.A > 2),
            ("!(Score > 20 || A > 5)", x => !(x.Score > 20 || x.A > 5)),
            ("!(Score > 20 && A > 1)", x => !(x.Score > 20 && x.A > 1)),
            ("!(Score > 5 && Score < 25)", x => !(x.Score > 5 && x.Score < 25)),
            ("!!(Score > 20)", x => !!(x.Score > 20)),
            ("A > 8 || !(Score <= 10)", x => x.A > 8 || !(x.Score <= 10)),
            ("Score == 10", x => x.Score == 10),
            ("Score != 10", x => x.Score != 10),
            ("!(Score == 10)", x => !(x.Score == 10)),
            ("!(Score != 10)", x => !(x.Score != 10)),
            ("Score != ten && A < 9", x => x.Score != ten && x.A < 9),
            ("!(When > D2)", x => !(x.When > D2)),
            ("When != D1", x => x.When != D1),
            ("!(Price >= 3)", x => !(x.Price >= 3m)),
            ("Price != 3", x => x.Price != 3m),
            ("(Score ?? 0) > 15", x => (x.Score ?? 0) > 15),
            ("!((Score ?? 0) > 15)", x => !((x.Score ?? 0) > 15)),
            ("Score.GetValueOrDefault() < 15", x => x.Score.GetValueOrDefault() < 15),

            // Nullable strings
            ("S == \"a\"", x => x.S == "a"),
            ("!(S == \"a\")", x => !(x.S == "a")),
            ("S != \"a\"", x => x.S != "a"),
            ("!(S != \"a\")", x => !(x.S != "a")),
            ("!(S == null)", x => !(x.S == null)),
            ("!(S == \"a\" || S == \"b\")", x => !(x.S == "a" || x.S == "b")),
            ("string.IsNullOrEmpty(S)", x => string.IsNullOrEmpty(x.S)),
            ("!string.IsNullOrEmpty(S)", x => !string.IsNullOrEmpty(x.S)),

            // String comparison
            ("string.CompareOrdinal(Code, \"c2\") > 0", x => string.CompareOrdinal(x.Code, "c2") > 0),
            ("string.Compare(Code, \"c2\", Ordinal) > 0", x => string.Compare(x.Code, "c2", StringComparison.Ordinal) > 0),
            ("string.CompareOrdinal(S, \"b\") < 0", x => string.CompareOrdinal(x.S, "b") < 0),
            ("string.CompareOrdinal(S, \"b\") >= 0", x => string.CompareOrdinal(x.S, "b") >= 0),
            ("!(string.CompareOrdinal(S, \"b\") > 0)", x => !(string.CompareOrdinal(x.S, "b") > 0)),
            ("string.CompareOrdinal(S, null) == 0", x => string.CompareOrdinal(x.S, nulS) == 0),
            ("string.CompareOrdinal(S, null) > 0", x => string.CompareOrdinal(x.S, nulS) > 0),
            ("string.CompareOrdinal(S, \"b\") != 0", x => string.CompareOrdinal(x.S, "b") != 0),
            ("0 < string.CompareOrdinal(S, \"b\")", x => 0 < string.CompareOrdinal(x.S, "b")),
            ("string.CompareOrdinal(\"b\", S) > 0", x => string.CompareOrdinal("b", x.S) > 0),
            ("string.CompareOrdinal(S, \"B\") <= 0 || A == 1", x => string.CompareOrdinal(x.S, "B") <= 0 || x.A == 1),

            // Nullable bool
            ("Flag == true", x => x.Flag == true),
            ("Flag == false", x => x.Flag == false),
            ("!(Flag == true)", x => !(x.Flag == true)),
            ("Flag != true", x => x.Flag != true),
            ("Flag != false", x => x.Flag != false),
            ("Flag ?? false", x => x.Flag ?? false),
            ("!(Flag ?? false)", x => !(x.Flag ?? false)),
            ("Flag.HasValue && Flag.Value", x => x.Flag.HasValue && x.Flag.Value),
            ("Flag.GetValueOrDefault()", x => x.Flag.GetValueOrDefault()),

            // Enums
            ("St == Active", x => x.St == Status.Active),
            ("St != Active", x => x.St != Status.Active),
            ("!(St == Active)", x => !(x.St == Status.Active)),
            ("St2 != Active", x => x.St2 != Status.Active),
            ("!(St > New)", x => !(x.St > Status.New)),

            // Contains
            ("scores.Contains(Score)", x => scores.Contains(x.Score)),
            ("!scores.Contains(Score)", x => !scores.Contains(x.Score)),
            ("plainScores.Contains(Score)", x => plainScores.Contains(x.Score)),
            ("!plainScores.Contains(Score)", x => !plainScores.Contains(x.Score)),
            ("names.Contains(S)", x => names.Contains(x.S)),
            ("!names.Contains(S)", x => !names.Contains(x.S)),
            ("namesWithNull.Contains(S)", x => namesWithNull.Contains(x.S)),
            ("!namesWithNull.Contains(S)", x => !namesWithNull.Contains(x.S)),
            ("S.Contains(\"a\") guarded", x => x.S != null && x.S.Contains("a")),
            ("!(S != null && S.StartsWith(\"b\"))", x => !(x.S != null && x.S.StartsWith("b"))),

            // Column against column
            ("Score > A", x => x.Score > x.A),
            ("!(Score > A)", x => !(x.Score > x.A)),
            ("Score == A * 5", x => x.Score == x.A * 5),
            ("!(Score == A * 5)", x => !(x.Score == x.A * 5)),
        };
    }

    /// <summary>
    /// Shapes no Cosmos DB rendering answers as C# does, refused by name before any request (measured: the provider
    /// rendered the culture comparisons as ordinal ones and gave other rows; TRIM keeps U+00A0, U+0085 and U+001C).
    /// </summary>
    internal static (string Label, Expression<Func<Row, bool>> Filter)[] Refused()
    {
        var high = ((char)0xFFFF).ToString();
        return new (string, Expression<Func<Row, bool>>)[]
        {
            ("string.IsNullOrWhiteSpace(S)", x => string.IsNullOrWhiteSpace(x.S)),
            ("!string.IsNullOrWhiteSpace(S)", x => !string.IsNullOrWhiteSpace(x.S)),
            ("string.Compare(Code, \"c2\") > 0", x => string.Compare(x.Code, "c2") > 0),
            ("string.Compare(Code, \"c2\") <= 0", x => string.Compare(x.Code, "c2") <= 0),
            ("Code.CompareTo(\"c2\") > 0", x => x.Code!.CompareTo("c2") > 0),
            ("string.Compare(S, \"b\", true) == 0", x => string.Compare(x.S, "b", true) == 0),
            ("string.Compare(S, \"b\", OrdinalIgnoreCase) == 0", x => string.Compare(x.S, "b", StringComparison.OrdinalIgnoreCase) == 0),
            ("string.Compare(S, \"b\", InvariantCulture) > 0", x => string.Compare(x.S, "b", StringComparison.InvariantCulture) > 0),
            ("string.CompareOrdinal(S, U+FFFF) > 0", x => string.CompareOrdinal(x.S, high) > 0),
            ("string.CompareOrdinal(S, Code) > 0", x => string.CompareOrdinal(x.S, x.Code) > 0),
            ("string.CompareOrdinal(S, \"b\") == 1", x => string.CompareOrdinal(x.S, "b") == 1),
            ("string.CompareOrdinal(S.ToLower(), \"b\") > 0", x => string.CompareOrdinal(x.S!.ToLower(), "b") > 0),
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

    /// <summary>Writes the seed as raw JSON, each document without the members it omits, and returns the rows' Guids.</summary>
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

    /// <summary>Every shape on every path; returns a report line per divergence.</summary>
    private async Task<List<string>> RunMatrixAsync(bool writes)
    {
        var failures = new List<string>();
        var conn = ResolveConnection();
        if (conn == null) return failures;

        var dbName = "birko_task896_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<Row>(conn, dbName, null, LiveSettings());
        try
        {
            await store.InitAsync();
            var container = store.Container!;
            var syncStore = new CosmosDBStore<Row>(container);
            var ids = await SeedAsync(container);
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(10);

            var cases = Shapes().Select(s => (s.Label, s.Filter, Refused: false))
                .Concat(Refused().Select(s => (s.Label, s.Filter, Refused: true)));
            foreach (var (label, filter, refused) in cases)
            {
                List<Row> expected;
                try { expected = refused ? new List<Row>() : all.Where(filter.Compile()).ToList(); }
                catch (Exception e) { failures.Add($"{label}: the oracle throws {e.GetType().Name}"); continue; }
                var want = refused ? "refused" : Codes(expected);

                async Task Check(string path, Func<Task<string>> run)
                {
                    try
                    {
                        var got = await run();
                        if (got != want) failures.Add($"{label} [{path}]: C# {{{want}}} got {{{got}}}");
                    }
                    catch (NotSupportedException e) when (refused && e.Message.Contains("(TASK-896)"))
                    {
                        // Refused before any request: a write must have changed nothing.
                        if (writes)
                        {
                            var now = (await store.ReadAsync(x => true)).ToList();
                            if (now.Count != all.Count || now.Any(r => r.Mark != null))
                                failures.Add($"{label} [{path}]: refused, but the rows changed");
                        }
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
                        var rows = await store.AggregateAsync(new AggregateQuery<Row>
                        {
                            Filter = filter,
                            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "A", "cnt") },
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
    public async Task Reads_counts_and_aggregates_match_CSharp()
    {
        var failures = await RunMatrixAsync(writes: false);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task Update_and_delete_filters_match_CSharp()
    {
        var failures = await RunMatrixAsync(writes: true);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    // ----- offline: what the store's LINQ provider is handed, no server -----

    private static string Where(Expression<Func<Row, bool>> filter)
    {
        using var client = new CosmosClient("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");
        var text = client.GetContainer("db", "c").GetItemLinqQueryable<Row>()
            .Where(CosmosFilterRewrite.Prepare(filter)!).ToQueryDefinition().QueryText;
        return text.Substring(text.IndexOf(" WHERE ", StringComparison.Ordinal) + 7);
    }

    private const string ScorePresent = "(IS_DEFINED(root[\"Score\"]) AND (NOT IS_NULL(root[\"Score\"])))";
    private const string ScoreAbsent = "((NOT IS_DEFINED(root[\"Score\"])) OR IS_NULL(root[\"Score\"]))";

    [Fact]
    public void HasValue_and_null_tests_read_null_and_missing_as_null()
    {
        Where(x => x.Score.HasValue).Should().Be(ScorePresent, "the provider's IS_DEFINED alone is true of a stored null");
        Where(x => x.Score == null).Should().Be(ScoreAbsent, "c.Score = null skips a missing member");
        Where(x => x.Score != null).Should().Be(ScorePresent);
    }

    [Fact]
    public void A_negated_ordering_is_guarded_and_a_plain_one_keeps_its_rendering()
    {
        Where(x => !(x.Score > 20)).Should().Be($"(NOT ({ScorePresent} AND (root[\"Score\"] > 20)))");
        Where(x => x.Score > 20).Should().Be("(root[\"Score\"] > 20)");
        Where(x => x.A > 2).Should().Be("(root[\"A\"] > 2)", "a non-nullable member needs no guard");
    }

    [Fact]
    public void An_ordinal_comparison_renders_as_a_plain_ordering_with_null_first()
    {
        Where(x => string.CompareOrdinal(x.S, "b") < 0)
            .Should().Be("(((NOT IS_DEFINED(root[\"S\"])) OR IS_NULL(root[\"S\"])) OR (root[\"S\"] < \"b\"))");
        Where(x => string.CompareOrdinal("b", x.S) < 0)
            .Should().Be("((IS_DEFINED(root[\"S\"]) AND (NOT IS_NULL(root[\"S\"]))) AND (root[\"S\"] > \"b\"))");
    }

    [Fact]
    public void Preparing_twice_answers_the_same_rows()
    {
        // A chunked write prepares each chunk again; the produced ordinal ordering must not be read as a culture one.
        Expression<Func<Row, bool>> filter = x => !(string.CompareOrdinal(x.S, "b") > 0) && !(x.Score > 20);
        var once = CosmosFilterRewrite.Prepare(filter)!;
        var act = () => CosmosFilterRewrite.Prepare(once);
        act.Should().NotThrow();
    }

    public static IEnumerable<object[]> RefusedLabels() => Refused().Select(r => new object[] { r.Label });

    [Theory]
    [MemberData(nameof(RefusedLabels))]
    public void A_shape_Cosmos_cannot_answer_as_CSharp_is_refused_by_name(string label)
    {
        var filter = Refused().Single(r => r.Label == label).Filter;
        var act = () => CosmosFilterRewrite.Prepare(filter);
        act.Should().Throw<NotSupportedException>().WithMessage("*(TASK-896)*");
    }
}