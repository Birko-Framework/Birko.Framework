using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Providers;

public class TestRandomProviderTests
{
    [Fact]
    public void NextInt_ReturnsDefault_WhenQueueEmpty()
    {
        var provider = new TestRandomProvider(defaultInt: 42);

        provider.NextInt().Should().Be(42);
    }

    [Fact]
    public void NextInt_ReturnsQueuedValues()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueInt(1, 2, 3);

        provider.NextInt().Should().Be(1);
        provider.NextInt().Should().Be(2);
        provider.NextInt().Should().Be(3);
    }

    [Fact]
    public void NextInt_FallsBackToDefault_AfterQueueDrained()
    {
        var provider = new TestRandomProvider(defaultInt: 99);
        provider.EnqueueInt(1);

        provider.NextInt().Should().Be(1);
        provider.NextInt().Should().Be(99);
    }

    [Fact]
    public void NextDouble_ReturnsQueuedValues()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueDouble(0.1, 0.9);

        provider.NextDouble().Should().Be(0.1);
        provider.NextDouble().Should().Be(0.9);
    }

    [Fact]
    public void NextBool_ReturnsQueuedValues()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueBool(true, false, true);

        provider.NextBool().Should().BeTrue();
        provider.NextBool().Should().BeFalse();
        provider.NextBool().Should().BeTrue();
    }

    [Fact]
    public void NextBytes_ReturnsQueuedBytes()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueBytes(new byte[] { 1, 2, 3, 4 });

        var buffer = new byte[4];
        provider.NextBytes(buffer);

        buffer.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void NextBytes_ClearsBuffer_WhenQueueEmpty()
    {
        var provider = new TestRandomProvider();
        var buffer = new byte[] { 0xFF, 0xFF, 0xFF };

        provider.NextBytes(buffer);

        buffer.Should().AllBeEquivalentTo(0);
    }

    [Fact]
    public void NextInt_WithMaxValue_ClampsQueuedValue()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueInt(100);

        provider.NextInt(10).Should().Be(9);
    }

    [Fact]
    public void NextInt_WithRange_ClampsQueuedValue()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueInt(100);

        provider.NextInt(5, 15).Should().Be(14);
    }

    [Fact]
    public void SetDefaults_ChangesDefaultValues()
    {
        var provider = new TestRandomProvider();
        provider.SetDefaults(defaultInt: 7, defaultDouble: 0.3, defaultBool: true);

        provider.NextInt().Should().Be(7);
        provider.NextDouble().Should().Be(0.3);
        provider.NextBool().Should().BeTrue();
    }

    [Fact]
    public void NextLong_ReturnsQueuedValues()
    {
        var provider = new TestRandomProvider();
        provider.EnqueueLong(long.MaxValue, 0L);

        provider.NextLong().Should().Be(long.MaxValue);
        provider.NextLong().Should().Be(0L);
    }
}
