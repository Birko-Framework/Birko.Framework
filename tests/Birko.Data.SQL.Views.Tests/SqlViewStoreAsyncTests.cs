using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// CR-H096: SqlViewStore's async methods called synchronous connector methods (blocking the calling
/// thread) and never threaded the CancellationToken into DB work. They now use the genuine async
/// connector path (SelectAsync/SelectCountAsync over Tables.View) when available. These SQLite-backed
/// tests exercise that async path end-to-end over a join view.
/// </summary>
public class SqlViewStoreAsyncTests : IDisposable
{
    private readonly string _root;

    public SqlViewStoreAsyncTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlviews-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Person : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class Order : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class OrderView
    {
        public Guid? OrderId { get; set; }
        public string PersonName { get; set; } = string.Empty;
    }

    private sealed class PersonMapping : IModelMapping<Person>
    {
        public void Configure(ModelMap<Person> map)
        {
            map.ToTable("Persons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class OrderMapping : IModelMapping<Order>
    {
        public void Configure(ModelMap<Order> map)
        {
            map.ToTable("Orders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    private async Task<SqLiteConnector> SeedAsync(int people, int ordersEach)
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "views.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Person), typeof(Order) });

        for (int i = 0; i < people; i++)
        {
            var person = new Person { Guid = Guid.NewGuid(), Name = "p" + i };
            await connector.InsertAsync(typeof(Person), person);
            for (int j = 0; j < ordersEach; j++)
            {
                await connector.InsertAsync(typeof(Order),
                    new Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Amount = 10 * (j + 1) });
            }
        }
        return connector;
    }

    private static ViewDefinition OrderDefinition()
        => new ViewDefinitionBuilder<OrderView>()
            .From<Order>()
            .Join<Order, Person, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<Order, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<Person, string>(p => p.Name!, v => v.PersonName)
            .Build();

    [Fact]
    public async Task QueryAsync_ReturnsJoinedRows_ViaAsyncPath()
    {
        var connector = await SeedAsync(people: 2, ordersEach: 3);
        var store = new SqlViewStore<OrderView>(connector, OrderDefinition());

        var all = (await store.QueryAsync()).ToList();

        all.Should().HaveCount(6);
        all.Should().OnlyContain(v => !string.IsNullOrEmpty(v.PersonName));
    }

    [Fact]
    public async Task CountAsync_ReturnsRowCount_ViaAsyncPath()
    {
        var connector = await SeedAsync(people: 2, ordersEach: 2);
        var store = new SqlViewStore<OrderView>(connector, OrderDefinition());

        (await store.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task QueryFirstAsync_ReturnsOne_ViaAsyncPath()
    {
        var connector = await SeedAsync(people: 1, ordersEach: 2);
        var store = new SqlViewStore<OrderView>(connector, OrderDefinition());

        (await store.QueryFirstAsync()).Should().NotBeNull();
    }
}
