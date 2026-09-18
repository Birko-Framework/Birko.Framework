using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Raven.Client.Documents;
using Xunit;

namespace Birko.Data.RavenDB.Tests;

/// <summary>
/// CR-L164: the stores now implement IDisposable and dispose the underlying DocumentStore only when they
/// own it (connection-string ctor or Settings.CreateDocumentStore) — an externally-supplied store is left
/// alone. DocumentStore.Initialize() sets up conventions without connecting, so this is verifiable offline.
/// </summary>
public class RavenDBStoreDisposalTests
{
    private class TestModel : AbstractModel { }

    [Fact]
    public void Sync_store_from_connection_string_disposes_its_owned_document_store()
    {
        var store = new RavenDBStore<TestModel>("http://localhost:8080", "db");
        var owned = store.DocumentStore!;

        store.Dispose();

        owned.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public void Sync_store_does_not_dispose_an_externally_supplied_document_store()
    {
        using var external = new DocumentStore { Urls = new[] { "http://localhost:8080" }, Database = "db" };
        external.Initialize();
        var store = new RavenDBStore<TestModel>(external);

        store.Dispose();

        external.WasDisposed.Should().BeFalse("the store did not create it, so it must not dispose it");
    }

    [Fact]
    public void Sync_store_dispose_is_idempotent()
    {
        var store = new RavenDBStore<TestModel>("http://localhost:8080", "db");

        store.Invoking(s => { s.Dispose(); s.Dispose(); }).Should().NotThrow();
    }

    [Fact]
    public void Async_store_from_connection_string_disposes_its_owned_document_store()
    {
        var store = new AsyncRavenDBStore<TestModel>("http://localhost:8080", "db");
        var owned = store.DocumentStore!;

        store.Dispose();

        owned.WasDisposed.Should().BeTrue();
    }

    [Fact]
    public void Async_store_does_not_dispose_an_externally_supplied_document_store()
    {
        using var external = new DocumentStore { Urls = new[] { "http://localhost:8080" }, Database = "db" };
        external.Initialize();
        var store = new AsyncRavenDBStore<TestModel>(external);

        store.Dispose();

        external.WasDisposed.Should().BeFalse();
    }
}
