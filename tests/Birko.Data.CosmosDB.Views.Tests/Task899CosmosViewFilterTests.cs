using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Serialization;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-899, live against the Cosmos emulator: a view filter over a NON-nullable member that a document omits answers as
/// C# does, which reads the type's default, on a projection read and its count; and a list past the query limit that holds
/// null or the default is split and answers as C# does.
/// </summary>
/// <remarks>
/// Measured before the fix (vnext-preview, 2026-11-10): <c>v.A == 0</c>, <c>v.A != 5</c>, <c>!(v.A &gt; 3)</c> and a list
/// holding 0 skipped the documents written without <c>A</c>, because Cosmos DB reads a missing member as undefined under every
/// operator. Gated on <c>BIRKO_COSMOS_CONNECTION</c>; with <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.
/// </remarks>
public sealed class Task899CosmosViewFilterTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<RowView> _oracle = new();

    public Task899CosmosViewFilterTests(ITestOutputHelper output) => _output = output;

    public enum Status { New, Active, Closed }

    public class Row
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public Status E { get; set; }
        public DateTime D { get; set; }
        public bool Del { get; set; }
        public string? Name { get; set; }
    }

    public class RowView
    {
        public string? Code { get; set; }
        public int Amount { get; set; }
        public Status E { get; set; }
        public DateTime D { get; set; }
        public bool Del { get; set; }
        public string? Name { get; set; }
    }

    private static readonly DateTime D1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime D2 = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The documents, and the members each one omits.</summary>
    private static readonly (Row Row, string[] Missing)[] Seed =
    {
        (new() { Code = "c1", A = 1, E = Status.Active, D = D1, Del = false, Name = "n1" }, Array.Empty<string>()),
        (new() { Code = "c2", A = 0, E = Status.New, D = D2, Del = true, Name = null }, Array.Empty<string>()),
        (new() { Code = "c3", A = 5, E = Status.Closed, D = D2, Del = true, Name = "n3" }, Array.Empty<string>()),
        (new() { Code = "c4", Name = "n4" }, new[] { "A", "E", "D", "Del" }),
        (new() { Code = "c5" }, new[] { "A", "E", "D", "Del", "Name" }),
        (new() { Code = "c6", A = 3, E = Status.New, D = D1, Del = false, Name = "n6" }, Array.Empty<string>()),
    };

    internal static (string Label, Expression<Func<RowView, bool>> Filter)[] Shapes()
    {
        var withZero = new[] { 0, 5 };
        var withoutZero = new[] { 1, 5 };
        var names = Enumerable.Range(0, 30_000).Select(i => "filler-name-" + i.ToString("D6")).ToList<string?>();
        names.Insert(10_000, "n3");
        names.Insert(25_000, null);
        var ints = Enumerable.Range(1_000_000, 100_000).ToList();
        ints.Insert(40_000, 5);
        ints.Add(0);
        return new (string, Expression<Func<RowView, bool>>)[]
        {
            ("Amount == 0", v => v.Amount == 0),
            ("Amount != 5", v => v.Amount != 5),
            ("Amount < 3", v => v.Amount < 3),
            ("Amount > 3", v => v.Amount > 3),
            ("!(Amount > 3)", v => !(v.Amount > 3)),
            ("!(Amount == 0)", v => !(v.Amount == 0)),
            ("withZero.Contains(Amount)", v => withZero.Contains(v.Amount)),
            ("!withoutZero.Contains(Amount)", v => !withoutZero.Contains(v.Amount)),
            ("E == New", v => v.E == Status.New),
            ("E != Active", v => v.E != Status.Active),
            ("D < D2", v => v.D < D2),
            ("!(D >= D1)", v => !(v.D >= D1)),
            ("Del == false", v => v.Del == false),
            ("Del != true", v => v.Del != true),
            ("!(Del == true)", v => !(v.Del == true)),
            ("Amount == 0 && Name != null", v => v.Amount == 0 && v.Name != null),
            ("names(30k + null).Contains(Name)", v => names.Contains(v.Name)),
            ("ints(100k + 0).Contains(Amount)", v => ints.Contains(v.Amount)),
            ("ints(100k + 0).Contains(Amount) && Code != \"c2\"", v => ints.Contains(v.Amount) && v.Code != "c2"),
        };
    }

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            Serializer = new CosmosGuidIdSerializer(),
        });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task899vf_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var (row, missing) in Seed)
        {
            var doc = (JsonObject)JsonSerializer.SerializeToNode(row)!;
            foreach (var name in missing) doc.Remove(name);
            doc["id"] = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(doc, new PartitionKey(doc["id"]!.GetValue<string>()));
            _oracle.Add(new RowView { Code = row.Code, Amount = row.A, E = row.E, D = row.D, Del = row.Del, Name = row.Name });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition View() => new ViewDefinitionBuilder<RowView>()
        .From<Row>()
        .Select<Row, string?>(r => r.Code, v => v.Code)
        .Select<Row, int>(r => r.A, v => v.Amount)
        .Select<Row, Status>(r => r.E, v => v.E)
        .Select<Row, DateTime>(r => r.D, v => v.D)
        .Select<Row, bool>(r => r.Del, v => v.Del)
        .Select<Row, string?>(r => r.Name, v => v.Name)
        .Build();

    [Fact]
    public async Task A_view_filter_reads_a_missing_non_nullable_member_as_its_default()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<RowView>(_rows, View());
        var failures = new List<string>();
        foreach (var (label, filter) in Shapes())
        {
            var expected = string.Join(",", _oracle.Where(filter.Compile()).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal));
            try
            {
                var read = string.Join(",", (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal));
                if (read != expected) failures.Add($"{label} [read]: C# {{{expected}}} got {{{read}}}");
                var count = await store.CountAsync(filter);
                if (count != expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Length)
                    failures.Add($"{label} [count]: C# {{{expected}}} got {count}");
            }
            catch (Exception e)
            {
                failures.Add($"{label}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    // ----- rendering, no server -----

    private static string Render(Expression<Func<RowView, bool>> filter)
        => CosmosFilterTranslator.Translate(filter, new List<KeyValuePair<string, object?>>(), name => name == "Amount" ? "A" : name);

    [Fact]
    public void A_test_the_default_passes_reads_a_missing_member_and_one_it_fails_keeps_its_rendering()
    {
        Render(v => v.Amount == 0).Should().Be("(NOT (IS_DEFINED(c[\"A\"])) OR c[\"A\"] = @p0)");
        Render(v => v.Amount > 3).Should().Be("c[\"A\"] > @p0");
        Render(v => !(v.Amount > 3)).Should().Be("NOT ((IS_DEFINED(c[\"A\"]) AND c[\"A\"] > @p0))");
    }
}
