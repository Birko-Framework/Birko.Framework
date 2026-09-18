using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Tables;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// CR-M146: SqLiteConnector overrode only the sync ViewExists; the async path fell through to the
/// base probe (SELECT 1 FROM "name" WHERE 1=0), which returns true for a same-named TABLE and relies
/// on catch-all control flow. The new ViewExistsAsync override queries sqlite_master WHERE type='view'.
/// CR-M149: Auto-mode ShouldUsePersistentView cached negative results from a broad-catch probe,
/// poisoning Auto mode on a transient failure and missing views created after the first probe. It now
/// caches only definitive positives.
/// </summary>
public class SqLiteViewExistsAndCacheTests : IDisposable
{
    private readonly string _root;

    public SqLiteViewExistsAndCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqliteview-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private SqLiteConnector NewConnector(string db)
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = db });
        return (SqLiteConnector)factory.GetConnector();
    }

    // ── CR-M146 ──

    [Fact]
    public async Task ViewExistsAsync_TrueForView_FalseForTableOrAbsent()
    {
        var connector = NewConnector("m146.db");
        connector.DoCommandWithTransaction(c => c.CommandText = "CREATE TABLE T (Id INTEGER)", c => c.ExecuteNonQuery());
        connector.DoCommandWithTransaction(c => c.CommandText = "CREATE VIEW V AS SELECT Id FROM T", c => c.ExecuteNonQuery());

        (await connector.ViewExistsAsync("V")).Should().BeTrue("V is a real view");
        // The base probe would return true here (T is queryable); the override filters type='view'.
        (await connector.ViewExistsAsync("T")).Should().BeFalse("T is a table, not a view");
        (await connector.ViewExistsAsync("nope")).Should().BeFalse("no such object");
    }

    [Fact]
    public async Task ViewExistsAsync_EmptyName_Throws()
    {
        var connector = NewConnector("m146b.db");

        await connector.Invoking(c => c.ViewExistsAsync(""))
            .Should().ThrowAsync<ArgumentException>();
    }

    // ── CR-M149 ──

    private sealed class CacheProbeConnector : SqLiteConnector
    {
        public CacheProbeConnector(SqLiteSettings settings) : base(settings) { }
        public bool Probe(View view, Func<string, bool> checkViewExists) => ShouldUsePersistentView(view, checkViewExists);
    }

    private CacheProbeConnector NewProbe() => new(new SqLiteSettings(_root, "cache.db"));

    [Fact]
    public void ShouldUsePersistentView_AutoFalse_IsNotCached_AndReChecked()
    {
        var connector = NewProbe();
        var view = new View(null, null, "AutoView") { QueryMode = ViewQueryMode.Auto };
        int calls = 0;
        bool Check(string _) { calls++; return false; }

        connector.Probe(view, Check).Should().BeFalse();
        connector.Probe(view, Check).Should().BeFalse();

        // A false is never memoized — each call re-probes so a later create / transient recovery is seen.
        calls.Should().Be(2);
    }

    [Fact]
    public void ShouldUsePersistentView_AutoTrue_IsCached()
    {
        var connector = NewProbe();
        var view = new View(null, null, "AutoView") { QueryMode = ViewQueryMode.Auto };
        int calls = 0;
        bool Check(string _) { calls++; return true; }

        connector.Probe(view, Check).Should().BeTrue();
        connector.Probe(view, Check).Should().BeTrue();

        // A definitive positive is memoized — the second call does not re-probe.
        calls.Should().Be(1);
    }

    [Fact]
    public void ShouldUsePersistentView_OnTheFlyAndPersistent_DoNotProbe()
    {
        var connector = NewProbe();
        int calls = 0;
        bool Check(string _) { calls++; return true; }

        connector.Probe(new View(null, null, "V") { QueryMode = ViewQueryMode.OnTheFly }, Check).Should().BeFalse();
        connector.Probe(new View(null, null, "V") { QueryMode = ViewQueryMode.Persistent }, Check).Should().BeTrue();

        calls.Should().Be(0);
    }
}
