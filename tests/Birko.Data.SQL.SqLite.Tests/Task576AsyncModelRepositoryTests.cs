using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-576: <see cref="AsyncDataBaseModelRepository{TConnector, T}"/> accepts a store built with a concrete connector,
/// and reaches it through a wrapper. It checked for <c>AsyncDataBaseBulkStore&lt;AbstractConnector, T&gt;</c>, which by
/// generic invariance no provider store is, and threw <c>ArgumentException</c> for every one.
/// </summary>
public class Task576AsyncModelRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t576-{Guid.NewGuid():N}");

    public Task576AsyncModelRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqlitePool.ClearForDirectory(_root, 2);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("T576Rows")]
    public class T576Row : AbstractLogModel
    {
        public string? Name { get; set; }
    }

    private AsyncDataBaseBulkStore<SqLiteConnector, T576Row> Store()
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, T576Row>();
        store.SetSettings(new SqLiteSettings(_root, "t576.db"));
        return store;
    }

    [Fact]
    public async Task A_store_with_a_concrete_connector_is_accepted_and_used()
    {
        var store = Store();
        var repository = new AsyncDataBaseModelRepository<SqLiteConnector, T576Row>(store);

        repository.DataBaseStore.Should().BeSameAs(store);
        repository.Connector.Should().BeSameAs(store.Connector).And.NotBeNull();

        var row = new T576Row { Name = "a" };
        await repository.CreateAsync(row);
        (await repository.ReadAsync(row.Guid!.Value))!.Name.Should().Be("a");
    }

    [Fact]
    public void A_wrapped_store_resolves_to_the_inner_store()
    {
        var store = Store();

        new AsyncDataBaseModelRepository<SqLiteConnector, T576Row>(
            new AsyncTimestampBulkStoreWrapper<IAsyncBulkStore<T576Row>, T576Row>(store, new SystemDateTimeProvider())).DataBaseStore.Should().BeSameAs(store);
    }

    [Fact]
    public void A_store_built_with_another_connector_type_is_refused()
    {
        var abstractStore = new AsyncDataBaseBulkStore<AbstractConnector, T576Row>();

        FluentActions.Invoking(() => new AsyncDataBaseModelRepository<SqLiteConnector, T576Row>(abstractStore))
            .Should().Throw<ArgumentException>().WithParameterName("store");
    }
}
