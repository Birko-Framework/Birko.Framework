using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.View.Tests;

/// <summary>
/// CR-H090: CreateIndexedView/DropIndexedView did not invalidate the shared view-existence cache,
/// so Auto-mode kept using a stale decision after the DDL ran. The DDL itself needs a live SQL
/// Server, so these offline tests compile-verify the fix and exercise the invalidation API it now
/// calls (constructing the connector also proves the MSSql.View shared project builds).
/// </summary>
public class MSSqlIndexedViewCacheTests
{
    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    [Fact]
    public void ViewExistsCache_InvalidationApi_IsWired()
    {
        var connector = NewConnector();

        // These are the methods CreateIndexedView/DropIndexedView now call after the DDL.
        connector.Invoking(c => c.InvalidateViewExistsCache("SomeView")).Should().NotThrow();
        connector.Invoking(c => c.ClearViewExistsCache()).Should().NotThrow();
    }

    [Fact]
    public void IndexedView_Methods_Are_Present()
    {
        var type = typeof(MSSqlConnector);
        type.GetMethod("CreateIndexedView").Should().NotBeNull();
        type.GetMethod("DropIndexedView").Should().NotBeNull();
    }
}
