using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// SH-M311 (TASK-557) — the in-memory stores kept the caller's own reference and returned it from every read, so
/// <c>store.Create(e); e.Name = "x";</c> changed stored state with no <c>Update</c>. Every serializing backend takes a
/// copy at write time, so the canonical test double was false-green for the whole class of missing-<c>Update</c> bugs.
/// The stores now keep and hand out copies (<c>SnapshotEntities</c>, default on).
///
/// <para>Observed state: what a fresh read returns after the caller mutated its own objects.</para>
/// </summary>
public class InMemorySnapshotTests
{
    [Fact]
    public void Mutating_an_entity_after_Create_does_not_change_the_store()
    {
        var store = new InMemoryStore<TestModel>();
        var entity = new TestModel { Name = "stored" };
        store.Create(entity);

        entity.Name = "changed without Update";

        store.Read(entity.Guid!.Value)!.Name.Should().Be("stored");
    }

    [Fact]
    public void Mutating_a_read_result_does_not_change_the_store()
    {
        var store = new InMemoryStore<TestModel>();
        var entity = new TestModel { Name = "stored" };
        store.Create(entity);

        store.Read(entity.Guid!.Value)!.Name = "changed on a read copy";
        store.ReadFirst(x => x.Guid == entity.Guid)!.Name = "changed on a filtered read";
        foreach (var r in store.Read(null, null, null, null)) r.Name = "changed on a bulk read";

        store.Read(entity.Guid.Value)!.Name.Should().Be("stored");
    }

    [Fact]
    public void Only_Update_changes_the_store()
    {
        // Contract pin: the explicit path still writes.
        var store = new InMemoryStore<TestModel>();
        var entity = new TestModel { Name = "stored" };
        store.Create(entity);

        entity.Name = "updated";
        store.Update(entity);

        store.Read(entity.Guid!.Value)!.Name.Should().Be("updated");
    }

    [Fact]
    public async Task The_async_store_keeps_copies_too()
    {
        var store = new AsyncInMemoryStore<TestModel>();
        var entity = new TestModel { Name = "stored" };
        await store.CreateAsync(entity);

        entity.Name = "changed without Update";
        (await store.ReadAsync(entity.Guid!.Value))!.Name = "changed on a read copy";

        (await store.ReadAsync(entity.Guid.Value))!.Name.Should().Be("stored");
    }

    [Fact]
    public void The_opt_out_shares_references_as_before()
    {
        // Contract pin for SnapshotEntities = false: a model that cannot round-trip keeps the old behaviour.
        var store = new InMemoryStore<TestModel> { SnapshotEntities = false };
        var entity = new TestModel { Name = "stored" };
        store.Create(entity);

        store.Read(entity.Guid!.Value).Should().BeSameAs(entity);
    }
}
