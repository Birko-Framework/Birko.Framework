using Birko.Configuration;
using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// CR-M098: the sync bulk CreateCore unconditionally did item.Guid = Guid.NewGuid() (discarding a
/// caller-assigned Guid) and used _items.Add (which throws on a duplicate key), diverging from the
/// single-item and async-bulk paths. It now uses Guid ??= and the upsert indexer.
/// </summary>
public class JsonBulkCreateTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public JsonBulkCreateTests()
    {
        _location = "birko-json-bulk-tests-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private Birko.Data.JSON.Stores.JsonStore<TestModel> NewStore(string file)
    {
        var store = new Birko.Data.JSON.Stores.JsonStore<TestModel>();
        store.SetSettings(new Settings(_location, file));
        return store;
    }

    [Fact]
    public void BulkCreate_preserves_caller_assigned_guids()
    {
        var store = NewStore("preserve.json");
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        store.Create(new[]
        {
            new TestModel { Guid = g1, Name = "a" },
            new TestModel { Guid = g2, Name = "b" },
        });

        var byId = store.Read().ToDictionary(x => x.Guid!.Value, x => x.Name);
        byId.Keys.Should().BeEquivalentTo(new[] { g1, g2 }, "caller-supplied Guids must be kept");
        byId[g1].Should().Be("a");
        byId[g2].Should().Be("b");
    }

    [Fact]
    public void BulkCreate_assigns_a_guid_when_none_supplied()
    {
        var store = NewStore("assign.json");

        store.Create(new[] { new TestModel { Name = "a" } });

        store.Read().Should().ContainSingle().Which.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
    }

    [Fact]
    public void BulkCreate_with_a_duplicate_guid_upserts_instead_of_throwing()
    {
        var store = NewStore("upsert.json");
        var g = Guid.NewGuid();

        store.Create(new[] { new TestModel { Guid = g, Name = "first" } });

        // Re-creating the same Guid used to throw ArgumentException (_items.Add on a dup key).
        Action act = () => store.Create(new[] { new TestModel { Guid = g, Name = "second" } });

        act.Should().NotThrow();
        var items = store.Read().ToList();
        items.Should().ContainSingle();
        items[0].Name.Should().Be("second", "the upsert replaces the existing entry");
    }
}
