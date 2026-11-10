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
/// TASK-899, live against the Cosmos emulator: a view's decimal SUM and a DateTime group key answer what LINQ answers over
/// the same rows, as the store aggregate does (TASK-897); a decimal AVG and a sort by a DateTime group key are refused by
/// name before any request.
/// </summary>
/// <remarks>
/// Measured before the fix (vnext-preview, 2026-11-10): the result was read by the container's serializer from Cosmos DB's
/// double, so a decimal SUM of 0.1 and 0.2 read 0.30000000000000004 and an AVG of 1, 0, 0 read 0.3333333333333333 where C#
/// has 28 digits; a DateTime group grouped the stored text, so a UTC, a local and an unspecified time with equal ticks
/// were three groups (C#: one), and a sort by it ordered the text (…10:00:00Z before …10:00:00.5Z descending). The SUM is
/// exact to 15 significant digits (the store's rule), so the values here stay within them.
/// </remarks>
public sealed class Task899CosmosViewAggregateTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;

    private static readonly DateTime Ten = new(2026, 1, 1, 10, 0, 0);

    private readonly List<Row> _source = new()
    {
        new() { Grp = "g1", M = 0.1m, MN = 0.1m, T = DateTime.SpecifyKind(Ten, DateTimeKind.Utc) },
        new() { Grp = "g1", M = 0.2m, MN = null, T = DateTime.SpecifyKind(Ten, DateTimeKind.Local) },
        new() { Grp = "g2", M = 1m, MN = 1m, T = DateTime.SpecifyKind(Ten, DateTimeKind.Unspecified) },
        new() { Grp = "g2", M = 0m, MN = 0m, T = DateTime.SpecifyKind(Ten.AddMilliseconds(500), DateTimeKind.Utc) },
        new() { Grp = "g2", M = 0m, MN = 0m, T = DateTime.SpecifyKind(Ten.AddHours(-1), DateTimeKind.Utc) },
        new() { Grp = "g3", M = 1234.5678m, MN = null, T = DateTime.SpecifyKind(Ten.AddHours(-1), DateTimeKind.Unspecified) },
        new() { Grp = "g3", M = 0.0001m, MN = null, T = DateTime.SpecifyKind(Ten.AddDays(1), DateTimeKind.Utc) },
        new() { Grp = "g4", M = 2m, MN = 2m, T = new DateTime(1500, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
    };

    public Task899CosmosViewAggregateTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string? id { get; set; }
        public string? Grp { get; set; }
        public decimal M { get; set; }
        public decimal? MN { get; set; }
        public DateTime T { get; set; }
    }

    public class Money
    {
        public string? Grp { get; set; }
        public decimal Sum { get; set; }
        public decimal Avg { get; set; }
        public decimal? SumN { get; set; }
        public decimal? AvgN { get; set; }
        public int Cnt { get; set; }
    }

    public class ByTime
    {
        public DateTime T { get; set; }
        public int Cnt { get; set; }
        public decimal Sum { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task899v_" + Guid.NewGuid().ToString("N"))).Database;
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

    private static ViewDefinition MoneyView() => new ViewDefinitionBuilder<Money>()
        .From<Row>()
        .GroupBy<Row, string?>(r => r.Grp)
        .Select<Row, string?>(r => r.Grp, v => v.Grp)
        .Sum<Row, decimal>(r => r.M, v => v.Sum)
        .Sum<Row, decimal?>(r => r.MN, v => v.SumN)
        .Count<Row>(v => v.Cnt)
        .Build();

    [Fact]
    public async Task A_grouped_view_answers_decimal_SUM_as_CSharp()
    {
        if (_rows == null) return;
        var expected = _source.GroupBy(r => r.Grp)
            .Select(g => new Money { Grp = g.Key, Sum = g.Sum(r => r.M), SumN = g.Sum(r => r.MN), Cnt = g.Count() })
            .OrderBy(m => m.Grp).ToList();

        var store = new CosmosViewStore<Money>(_rows, MoneyView());
        var actual = (await store.QueryAsync()).OrderBy(m => m.Grp).ToList();
        string Show(IEnumerable<Money> ms) => string.Join("; ", ms.Select(m => $"{m.Grp}: {m.Sum} {m.SumN} n={m.Cnt}"));
        _output.WriteLine("C#:     " + Show(expected));
        _output.WriteLine("Cosmos: " + Show(actual));
        actual.Select(m => (m.Grp, m.Sum, m.SumN, m.Cnt)).Should().Equal(expected.Select(m => (m.Grp, m.Sum, m.SumN, m.Cnt)));

        (await store.QueryFirstAsync(v => v.Grp == "g1"))!.Sum.Should().Be(0.3m);
        var paged = (await store.QueryAsync(null, OrderBy<Money>.By(m => m.Grp), 1, 0)).Single();
        paged.Sum.Should().Be(0.3m);
    }

    [Fact]
    public async Task An_ungrouped_view_answers_decimal_SUM_as_CSharp()
    {
        if (_rows == null) return;
        var view = new ViewDefinitionBuilder<Money>()
            .From<Row>()
            .Sum<Row, decimal>(r => r.M, v => v.Sum)
            .Sum<Row, decimal?>(r => r.MN, v => v.SumN)
            .Build();
        var actual = (await new CosmosViewStore<Money>(_rows, view).QueryAsync()).Single();
        _output.WriteLine($"Cosmos: {actual.Sum} {actual.SumN}");
        actual.Sum.Should().Be(_source.Sum(r => r.M));
        actual.SumN.Should().Be(_source.Sum(r => r.MN));
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

    private (Container Rows, CountingHandler Requests, CosmosClient Client) Counted()
    {
        var handler = new CountingHandler();
        var options = new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway };
        options.CustomHandlers.Add(handler);
        var client = new CosmosClient(Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION"), options);
        return (client.GetContainer(_db!.Id, _rows!.Id), handler, client);
    }

    /// <summary>
    /// A decimal AVG is refused by name before any request: Cosmos DB averages in double (1, 0, 0 read 0.3333333333333333,
    /// measured), and the exact decimal answer needs a SUM and a COUNT the view does not read.
    /// </summary>
    [Fact]
    public async Task A_decimal_AVG_is_refused_before_any_request()
    {
        if (_rows == null) return;
        var (rows, requests, client) = Counted();
        using var owned = client;
        foreach (var view in new[]
        {
            new ViewDefinitionBuilder<Money>().From<Row>().GroupBy<Row, string?>(r => r.Grp)
                .Select<Row, string?>(r => r.Grp, v => v.Grp).Avg<Row, decimal>(r => r.M, v => v.Avg).Build(),
            new ViewDefinitionBuilder<Money>().From<Row>().Avg<Row, decimal?>(r => r.MN, v => v.AvgN).Build(),
        })
        {
            var store = new CosmosViewStore<Money>(rows, view);
            (await FluentActions.Invoking(() => store.QueryAsync()).Should().ThrowAsync<NotSupportedException>())
                .WithMessage("Cosmos DB view Money refused: Avg of Row.M*, a Decimal*TASK-899*");
            await FluentActions.Invoking(() => store.QueryFirstAsync()).Should().ThrowAsync<NotSupportedException>();
            await FluentActions.Invoking(() => store.CountAsync()).Should().ThrowAsync<NotSupportedException>();
        }
        requests.Count.Should().Be(0, "each refusal is decided before any request");
    }

    private static ViewDefinition TimeView() => new ViewDefinitionBuilder<ByTime>()
        .From<Row>()
        .GroupBy<Row, DateTime>(r => r.T)
        .Select<Row, DateTime>(r => r.T, v => v.T)
        .Count<Row>(v => v.Cnt)
        .Sum<Row, decimal>(r => r.M, v => v.Sum)
        .Build();

    private List<ByTime> ExpectedTimes() => _source.GroupBy(r => r.T)
        .Select(g => new ByTime { T = g.Key, Cnt = g.Count(), Sum = g.Sum(r => r.M) })
        .OrderBy(t => t.T).ToList();

    private static IEnumerable<(long, int, decimal)> Shape(IEnumerable<ByTime> ts) => ts.Select(t => (t.T.Ticks, t.Cnt, t.Sum));

    [Fact]
    public async Task A_DateTime_group_groups_equal_ticks_as_CSharp()
    {
        if (_rows == null) return;
        var expected = ExpectedTimes();
        var store = new CosmosViewStore<ByTime>(_rows, TimeView());
        var actual = (await store.QueryAsync()).OrderBy(t => t.T).ToList();
        string Show(IEnumerable<ByTime> ts) => string.Join("; ", ts.Select(t => $"{t.T:o} n={t.Cnt} sum={t.Sum}"));
        _output.WriteLine("C#:     " + Show(expected));
        _output.WriteLine("Cosmos: " + Show(actual));
        Shape(actual).Should().Equal(Shape(expected));
        (await store.CountAsync()).Should().Be(expected.Count);
    }

    [Fact]
    public async Task A_DateTime_group_pages_and_filters_as_CSharp()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<ByTime>(_rows, TimeView());
        var all = (await store.QueryAsync()).ToList();
        var paged = new List<ByTime>();
        for (var offset = 0; offset < all.Count + 1; offset += 2)
        {
            paged.AddRange(await store.QueryAsync(null, null, 2, offset));
        }
        Shape(paged.OrderBy(t => t.T)).Should().Equal(Shape(ExpectedTimes()), "the pages cover every group once");

        var later = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var filtered = (await store.QueryAsync(v => v.T > later)).ToList();
        Shape(filtered).Should().Equal(Shape(ExpectedTimes().Where(t => t.T > later)));
    }

    /// <summary>
    /// A sort by a DateTime group key is refused by name before any request: the group is its wall clock, an expression,
    /// and Cosmos DB sorts only by a document path (measured: 2206 for the expression, SC2103 for the field).
    /// </summary>
    [Fact]
    public async Task A_sort_by_a_DateTime_group_key_is_refused_before_any_request()
    {
        if (_rows == null) return;
        var (rows, requests, client) = Counted();
        using var owned = client;
        var store = new CosmosViewStore<ByTime>(rows, TimeView());
        (await FluentActions.Invoking(() => store.QueryAsync(null, OrderBy<ByTime>.By(t => t.T), 2, 1))
                .Should().ThrowAsync<NotSupportedException>())
            .WithMessage("Cosmos DB cannot sort a grouped view by a DateTime group key: 'ByTime.T'*TASK-899*");
        await FluentActions.Invoking(() => store.QueryAsync(null, OrderBy<ByTime>.ByDescending(t => t.T)))
            .Should().ThrowAsync<NotSupportedException>();
        requests.Count.Should().Be(0);
    }
}
