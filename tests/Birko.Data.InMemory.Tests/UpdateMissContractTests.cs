using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using Birko.Tests.Shared.StoreContract;

namespace Birko.Data.InMemory.Tests;

/// <summary>TASK-555's cross-provider update-miss contract (<c>tests/Shared/StoreContract</c>) on the in-memory store.</summary>
public class InMemoryUpdateMissContractTests : StoreContract<TestModel>
{
    protected override IBulkStore<TestModel> NewStore() => new InMemoryStore<TestModel>();
    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
}

/// <summary>The async twin, on <see cref="AsyncInMemoryStore{T}"/>.</summary>
public class AsyncInMemoryUpdateMissContractTests : AsyncStoreContract<TestModel>
{
    protected override IAsyncBulkStore<TestModel> NewStore() => new AsyncInMemoryStore<TestModel>();
    protected override TestModel NewEntity(string name) => new() { Name = name };
    protected override string NameOf(TestModel entity) => entity.Name;
    protected override void Rename(TestModel entity, string name) => entity.Name = name;
}
