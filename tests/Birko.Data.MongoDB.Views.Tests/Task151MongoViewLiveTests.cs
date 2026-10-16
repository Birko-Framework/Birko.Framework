using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-151, live MongoDB: a missing Persistent view is an error (SH-M392), DropAsync drops only views (SH-M393), a
/// Cross join is refused (SH-M394), and Ensure updates a changed view (SH-M406). Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task151MongoViewLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task151MongoViewLiveTests(ITestOutputHelper output) => _output = output;

    private string? Host()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
        return null;
    }

    public class T151Item : AbstractModel { public string? Name { get; set; } public int Qty { get; set; } }
    public class T151Other : AbstractModel { public Guid ItemId { get; set; } }
    public class ItemView { public string? Name { get; set; } public int Qty { get; set; } }

    private static ViewDefinition NamesOnly(ViewQueryMode mode = ViewQueryMode.Persistent) => new ViewDefinitionBuilder<ItemView>()
        .HasName("t151_items").HasQueryMode(mode)
        .From<T151Item>()
        .Select<T151Item, string?>(c => c.Name, v => v.Name)
        .Build();

    private static ViewDefinition WithQty() => new ViewDefinitionBuilder<ItemView>()
        .HasName("t151_items").HasQueryMode(ViewQueryMode.Persistent)
        .From<T151Item>()
        .Select<T151Item, string?>(c => c.Name, v => v.Name)
        .Select<T151Item, int>(c => c.Qty, v => v.Qty)
        .Build();

    private async Task Run(Func<MongoDBClient, Task> body)
    {
        var host = Host();
        if (host == null) return;
        var settings = new Settings(host, "birko_task151_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            var store = new AsyncMongoDBStore<T151Item>();
            store.SetSettings(settings);
            await store.CreateAsync(new T151Item { Name = "a", Qty = 2 });
            await store.CreateAsync(new T151Item { Name = "b", Qty = 3 });
            await body(client);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    [Fact]
    public Task A_persistent_view_that_was_never_created_is_an_error() => Run(async client =>
    {
        var views = new MongoViewStore<ItemView>(client, NamesOnly());
        await views.Invoking(v => v.QueryAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not exist*");
    });

    [Fact]
    public Task Drop_refuses_a_data_collection_and_drops_a_view() => Run(async client =>
    {
        var manager = new MongoViewManager(client);
        await manager.Invoking(m => m.DropAsync("T151Item")).Should().ThrowAsync<InvalidOperationException>();
        (await client.Database.GetCollection<BsonDocument>("T151Item").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)).Should().Be(2);

        await manager.EnsureAsync(NamesOnly());
        (await manager.ExistsAsync("t151_items")).Should().BeTrue();
        (await manager.ExistsAsync("T151Item")).Should().BeFalse("a data collection is not a view");
        await manager.DropAsync("t151_items");
        (await manager.ExistsAsync("t151_items")).Should().BeFalse();
    });

    [Fact]
    public Task Ensure_updates_a_changed_view() => Run(async client =>
    {
        var manager = new MongoViewManager(client);
        await manager.EnsureAsync(NamesOnly());
        await manager.EnsureAsync(WithQty());

        var rows = (await new MongoViewStore<ItemView>(client, WithQty()).QueryAsync()).ToList();
        rows.Select(r => r.Qty).Should().BeEquivalentTo(new[] { 2, 3 }, "the view now projects Qty");
    });

    [Fact]
    public void A_cross_join_is_refused()
    {
        var definition = new ViewDefinitionBuilder<ItemView>()
            .From<T151Item>()
            .Join<T151Item, T151Other, Guid?>(i => i.Guid, o => o.ItemId, JoinType.Cross)
            .Select<T151Item, string?>(c => c.Name, v => v.Name)
            .Build();

        FluentActions.Invoking(() => MongoViewTranslator.TranslatePipeline(definition)).Should().Throw<NotSupportedException>();
    }
}
