using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-151, live against the Cosmos emulator: a plain view projects its renamed fields and filters on them
/// (SH-M410), a grouping view counts and reads its groups on every path (SH-M378, SH-M379), the manager neither
/// pretends to create a Persistent view nor deletes a data container (SH-M407, SH-M408), and a join is refused
/// (SH-M411). Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task151CosmosViewLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;

    public Task151CosmosViewLiveTests(ITestOutputHelper output) => _output = output;

    public class Row { public string? Region { get; set; } public string? Code { get; set; } public int Amount { get; set; } }
    public class Other { public string? Code { get; set; } }
    public class StatusView { public string? Status { get; set; } public string? Region { get; set; } }
    public class RegionView { public string? Region { get; set; } }
    public class RegionTotalView { public string? Region { get; set; } public int Total { get; set; } }

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task151_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var (region, code, amount) in new[] { ("EU", "a", 1), ("EU", "b", 2), ("US", "c", 4) })
        {
            var id = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(new { id, Region = region, Code = code, Amount = amount }, new PartitionKey(id));
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Statuses() => new ViewDefinitionBuilder<StatusView>()
        .From<Row>()
        .Select<Row, string?>(r => r.Code, v => v.Status)
        .Select<Row, string?>(r => r.Region, v => v.Region)
        .Build();

    [Fact]
    public async Task A_plain_view_projects_its_renamed_fields_and_filters_on_them()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<StatusView>(_rows, Statuses());

        (await store.QueryAsync()).Select(v => v.Status).Should().BeEquivalentTo(new[] { "a", "b", "c" });
        (await store.QueryAsync(v => v.Status == "b")).Should().ContainSingle().Which.Region.Should().Be("EU");
        (await store.QueryFirstAsync(v => v.Status == "c"))!.Region.Should().Be("US");
        (await store.CountAsync(v => v.Region == "EU")).Should().Be(2);
    }

    [Fact]
    public async Task A_grouping_view_reads_and_counts_its_groups()
    {
        if (_rows == null) return;
        var distinct = new ViewDefinitionBuilder<RegionView>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.Region)
            .Select<Row, string?>(r => r.Region, v => v.Region)
            .Build();
        var store = new CosmosViewStore<RegionView>(_rows, distinct);

        (await store.QueryAsync()).Select(v => v.Region).Should().BeEquivalentTo(new[] { "EU", "US" });
        (await store.CountAsync()).Should().Be(2, "two groups, not three documents");

        var totals = new ViewDefinitionBuilder<RegionTotalView>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.Region)
            .Select<Row, string?>(r => r.Region, v => v.Region)
            .Sum<Row, int>(r => r.Amount, v => v.Total)
            .Build();
        var totalStore = new CosmosViewStore<RegionTotalView>(_rows, totals);
        (await totalStore.CountAsync()).Should().Be(2);
        (await totalStore.CountAsync(v => v.Region == "EU")).Should().Be(1);
        (await totalStore.QueryAsync()).Should().BeEquivalentTo(new[]
        {
            new RegionTotalView { Region = "EU", Total = 3 },
            new RegionTotalView { Region = "US", Total = 4 },
        });
    }

    [Fact]
    public async Task The_manager_neither_fakes_a_persistent_view_nor_deletes_a_data_container()
    {
        if (_rows == null) return;
        var manager = new CosmosViewManager(_db!);
        var persistent = new ViewDefinitionBuilder<StatusView>()
            .HasName("Rows").HasQueryMode(ViewQueryMode.Persistent)
            .From<Row>()
            .Select<Row, string?>(r => r.Code, v => v.Status)
            .Build();

        await manager.Invoking(m => m.EnsureAsync(persistent)).Should().ThrowAsync<NotSupportedException>();
        await new CosmosViewStore<StatusView>(_rows, persistent).Invoking(s => s.QueryAsync()).Should().ThrowAsync<NotSupportedException>();

        (await manager.ExistsAsync("Rows")).Should().BeFalse("a data container is not a view");
        await manager.DropAsync("Rows");
        (await _rows.ReadContainerAsync()).StatusCode.Should().Be(System.Net.HttpStatusCode.OK, "the data container survives");
    }

    [Fact]
    public void A_join_is_refused()
    {
        var joined = new ViewDefinitionBuilder<StatusView>()
            .From<Row>()
            .Join<Row, Other, string?>(r => r.Code, o => o.Code)
            .Select<Row, string?>(r => r.Code, v => v.Status)
            .Build();

        FluentActions.Invoking(() => new CosmosViewStore<StatusView>(new Mock().Container, joined)).Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task A_filter_on_an_aggregate_is_refused_not_tested_against_a_document_field()
    {
        var totals = new ViewDefinitionBuilder<RegionTotalView>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.Region)
            .Select<Row, string?>(r => r.Region, v => v.Region)
            .Sum<Row, int>(r => r.Amount, v => v.Total)
            .Build();

        await new CosmosViewStore<RegionTotalView>(new Mock().Container, totals)
            .Invoking(s => s.QueryAsync(v => v.Total > 2)).Should().ThrowAsync<NotSupportedException>().WithMessage("*HAVING*");
    }

    private sealed class Mock
    {
        public Container Container { get; } = new CosmosClient("AccountEndpoint=http://localhost:1/;AccountKey=" + Convert.ToBase64String(new byte[64])).GetContainer("d", "c");
    }
}
