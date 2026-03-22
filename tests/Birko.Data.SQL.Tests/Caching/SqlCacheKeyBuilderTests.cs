using Birko.Data.SQL.Caching;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Caching;

public class SqlCacheKeyBuilderTests
{
    [Fact]
    public void BuildKey_SameInputs_ProducesSameKey()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey("Orders", "Status = 1", "Name ASC", 10, 0);
        var key2 = SqlCacheKeyBuilder.BuildKey("Orders", "Status = 1", "Name ASC", 10, 0);

        key1.Should().Be(key2);
    }

    [Fact]
    public void BuildKey_DifferentFilters_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey("Orders", "Status = 1", null, null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey("Orders", "Status = 2", null, null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentOrders_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey("Orders", null, "Name ASC", null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey("Orders", null, "Name DESC", null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_DifferentTables_ProduceDifferentKeys()
    {
        var key1 = SqlCacheKeyBuilder.BuildKey("Orders", "Status = 1", null, null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey("Products", "Status = 1", null, null, null);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void BuildKey_NullFilter_UsesUnderscore()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Orders", null, null, null, null);

        key.Should().Contain(":_:");
    }

    [Fact]
    public void BuildKey_EmptyFilter_UsesUnderscore()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Orders", "", null, null, null);

        key.Should().Contain(":_:");
    }

    [Fact]
    public void BuildKey_NullOrder_UsesUnderscore()
    {
        var keyNull = SqlCacheKeyBuilder.BuildKey("Orders", "x = 1", null, null, null);
        var keyEmpty = SqlCacheKeyBuilder.BuildKey("Orders", "x = 1", "", null, null);

        keyNull.Should().Be(keyEmpty);
    }

    [Fact]
    public void BuildKey_WithLimitAndOffset_IncludesValues()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Orders", null, null, 25, 50);

        key.Should().EndWith(":25:50");
    }

    [Fact]
    public void BuildKey_NullLimitAndOffset_UsesUnderscores()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Orders", null, null, null, null);

        key.Should().EndWith(":_:_");
    }

    [Fact]
    public void BuildKey_StartsWithSqlPrefix()
    {
        var key = SqlCacheKeyBuilder.BuildKey("Orders", null, null, null, null);

        key.Should().StartWith("sql:Orders:");
    }

    [Fact]
    public void GetTablePrefix_ReturnsCorrectFormat()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix("Orders");

        prefix.Should().Be("sql:Orders:");
    }

    [Fact]
    public void GetTablePrefix_KeyStartsWithPrefix()
    {
        var prefix = SqlCacheKeyBuilder.GetTablePrefix("Products");
        var key = SqlCacheKeyBuilder.BuildKey("Products", "x = 1", "Name ASC", 10, 0);

        key.Should().StartWith(prefix);
    }
}
