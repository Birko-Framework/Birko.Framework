using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-897, live against the Cosmos emulator: a grouped view's MIN/MAX of a DateTime answers the C# MIN/MAX over the
/// same rows, and a MIN/MAX of a string is Ordinal (TASK-907; it was refused by name before any request).
/// </summary>
/// <remarks>
/// Measured before the fix (vnext-preview, 2026-11-10): MIN/MAX compared the stored text, so <c>"…10:00:00.5Z"</c> was
/// the MIN of a group holding <c>…10:00:00Z</c> (and the MAX the other way round). The oracle is LINQ over the rows as written: <c>g.Min(r =&gt; r.T)</c>, by ticks, as C# compares
/// a DateTime whatever its kind.
/// </remarks>
public sealed class Task897CosmosViewAggregateTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;

    private static DateTime Utc(int hour, int minute, int second, int ms = 0)
        => new DateTime(2026, 1, 1, hour, minute, second, DateTimeKind.Utc).AddMilliseconds(ms);

    private readonly List<Row> _source = new()
    {
        new() { Grp = "g1", T = Utc(10, 0, 0), TN = Utc(10, 0, 0, 500), S = "a" },
        new() { Grp = "g1", T = Utc(10, 0, 0, 500), TN = Utc(10, 0, 0), S = "B" },
        new() { Grp = "g2", T = Utc(9, 59, 59).AddTicks(9_999_999), TN = null, S = "c" },
        new() { Grp = "g2", T = new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Local), TN = null, S = "d" },
        new() { Grp = "g2", T = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Local).AddMilliseconds(250), TN = null, S = null },
        new() { Grp = "g3", T = new DateTime(2026, 1, 1, 8, 0, 0, 250, DateTimeKind.Unspecified), TN = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified), S = "e" },
        new() { Grp = "g3", T = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Unspecified), TN = new DateTime(2026, 1, 1, 7, 59, 59, 999, DateTimeKind.Unspecified), S = "F" },
        new() { Grp = "g4", T = new DateTime(1500, 6, 1, 0, 0, 0, DateTimeKind.Utc), TN = new DateTime(1500, 6, 1, 0, 0, 0, 1, DateTimeKind.Utc), S = "g" },
        new() { Grp = "g4", T = new DateTime(1600, 6, 1, 0, 0, 0, DateTimeKind.Utc), TN = null, S = "h" },
    };

    public Task897CosmosViewAggregateTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string? id { get; set; }
        public string? Grp { get; set; }
        public DateTime T { get; set; }
        public DateTime? TN { get; set; }
        public string? S { get; set; }
    }

    public class Times
    {
        public string? Grp { get; set; }
        public DateTime MinT { get; set; }
        public DateTime MaxT { get; set; }
        public DateTime? MinTN { get; set; }
        public DateTime? MaxTN { get; set; }
    }

    public class Text
    {
        public string? Grp { get; set; }
        public string? X { get; set; }
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
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task897v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var row in _source)
        {
            row.id = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Fact]
    public async Task A_grouped_view_answers_DateTime_MIN_and_MAX_as_CSharp()
    {
        if (_rows == null) return;
        var view = new ViewDefinitionBuilder<Times>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.Grp)
            .Select<Row, string?>(r => r.Grp, v => v.Grp)
            .Min<Row, DateTime>(r => r.T, v => v.MinT)
            .Max<Row, DateTime>(r => r.T, v => v.MaxT)
            .Min<Row, DateTime?>(r => r.TN, v => v.MinTN)
            .Max<Row, DateTime?>(r => r.TN, v => v.MaxTN)
            .Build();

        var expected = _source.GroupBy(r => r.Grp).Select(g => new Times
        {
            Grp = g.Key,
            MinT = g.Min(r => r.T),
            MaxT = g.Max(r => r.T),
            MinTN = g.Min(r => r.TN),
            MaxTN = g.Max(r => r.TN),
        }).OrderBy(t => t.Grp).ToList();

        var actual = (await new CosmosViewStore<Times>(_rows, view).QueryAsync()).OrderBy(t => t.Grp).ToList();
        string Show(IEnumerable<Times> ts) => string.Join("; ", ts.Select(t => $"{t.Grp}: {t.MinT:o} {t.MaxT:o} {t.MinTN:o} {t.MaxTN:o}"));
        _output.WriteLine("C#:     " + Show(expected));
        _output.WriteLine("Cosmos: " + Show(actual));

        actual.Select(t => (t.Grp, t.MinT.Ticks, t.MaxT.Ticks, t.MinTN?.Ticks, t.MaxTN?.Ticks))
            .Should().Equal(expected.Select(t => (t.Grp, t.MinT.Ticks, t.MaxT.Ticks, t.MinTN?.Ticks, t.MaxTN?.Ticks)));
    }

    private sealed class CountingHandler : RequestHandler
    {
        private int _count;
        public int Count => System.Threading.Volatile.Read(ref _count);
        public override Task<ResponseMessage> SendAsync(RequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            System.Threading.Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// TASK-907 (inverted, rule 56): a MIN/MAX of a string is Ordinal, the store aggregate's default (the user's decision,
    /// 2026-11-10), which is Cosmos DB's code-point order for these values (<c>"B"</c> below <c>"a"</c>). It was refused by
    /// name before any request while C#'s order was the culture's. A SUM/AVG of one never reaches the store: the builder
    /// refuses a non-numeric SUM/AVG view property.
    /// </summary>
    [Fact]
    public async Task A_MIN_or_MAX_of_a_string_is_Ordinal()
    {
        if (_rows == null) return;

        ViewDefinition Of(AggregateFunction function) => new ViewDefinitionBuilder<Text>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.Grp)
            .Select<Row, string?>(r => r.Grp, v => v.Grp)
            .Aggregate<Row, string?>(function, r => r.S, v => v.X)
            .Build();

        FluentActions.Invoking(() => Of(AggregateFunction.Sum)).Should().Throw<InvalidOperationException>().WithMessage("*requires a numeric*");

        foreach (var function in new[] { AggregateFunction.Min, AggregateFunction.Max })
        {
            var got = (await new CosmosViewStore<Text>(_rows, Of(function)).QueryAsync())
                .OrderBy(v => v.Grp, StringComparer.Ordinal).Select(v => $"{v.Grp}={v.X ?? "null"}").ToList();
            var expected = _source.GroupBy(r => r.Grp).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                var values = g.Select(r => r.S).Where(s => s != null).ToList();
                var x = values.Count == 0 ? null
                    : function == AggregateFunction.Min ? values.Min(StringComparer.Ordinal) : values.Max(StringComparer.Ordinal);
                return $"{g.Key}={x ?? "null"}";
            }).ToList();
            got.Should().Equal(expected, function.ToString());
        }
    }
}
