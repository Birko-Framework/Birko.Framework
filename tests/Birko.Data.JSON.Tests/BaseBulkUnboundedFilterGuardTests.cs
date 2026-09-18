using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// TASK-215 — the base-class half, asserted from a backend that overrides <b>none</b> of the filter-based
/// destructive methods.
///
/// <para><b>Why this file is in the JSON suite and not with the guard.</b> The defect was filed against
/// InMemory and ElasticSearch, but the measurement found the hole one layer up: <c>AbstractBulkStore</c>'s
/// own <c>Delete(filter)</c> / <c>Update(filter, …)</c> never called <c>RequireBoundedFilter</c> either.
/// <c>JsonStore</c> overrides none of them, so it exercises the base directly — measured before the fix,
/// <c>Delete(x =&gt; !empty.Contains(x.Value))</c> left <b>0 of 3</b> rows with no exception. Every portable
/// backend that overrides nothing (JSON, XML, RavenDB, CosmosDB, InfluxDB) inherited the same hole, so
/// pinning it from outside <c>Birko.Data.Stores</c> is the point: a test living beside the guard could pass
/// while the inherited path stayed broken.</para>
///
/// <para>Third instance of § Conventions' rule that a scope guard tests what a statement MEANS — after
/// SQL's <c>1 = 1</c> (TASK-137) and MongoDB's <c>$nin: []</c> (TASK-212). Here nothing is translated at
/// all: the predicate is compiled and run as a delegate, so <c>!empty.Contains(x)</c> is true of every
/// entity by C# semantics and no downstream layer could ever have noticed.</para>
/// </summary>
public class BaseBulkUnboundedFilterGuardTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;
    private static readonly List<int> Empty = new();
    private static readonly List<int> Some = new() { 10 };

    public BaseBulkUnboundedFilterGuardTests()
    {
        _location = "birko-json-unbounded-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private Birko.Data.JSON.Stores.JsonStore<TestModel> Seeded(string file)
    {
        var store = new Birko.Data.JSON.Stores.JsonStore<TestModel>();
        store.SetSettings(new Settings(_location, file));
        for (var i = 1; i <= 3; i++)
        {
            store.Create(new TestModel { Guid = Guid.NewGuid(), Name = $"r{i}", Value = i * 10 });
        }
        return store;
    }

    [Fact]
    public void UnboundedFilter_Delete_IsRefused_AndKeepsEveryRow()
    {
        var store = Seeded("refused-delete.json");

        var act = () => store.Delete(x => !Empty.Contains(x.Value));

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Should().HaveCount(3, "measured before the fix: 0 of 3 left, no exception");
    }

    [Fact]
    public void UnboundedFilter_Update_IsRefused_AndChangesNothing()
    {
        var store = Seeded("refused-update.json");

        var act = () => store.Update(
            x => !Empty.Contains(x.Value), new PropertyUpdate<TestModel>().Set(r => r.Name, "clobbered"));

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public void TheExplicitDoors_StillEmptyTheStore()
    {
        // § SH-H037: the opt-out is part of the fix and gets its own executed test, not a mention.
        var viaPredicate = Seeded("door-predicate.json");
        viaPredicate.Delete(x => true);
        viaPredicate.Read().Should().BeEmpty();

        var viaMethod = Seeded("door-method.json");
        viaMethod.DeleteAll();
        viaMethod.Read().Should().BeEmpty();
    }

    [Fact]
    public void ABoundedFilter_StillDeletesExactlyItsRows()
    {
        var store = Seeded("bounded.json");

        store.Delete(x => x.Value > 10);

        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1" });
    }

    [Fact]
    public void ANonEmptyNegatedContains_IsNotRefused()
    {
        var store = Seeded("non-empty-nin.json");

        store.Delete(x => !Some.Contains(x.Value));

        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1" });
    }
}
