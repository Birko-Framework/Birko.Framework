using Birko.Data.SQL.Caching;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Caching;

public class SqlCacheKeyBuilderTests
{
    // SH-H005: cache keys are now scoped by database identity (Settings.GetId()), so both key builders
    // take that scope as their first argument. These tests supply a fixed one — they are about the key's
    // SHAPE and its sensitivity to the other components, which is unchanged. The scope's own behaviour
    // (two databases no longer colliding, invalidation staying aligned with lookup) is covered in
    // Birko.Data.SQL.Caching.Tests/CrossDatabaseAndFilterKeyingTests.
    private const string Scope = "localhost:appdb";

    [Fact]
    public void BuildKey_SameInputs_ProducesSameKey()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "Status = 1", "Name ASC", 10, 0);
        var key2 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "Status = 1", "Name ASC", 10, 0);

        key1.Should().Be(key2);
    }

    [Fact]
    public void BuildKey_DifferentFilters_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "Status = 1", null, null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "Status = 2", null, null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentOrders_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, "Name ASC", null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, "Name DESC", null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentTables_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "Status = 1", null, null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey(Scope, "Products", "Status = 1", null, null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_NullFilter_UsesUnderscore()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, null, null, null);

        key.Should().Contain(":_:");
    }

    [Fact]
    public void BuildKey_EmptyFilter_UsesUnderscore()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "", null, null, null);

        key.Should().Contain(":_:");
    }

    [Fact]
    public void BuildKey_NullOrder_UsesUnderscore()
    {
        var keyNull = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "x = 1", null, null, null);
        var keyEmpty = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", "x = 1", "", null, null);

        keyNull.Should().Be(keyEmpty);
    }

    [Fact]
    public void BuildKey_WithLimitAndOffset_IncludesValues()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, null, 25, 50);

        key.Should().EndWith(":25:50");
    }

    [Fact]
    public void BuildKey_NullLimitAndOffset_UsesUnderscores()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, null, null, null);

        key.Should().EndWith(":_:_");
    }

    [Fact]
    public void BuildKey_StartsWithSqlPrefix()
    {
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Orders", null, null, null, null);

        // SH-H005: the scope segment sits between the prefix and the table, so the table is no longer
        // the second segment. Asserted via GetTablePrefix rather than a literal, so the two cannot drift.
        key.Should().StartWith("sql:").And.Contain(":Orders:");
        key.Should().StartWith(SqlCacheKeyBuilder.GetTablePrefix(Scope, "Orders"));
    }

    [Fact]
    public void GetTablePrefix_ReturnsCorrectFormat()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix(Scope, "Orders");

        // SH-H005: now sql:<scopeHash>:Orders: — the scope is hashed so a connection string cannot leak
        // into a cache key, and the table stays in clear so a key is recognisable by eye.
        prefix.Should().StartWith("sql:").And.EndWith(":Orders:");
        prefix.Split(':')[1].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void GetTablePrefix_KeyStartsWithPrefix()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix(Scope, "Products");
        var key = SqlCacheKeyBuilder.BuildKey(Scope, "Products", "x = 1", "Name ASC", 10, 0);

        key.Should().StartWith(prefix);
    }
}
