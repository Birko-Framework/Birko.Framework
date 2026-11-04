using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Telemetry.Tests;

/// <summary>
/// TASK-724. Measured before the fix: the instrumented bulk wrappers implemented neither aggregation interface, so an
/// aggregatable store stopped being one under instrumentation, and a Patterns decorator above it refused
/// (<c>StoreChain.AggregateThrough</c> found no aggregatable inner store). They forward it now, recorded as "Aggregate".
/// </summary>
public class Task724AggregationTests
{
    public class Row : Data.Models.AbstractModel { public string Name { get; set; } = ""; }

    private static AggregateQuery<Row> CountAll() => new() { Aggregates = [new AggregateField(AggregateFunction.Count, "Name", "n")] };

    [Fact]
    public async Task The_async_wrapper_aggregates_through()
    {
        var inner = new AsyncInMemoryStore<Row>();
        await inner.CreateAsync(new Row { Name = "a" });
        await inner.CreateAsync(new Row { Name = "b" });
        var wrapper = new AsyncInstrumentedBulkStoreWrapper<IAsyncBulkStore<Row>, Row>(inner);

        wrapper.Should().BeAssignableTo<IAsyncAggregatableStore<Row>>();
        var results = await StoreChain.AggregateThrough<Row>(wrapper, CountAll(), default);

        results.Should().ContainSingle();
    }

    [Fact]
    public void The_sync_wrapper_aggregates_through()
    {
        var inner = new InMemoryStore<Row>();
        inner.Create(new Row { Name = "a" });
        var wrapper = new InstrumentedBulkStoreWrapper<IBulkStore<Row>, Row>(inner);

        wrapper.Should().BeAssignableTo<IAggregatableStore<Row>>();
        ((IAggregatableStore<Row>)wrapper).Aggregate(CountAll()).Should().ContainSingle();
    }
}
