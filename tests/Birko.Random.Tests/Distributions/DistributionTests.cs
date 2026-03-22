using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Distributions;

public class UniformDistributionTests
{
    [Fact]
    public void Next_ReturnsValuesInRange()
    {
        var rng = new SplitMixProvider(42);
        var dist = new UniformDistribution(rng, 10.0, 20.0);

        for (int i = 0; i < 100; i++)
        {
            var value = dist.Next();
            value.Should().BeGreaterThanOrEqualTo(10.0);
            value.Should().BeLessThan(20.0);
        }
    }

    [Fact]
    public void Constructor_InvalidRange_Throws()
    {
        var rng = new SplitMixProvider(42);

        var act = () => new UniformDistribution(rng, 10.0, 5.0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new UniformDistribution(null!, 0, 1);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NextInt_ReturnsValuesInRange()
    {
        var rng = new SplitMixProvider(42);
        var dist = new UniformDistribution(rng);

        for (int i = 0; i < 100; i++)
        {
            dist.NextInt(0, 10).Should().BeInRange(0, 9);
        }
    }
}

public class NormalDistributionTests
{
    [Fact]
    public void Next_ProducesValuesAroundMean()
    {
        var rng = new SplitMixProvider(42);
        var dist = new NormalDistribution(rng, mean: 100, stdDev: 10);

        double sum = 0;
        int count = 10000;

        for (int i = 0; i < count; i++)
        {
            sum += dist.Next();
        }

        double mean = sum / count;
        mean.Should().BeApproximately(100, 2.0);
    }

    [Fact]
    public void Constructor_InvalidStdDev_Throws()
    {
        var rng = new SplitMixProvider(42);

        var act = () => new NormalDistribution(rng, stdDev: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new NormalDistribution(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

public class ExponentialDistributionTests
{
    [Fact]
    public void Next_ProducesPositiveValues()
    {
        var rng = new SplitMixProvider(42);
        var dist = new ExponentialDistribution(rng, rate: 2.0);

        for (int i = 0; i < 100; i++)
        {
            dist.Next().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Next_MeanApproximatesInverseRate()
    {
        var rng = new SplitMixProvider(42);
        var dist = new ExponentialDistribution(rng, rate: 0.5);

        double sum = 0;
        int count = 10000;

        for (int i = 0; i < count; i++)
        {
            sum += dist.Next();
        }

        double mean = sum / count;
        mean.Should().BeApproximately(2.0, 0.3); // mean = 1/rate = 2.0
    }

    [Fact]
    public void Constructor_InvalidRate_Throws()
    {
        var rng = new SplitMixProvider(42);

        var act = () => new ExponentialDistribution(rng, rate: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class PoissonDistributionTests
{
    [Fact]
    public void Next_ProducesNonNegativeIntegers()
    {
        var rng = new SplitMixProvider(42);
        var dist = new PoissonDistribution(rng, lambda: 5.0);

        for (int i = 0; i < 100; i++)
        {
            dist.Next().Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Next_SmallLambda_MeanApproximatesLambda()
    {
        var rng = new SplitMixProvider(42);
        var dist = new PoissonDistribution(rng, lambda: 3.0);

        double sum = 0;
        int count = 10000;

        for (int i = 0; i < count; i++)
        {
            sum += dist.Next();
        }

        double mean = sum / count;
        mean.Should().BeApproximately(3.0, 0.3);
    }

    [Fact]
    public void Next_LargeLambda_MeanApproximatesLambda()
    {
        var rng = new SplitMixProvider(42);
        var dist = new PoissonDistribution(rng, lambda: 50.0);

        double sum = 0;
        int count = 10000;

        for (int i = 0; i < count; i++)
        {
            sum += dist.Next();
        }

        double mean = sum / count;
        mean.Should().BeApproximately(50.0, 3.0);
    }

    [Fact]
    public void Constructor_InvalidLambda_Throws()
    {
        var rng = new SplitMixProvider(42);

        var act = () => new PoissonDistribution(rng, lambda: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class BernoulliDistributionTests
{
    [Fact]
    public void Next_WithProbabilityOne_AlwaysReturnsTrue()
    {
        var rng = new SplitMixProvider(42);
        var dist = new BernoulliDistribution(rng, probability: 1.0);

        for (int i = 0; i < 100; i++)
        {
            dist.Next().Should().BeTrue();
        }
    }

    [Fact]
    public void Next_WithProbabilityZero_AlwaysReturnsFalse()
    {
        var rng = new SplitMixProvider(42);
        var dist = new BernoulliDistribution(rng, probability: 0.0);

        for (int i = 0; i < 100; i++)
        {
            dist.Next().Should().BeFalse();
        }
    }

    [Fact]
    public void Next_WithHalfProbability_ProducesBothValues()
    {
        var rng = new SplitMixProvider(42);
        var dist = new BernoulliDistribution(rng, probability: 0.5);

        bool seenTrue = false;
        bool seenFalse = false;

        for (int i = 0; i < 100; i++)
        {
            if (dist.Next()) seenTrue = true;
            else seenFalse = true;
        }

        seenTrue.Should().BeTrue();
        seenFalse.Should().BeTrue();
    }

    [Fact]
    public void NextInt_ReturnsOneOrZero()
    {
        var rng = new SplitMixProvider(42);
        var dist = new BernoulliDistribution(rng, probability: 0.5);

        for (int i = 0; i < 100; i++)
        {
            dist.NextInt().Should().BeOneOf(0, 1);
        }
    }

    [Fact]
    public void Constructor_InvalidProbability_Throws()
    {
        var rng = new SplitMixProvider(42);

        var act1 = () => new BernoulliDistribution(rng, probability: -0.1);
        var act2 = () => new BernoulliDistribution(rng, probability: 1.1);

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }
}
