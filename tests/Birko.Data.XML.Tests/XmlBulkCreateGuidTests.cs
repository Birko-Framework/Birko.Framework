using Birko.Configuration;
using Birko.Data.Stores;
using Birko.Data.XML.Stores;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// CR-L244: the sync bulk CreateCore (AbstractXmlStore) assigned item.Guid = Guid.NewGuid()
/// unconditionally (discarding a caller-supplied Guid) and used _items.Add, which throws
/// ArgumentException on a duplicate key — inconsistent with the async bulk CreateCoreAsync, which
/// preserves an existing Guid and upserts via the indexer. These pin the aligned behavior: caller
/// Guids are preserved, and re-creating an existing Guid upserts instead of throwing.
/// </summary>
public class XmlBulkCreateGuidTests : IDisposable
{
    private readonly string _location;
    private readonly string _dir;

    public XmlBulkCreateGuidTests()
    {
        _location = "birko-xml-l244-" + Guid.NewGuid().ToString("N");
        _dir = Path.GetFullPath(_location);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void BulkCreate_PreservesCallerSuppliedGuid()
    {
        var store = new XmlStore<TestModel>();
        store.SetSettings(new Settings(_location, "l244-preserve"));

        var known = Guid.NewGuid();
        ((IBulkStore<TestModel>)store).Create(new List<TestModel> { new() { Guid = known, Name = "keep" } });

        // The caller Guid must survive (the old sync bulk path overwrote it with a fresh one).
        var reloaded = new XmlStore<TestModel>();
        reloaded.SetSettings(new Settings(_location, "l244-preserve"));
        reloaded.Read(known).Should().NotBeNull();
        reloaded.Read(known)!.Name.Should().Be("keep");
    }

    [Fact]
    public void BulkCreate_DuplicateGuid_UpsertsInsteadOfThrowing()
    {
        var store = new XmlStore<TestModel>();
        store.SetSettings(new Settings(_location, "l244-upsert"));

        var g = Guid.NewGuid();
        var bulk = (IBulkStore<TestModel>)store;
        bulk.Create(new List<TestModel> { new() { Guid = g, Name = "first" } });

        // Re-creating the same Guid must NOT throw (indexer upsert), unlike the old _items.Add.
        Action act = () => bulk.Create(new List<TestModel> { new() { Guid = g, Name = "second" } });
        act.Should().NotThrow();

        var items = bulk.Read(_ => true).ToList();
        items.Should().ContainSingle();
        items[0].Name.Should().Be("second");
    }
}
