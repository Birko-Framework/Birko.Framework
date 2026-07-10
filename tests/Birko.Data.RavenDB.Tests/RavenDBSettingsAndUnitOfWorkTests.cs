using System;
using System.Threading.Tasks;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;

namespace Birko.Data.RavenDB.Tests;

/// <summary>
/// CR-M131: offline coverage for the pure/near-pure surfaces — Settings (GetId, LoadFrom round-trip +
/// type-guard, CreateDocumentStore config) and the RavenDbUnitOfWork state machine. Store CRUD, facet
/// aggregation execution, and Commit (SaveChanges) need an embedded/live RavenDB (integration-tier).
/// </summary>
public class RavenDBSettingsAndUnitOfWorkTests
{
    [Fact]
    public void GetId_composes_url_and_database()
    {
        new Settings("http://localhost:8080", "shop").GetId().Should().Be("http://localhost:8080:shop");
    }

    [Fact]
    public void LoadFrom_settings_copies_base_and_request_timeout()
    {
        var target = new Settings();
        var source = new Settings("http://srv:8080", "db", "user", "pw") { RequestTimeout = TimeSpan.FromSeconds(90) };

        target.LoadFrom(source);

        target.Location.Should().Be("http://srv:8080");
        target.Name.Should().Be("db");
        target.UserName.Should().Be("user");
        target.RequestTimeout.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void LoadFrom_foreign_settings_falls_back_to_base_without_throwing()
    {
        var target = new Settings { RequestTimeout = TimeSpan.FromSeconds(45) };

        target.LoadFrom(new Birko.Configuration.Settings("loc", "name")); // not a RavenDB Settings

        target.Location.Should().Be("loc");
        target.Name.Should().Be("name");
        target.RequestTimeout.Should().Be(TimeSpan.FromSeconds(45), "the RavenDB-specific field is untouched by a base load");
    }

    [Fact]
    public void CreateDocumentStore_maps_url_database_and_timeout()
    {
        var settings = new Settings("http://localhost:8080", "mydb") { RequestTimeout = TimeSpan.FromSeconds(15) };

        using var store = (DocumentStore)settings.CreateDocumentStore();

        store.Urls.Should().Contain("http://localhost:8080");
        store.Database.Should().Be("mydb");
        store.Conventions.RequestTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    private static (DocumentStore store, RavenDbUnitOfWork uow) NewUoW()
    {
        var store = new DocumentStore { Urls = new[] { "http://localhost:59999" }, Database = "test" };
        store.Initialize(); // lazy — no network until a query
        return (store, new RavenDbUnitOfWork(store));
    }

    [Fact]
    public void Ctor_null_store_throws()
    {
        Action act = () => new RavenDbUnitOfWork(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Begin_activates_and_double_begin_throws()
    {
        var (store, uow) = NewUoW();
        using (store)
        {
            await uow.BeginAsync();
            uow.IsActive.Should().BeTrue();

            Func<Task> act = () => uow.BeginAsync();
            await act.Should().ThrowAsync<TransactionAlreadyActiveException>();

            await uow.RollbackAsync();
            uow.IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Commit_or_Rollback_without_active_transaction_throws()
    {
        var (store, uow) = NewUoW();
        using (store)
        {
            await uow.Invoking(u => u.CommitAsync()).Should().ThrowAsync<NoActiveTransactionException>();
            await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();
        }
    }

    [Fact]
    public async Task Begin_after_Dispose_throws_ObjectDisposed()
    {
        var (store, uow) = NewUoW();
        using (store)
        {
            uow.Dispose();
            await uow.Invoking(u => u.BeginAsync()).Should().ThrowAsync<ObjectDisposedException>();
        }
    }
}
