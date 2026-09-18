using Birko.Data.SQL.Caching;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.SQL.Tests.Caching;

public class SqlCacheOptionsTests
{
    [Fact]
    public void DefaultExpiration_IsFiveMinutes()
    {
        var options = new SqlCacheOptions();

        options.DefaultExpiration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Enabled_DefaultsToTrue()
    {
        var options = new SqlCacheOptions();

        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void DefaultExpiration_CanBeChanged()
    {
        var options = new SqlCacheOptions
        {
            DefaultExpiration = TimeSpan.FromMinutes(10)
        };

        options.DefaultExpiration.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Enabled_CanBeSetToFalse()
    {
        var options = new SqlCacheOptions
        {
            Enabled = false
        };

        options.Enabled.Should().BeFalse();
    }
}
