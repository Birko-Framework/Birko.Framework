using System;
using Birko.Data.Models;
using Birko.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-M133: the base repositories' null-store fallbacks and the bulk repository's type-mismatch guard
/// had no coverage. TASK-162 (SH-M219) changed the null-store contract: the graceful degradation these tests used to
/// pin (null / Guid.Empty / 0 / a silent no-op) made a mis-wired repository indistinguishable from an empty one, so
/// every data operation now throws. Two answers stay: CreateInstance activates a new instance, and Destroy with no store
/// has nothing to destroy.
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
    public void Read_with_null_store_throws()
    {
        var repo = new NullStoreRepository();

        repo.Invoking(r => r.Read(Guid.NewGuid())).Should().Throw<InvalidOperationException>().WithMessage("*no store*");
        repo.Invoking(r => r.Read(e => e.Guid == Guid.Empty)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_and_Save_with_null_store_throw()
    {
        var repo = new NullStoreRepository();

        repo.Invoking(r => r.Create(new Entity())).Should().Throw<InvalidOperationException>();
        repo.Invoking(r => r.Save(new Entity())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Count_with_null_store_throws()
    {
        new NullStoreRepository().Invoking(r => r.Count()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateInstance_with_null_store_activates_a_new_instance()
    {
        new NullStoreRepository().CreateInstance().Should().NotBeNull().And.BeOfType<Entity>();
    }

    [Fact]
    public void Update_and_Delete_with_null_store_throw_and_Destroy_has_nothing_to_destroy()
    {
        var repo = new NullStoreRepository();

        repo.Invoking(r => r.Update(new Entity())).Should().Throw<InvalidOperationException>();
        repo.Invoking(r => r.Delete(new Entity())).Should().Throw<InvalidOperationException>();
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
