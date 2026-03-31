using Birko.Caching;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Caching.Tests.Core;

public class CacheEntryOptionsTests
{
    [Fact]
    public void Default_AbsoluteExpiration_Is5Minutes()
    {
        var options = CacheEntryOptions.Default;

        options.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(5));
        options.SlidingExpiration.Should().BeNull();
        options.Priority.Should().Be(CachePriority.Normal);
    }

    [Fact]
    public void Absolute_SetsAbsoluteExpiration()
    {
        var options = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10));

        options.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(10));
        options.SlidingExpiration.Should().BeNull();
    }

    [Fact]
    public void Sliding_SetsSlidingExpiration()
    {
        var options = CacheEntryOptions.Sliding(TimeSpan.FromMinutes(2));

        options.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(2));
        options.AbsoluteExpiration.Should().BeNull();
    }

    [Fact]
    public void AbsoluteAndSliding_SetsBoth()
    {
        var options = CacheEntryOptions.AbsoluteAndSliding(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));

        options.AbsoluteExpiration.Should().Be(TimeSpan.FromMinutes(30));
        options.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Priority_DefaultsToNormal()
    {
        var options = new CacheEntryOptions();

        options.Priority.Should().Be(CachePriority.Normal);
    }
}
