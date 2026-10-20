using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-151, on a real SQLite file: a single-source view (SH-M401), offset without limit (SH-M403), the count of a
/// grouped view (SH-M404), ordering by an aggregate (SH-M402), a filter on a portable view property (SH-M405), and
/// Ensure updating a changed view (SH-M406).
/// </summary>
public class Task151SqlViewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t151-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class T151Person : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class T151Order : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class AmountView
    {
        public decimal Value { get; set; }
    }

    public class TotalsView
    {
        public string PersonName { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Orders { get; set; }
    }

    public class NamesView
    {
        public string PersonName { get; set; } = string.Empty;
        public Guid? PersonId { get; set; }
    }

    private sealed class PersonMapping : IModelMapping<T151Person>
    {
        public void Configure(ModelMap<T151Person> map)
        {
            map.ToTable("T151Persons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name);
        }
    }

    private sealed class OrderMapping : IModelMapping<T151Order>
    {
        public void Configure(ModelMap<T151Order> map)
        {
            map.ToTable("T151Orders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    /// <summary>Three people with 1, 2 and 3 orders of 10, 20, 30.</summary>
    private async Task<SqLiteConnector> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var connector = (SqLiteConnector)new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "views.db" }).GetConnector();
        connector.CreateTable(new[] { typeof(T151Person), typeof(T151Order) });
        for (var i = 1; i <= 3; i++)
        {
            var person = new T151Person { Guid = Guid.NewGuid(), Name = "p" + i };
            await connector.InsertAsync(typeof(T151Person), person);
            for (var j = 1; j <= i; j++)
                await connector.InsertAsync(typeof(T151Order), new T151Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = 10 * j });
        }
        return connector;
    }

    private static ViewDefinition Totals(Birko.Data.Views.ViewQueryMode mode = Birko.Data.Views.ViewQueryMode.OnTheFly, string? name = null)
    {
        var builder = new ViewDefinitionBuilder<TotalsView>()
            .From<T151Order>()
            .Join<T151Order, T151Person, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<T151Person, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<T151Person, string>(p => p.Name!)
            .Sum<T151Order, decimal>(o => o.Amount, v => v.Total)
            .Count<T151Order>(v => v.Orders);
        if (name != null) builder = builder.HasName(name).HasQueryMode(mode);
        return builder.Build();
    }

    [Fact]
    public async Task A_single_source_view_can_be_queried()
    {
        var connector = await SeedAsync();
        var store = new SqlViewStore<AmountView>(connector, new ViewDefinitionBuilder<AmountView>()
            .From<T151Order>()
            .Select<T151Order, decimal>(o => o.Amount, v => v.Value)
            .Build());

        (await store.QueryAsync()).Should().HaveCount(6);
        (await store.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task An_offset_without_a_limit_skips()
    {
        var connector = await SeedAsync();
        var store = new SqlViewStore<TotalsView>(connector, Totals());

        var page = (await store.QueryAsync(orderBy: OrderBy<TotalsView>.By(v => v.PersonName), offset: 1)).ToList();

        page.Select(v => v.PersonName).Should().Equal("p2", "p3");
    }

    [Fact]
    public async Task A_grouped_view_counts_its_rows_and_orders_by_an_aggregate()
    {
        var connector = await SeedAsync();
        var store = new SqlViewStore<TotalsView>(connector, Totals());

        (await store.QueryAsync()).Should().HaveCount(3);
        (await store.CountAsync()).Should().Be(3, "three groups, not six joined rows");

        var byTotal = (await store.QueryAsync(orderBy: OrderBy<TotalsView>.ByDescending(v => v.Total))).ToList();
        byTotal.Select(v => v.Total).Should().Equal(60m, 30m, 10m);
    }

    [Fact]
    public async Task A_filter_on_a_portable_view_property_works()
    {
        var connector = await SeedAsync();
        var store = new SqlViewStore<TotalsView>(connector, Totals());

        var rows = (await store.QueryAsync(v => v.PersonName == "p2")).ToList();

        rows.Should().ContainSingle().Which.Total.Should().Be(30m);
    }

    [Fact]
    public async Task Ensure_updates_a_view_whose_definition_changed()
    {
        var connector = await SeedAsync();
        var manager = new SqlViewManager(connector);
        await manager.EnsureAsync(new ViewDefinitionBuilder<NamesView>()
            .HasName("T151Names").HasQueryMode(Birko.Data.Views.ViewQueryMode.Persistent)
            .From<T151Person>()
            .Select<T151Person, string>(p => p.Name!, v => v.PersonName)
            .Build());

        var changed = new ViewDefinitionBuilder<NamesView>()
            .HasName("T151Names").HasQueryMode(Birko.Data.Views.ViewQueryMode.Persistent)
            .From<T151Person>()
            .Select<T151Person, string>(p => p.Name!, v => v.PersonName)
            .Select<T151Person, Guid?>(p => p.Guid, v => v.PersonId)
            .Build();
        await manager.EnsureAsync(changed);

        var rows = (await new SqlViewStore<NamesView>(connector, changed).QueryAsync()).ToList();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(v => v.PersonId != null, "Ensure replaced the view with the new column");
    }
}
