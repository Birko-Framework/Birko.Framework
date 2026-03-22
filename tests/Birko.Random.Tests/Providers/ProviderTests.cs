using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Providers;

public abstract class ProviderTestBase
{
    protected abstract IRandomProvider CreateProvider();

    [Fact]
    public void NextInt_ReturnsNonNegative()
    {
        var provider = CreateProvider();

        for (int i = 0; i < 100; i++)
        {
            provider.NextInt().Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void NextInt_WithMaxValue_ReturnsInRange()
    {
        var provider = CreateProvider();

        for (int i = 0; i < 100; i++)
        {
            provider.NextInt(10).Should().BeInRange(0, 9);
        }
    }

    [Fact]
    public void NextInt_WithRange_ReturnsInRange()
    {
        var provider = CreateProvider();

        for (int i = 0; i < 100; i++)
        {
            provider.NextInt(5, 15).Should().BeInRange(5, 14);
        }
    }

    [Fact]
    public void NextLong_ReturnsNonNegative()
    {
        var provider = CreateProvider();

        for (int i = 0; i < 100; i++)
        {
            provider.NextLong().Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void NextDouble_ReturnsInUnitRange()
    {
        var provider = CreateProvider();

        for (int i = 0; i < 100; i++)
        {
            var value = provider.NextDouble();
            value.Should().BeGreaterThanOrEqualTo(0.0);
            value.Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void NextBytes_FillsBuffer()
    {
        var provider = CreateProvider();
        var buffer = new byte[32];

        provider.NextBytes(buffer);

        // Extremely unlikely all zeros from a real provider
        buffer.Should().Contain(b => b != 0);
    }

    [Fact]
    public void NextBool_ReturnsBooleanValues()
    {
        var provider = CreateProvider();
        bool seenTrue = false;
        bool seenFalse = false;

        for (int i = 0; i < 100; i++)
        {
            if (provider.NextBool()) seenTrue = true;
            else seenFalse = true;

            if (seenTrue && seenFalse) break;
        }

        seenTrue.Should().BeTrue("should produce true at least once in 100 tries");
        seenFalse.Should().BeTrue("should produce false at least once in 100 tries");
    }
}

public class SystemRandomProviderTests : ProviderTestBase
{
    protected override IRandomProvider CreateProvider() => new SystemRandomProvider();
}

public class CryptoRandomProviderTests : ProviderTestBase
{
    protected override IRandomProvider CreateProvider() => new CryptoRandomProvider();

    [Fact]
    public void NextInt_WithZeroMaxValue_Throws()
    {
        var provider = new CryptoRandomProvider();

        var act = () => provider.NextInt(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextInt_WithInvalidRange_Throws()
    {
        var provider = new CryptoRandomProvider();

        var act = () => provider.NextInt(10, 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class XorShiftProviderTests : ProviderTestBase
{
    protected override IRandomProvider CreateProvider() => new XorShiftProvider(42);

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new XorShiftProvider(123);
        var b = new XorShiftProvider(123);

        for (int i = 0; i < 10; i++)
        {
            a.NextInt().Should().Be(b.NextInt());
        }
    }
}

public class MersenneTwisterProviderTests : ProviderTestBase
{
    protected override IRandomProvider CreateProvider() => new MersenneTwisterProvider(42);

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new MersenneTwisterProvider(123);
        var b = new MersenneTwisterProvider(123);

        for (int i = 0; i < 10; i++)
        {
            a.NextDouble().Should().Be(b.NextDouble());
        }
    }
}

public class SplitMixProviderTests : ProviderTestBase
{
    protected override IRandomProvider CreateProvider() => new SplitMixProvider(42);

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new SplitMixProvider(123);
        var b = new SplitMixProvider(123);

        for (int i = 0; i < 10; i++)
        {
            a.NextLong().Should().Be(b.NextLong());
        }
    }
}
