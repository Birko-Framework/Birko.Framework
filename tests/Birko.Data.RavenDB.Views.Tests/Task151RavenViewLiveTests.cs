using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// TASK-151, live RavenDB: an index reads the collection the store writes and projects the view (SH-M399), an
/// average is not integer division (SH-M397), a join loads the right document by its store id and honours Inner
/// (SH-M398), and the modes with no index say so instead of answering empty (SH-M395, SH-M396). Gated on
/// <c>BIRKO_RAVEN_URL</c>.
/// </summary>
public class Task151RavenViewLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public Task151RavenViewLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
        return false;
    }

    public class T151Order : AbstractModel { public string? Number { get; set; } public Guid? CustomerGuid { get; set; } public int Qty { get; set; } }
    public class T151Customer : AbstractModel { public string? Name { get; set; } }

    public class NumberView { public string? OrderNumber { get; set; } }
    public class AvgView { public double AvgQty { get; set; } }
    public class OrderCustomerView { public string? OrderNumber { get; set; } public string? CustomerName { get; set; } }

    private static readonly Guid Ann = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ghost = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private async Task Run(Func<IDocumentStore, Task> body)
    {
        if (!RequireServer()) return;
        var db = "birko-task151-" + Guid.NewGuid().ToString("N");
        using var orders = new AsyncRavenDBStore<T151Order>(Url!, db);
        using var customers = new AsyncRavenDBStore<T151Customer>(Url!, db);
        try
        {
            await customers.CreateAsync(new T151Customer { Guid = Ann, Name = "Ann" });
            await orders.CreateAsync(new T151Order { Number = "A", CustomerGuid = Ann, Qty = 10 });
            await orders.CreateAsync(new T151Order { Number = "B", CustomerGuid = Ghost, Qty = 15 });
            await body(orders.DocumentStore!);
        }
        finally
        {
            try { await orders.DestroyAsync(); } catch { }
        }
    }

    private static async Task Ensure(IDocumentStore store, ViewDefinition definition)
    {
        await new RavenViewManager(store).EnsureAsync(definition);
        for (var i = 0; i < 100; i++)
        {
            var staleness = await store.Maintenance.SendAsync(new GetIndexStatisticsOperation(definition.Name!));
            if (!staleness.IsStale) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("index stayed stale");
    }

    [Fact]
    public Task A_persistent_view_reads_the_stored_collection_and_projects_its_fields() => Run(async store =>
    {
        var definition = new ViewDefinitionBuilder<NumberView>()
            .HasName("t151_numbers").HasQueryMode(ViewQueryMode.Persistent)
            .From<T151Order>()
            .Select<T151Order, string?>(o => o.Number, v => v.OrderNumber)
            .Build();
        await Ensure(store, definition);

        var rows = (await new RavenViewStore<NumberView>(store, definition).QueryAsync()).ToList();

        rows.Select(r => r.OrderNumber).Should().BeEquivalentTo(new[] { "A", "B" });
        (await new RavenViewStore<NumberView>(store, definition).QueryAsync(v => v.OrderNumber == "B")).Should().ContainSingle();
    });

    [Fact]
    public Task An_average_of_an_integer_column_keeps_its_fraction() => Run(async store =>
    {
        var definition = new ViewDefinitionBuilder<AvgView>()
            .HasName("t151_avg").HasQueryMode(ViewQueryMode.Persistent)
            .From<T151Order>()
            .Aggregate<T151Order, double>(AggregateFunction.Avg, o => o.Qty, v => v.AvgQty)
            .Build();
        await Ensure(store, definition);

        var row = await new RavenViewStore<AvgView>(store, definition).QueryFirstAsync();

        row!.AvgQty.Should().Be(12.5);
    });

    private static ViewDefinition Joined(JoinType type, string name) => new ViewDefinitionBuilder<OrderCustomerView>()
        .HasName(name).HasQueryMode(ViewQueryMode.Persistent)
        .From<T151Order>()
        .Join<T151Order, T151Customer, Guid?>(o => o.CustomerGuid, c => c.Guid, type)
        .Select<T151Order, string?>(o => o.Number, v => v.OrderNumber)
        .Select<T151Customer, string?>(c => c.Name, v => v.CustomerName)
        .Build();

    [Fact]
    public Task An_inner_join_loads_the_customer_and_drops_the_unmatched_order() => Run(async store =>
    {
        var definition = Joined(JoinType.Inner, "t151_inner");
        await Ensure(store, definition);

        var rows = (await new RavenViewStore<OrderCustomerView>(store, definition).QueryAsync()).ToList();

        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new OrderCustomerView { OrderNumber = "A", CustomerName = "Ann" });
    });

    [Fact]
    public Task A_left_join_keeps_the_unmatched_order() => Run(async store =>
    {
        var definition = Joined(JoinType.LeftOuter, "t151_left");
        await Ensure(store, definition);

        var rows = (await new RavenViewStore<OrderCustomerView>(store, definition).QueryAsync()).OrderBy(r => r.OrderNumber).ToList();

        rows.Should().BeEquivalentTo(new[]
        {
            new OrderCustomerView { OrderNumber = "A", CustomerName = "Ann" },
            new OrderCustomerView { OrderNumber = "B", CustomerName = null },
        }, o => o.WithStrictOrdering());
    });

    [Fact]
    public Task Modes_with_no_index_refuse_instead_of_answering_empty() => Run(async store =>
    {
        var onTheFly = new ViewDefinitionBuilder<NumberView>()
            .HasName("t151_otf").HasQueryMode(ViewQueryMode.OnTheFly)
            .From<T151Order>()
            .Select<T151Order, string?>(o => o.Number, v => v.OrderNumber)
            .Build();
        await new RavenViewStore<NumberView>(store, onTheFly).Invoking(s => s.QueryAsync()).Should().ThrowAsync<NotSupportedException>();

        var auto = new ViewDefinitionBuilder<NumberView>()
            .HasName("t151_never_created").HasQueryMode(ViewQueryMode.Auto)
            .From<T151Order>()
            .Select<T151Order, string?>(o => o.Number, v => v.OrderNumber)
            .Build();
        await new RavenViewStore<NumberView>(store, auto).Invoking(s => s.QueryAsync()).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EnsureAsync*");
    });

    [Fact]
    public void A_join_on_a_key_other_than_the_document_identity_is_refused()
    {
        var definition = new ViewDefinitionBuilder<OrderCustomerView>()
            .From<T151Order>()
            .Join<T151Order, T151Customer, string?>(o => o.Number, c => c.Name)
            .Select<T151Order, string?>(o => o.Number, v => v.OrderNumber)
            .Build();

        FluentActions.Invoking(() => RavenViewTranslator.TranslateToMapReduce(definition)).Should().Throw<NotSupportedException>();
    }
}
