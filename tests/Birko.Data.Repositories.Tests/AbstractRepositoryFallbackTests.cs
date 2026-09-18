using System;
using Birko.Data.Models;
using Birko.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-M133: the base repositories' null-store fallbacks and the bulk repository's type-mismatch guard
/// had no coverage. A null-store AbstractRepository degrades gracefully (null / Guid.Empty / 0 /
/// Activator-new / no-op), while AbstractBulkRepository throws InvalidOperationException when its Store
/// is not an IBulkStore.
/// </summary>
public class AbstractRepositoryFallbackTests
{
    private class Entity : AbstractModel { }

    private sealed class NullStoreRepository : AbstractRepository<Entity>
    {
        public NullStoreRepository() : base(null) { }
    }

    private sealed class NullStoreBulkRepository : AbstractBulkRepository<Entity>
    {
        public NullStoreBulkRepository() : base(null) { }
    }

    [Fact]
    public void Read_with_null_store_returns_null()
    {
        var repo = new NullStoreRepository();

        repo.Read(Guid.NewGuid()).Should().BeNull();
        repo.Read(e => e.Guid == Guid.Empty).Should().BeNull();
    }

    [Fact]
    public void Create_and_Save_with_null_store_return_empty_guid()
    {
        var repo = new NullStoreRepository();

        repo.Create(new Entity()).Should().Be(Guid.Empty);
        repo.Save(new Entity()).Should().Be(Guid.Empty);
    }

    [Fact]
    public void Count_with_null_store_returns_zero()
    {
        new NullStoreRepository().Count().Should().Be(0);
    }

    [Fact]
    public void CreateInstance_with_null_store_activates_a_new_instance()
    {
        new NullStoreRepository().CreateInstance().Should().NotBeNull().And.BeOfType<Entity>();
    }

    [Fact]
    public void Update_Delete_Destroy_with_null_store_do_not_throw()
    {
        var repo = new NullStoreRepository();

        repo.Invoking(r => r.Update(new Entity())).Should().NotThrow();
        repo.Invoking(r => r.Delete(new Entity())).Should().NotThrow();
        repo.Invoking(r => r.Destroy()).Should().NotThrow();
    }

    [Fact]
    public void Bulk_repository_without_a_bulk_store_throws_on_read()
    {
        var repo = new NullStoreBulkRepository();

        repo.Invoking(r => r.Read()).Should().Throw<InvalidOperationException>()
            .WithMessage("*IBulkStore*");
    }

    [Fact]
    public void Bulk_repository_without_a_bulk_store_throws_on_create()
    {
        var repo = new NullStoreBulkRepository();

        repo.Invoking(r => r.Create(new[] { new Entity() })).Should().Throw<InvalidOperationException>()
            .WithMessage("*IBulkStore*");
    }
}
