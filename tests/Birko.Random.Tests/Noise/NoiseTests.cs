using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Noise;

public class PerlinNoiseTests
{
    [Fact]
    public void Noise1D_ReturnsBoundedValues()
    {
        var noise = new PerlinNoise(42);

        for (double x = 0; x < 10; x += 0.1)
        {
            var value = noise.Noise(x);
            value.Should().BeGreaterThanOrEqualTo(-1.5);
            value.Should().BeLessThanOrEqualTo(1.5);
        }
    }

    [Fact]
    public void Noise2D_ReturnsBoundedValues()
    {
        var noise = new PerlinNoise(42);

        for (double x = 0; x < 5; x += 0.5)
        {
            for (double y = 0; y < 5; y += 0.5)
            {
                var value = noise.Noise(x, y);
                value.Should().BeGreaterThanOrEqualTo(-1.5);
                value.Should().BeLessThanOrEqualTo(1.5);
            }
        }
    }

    [Fact]
    public void Noise3D_ReturnsBoundedValues()
    {
        var noise = new PerlinNoise(42);

        for (double x = 0; x < 3; x += 0.5)
        {
            for (double y = 0; y < 3; y += 0.5)
            {
                for (double z = 0; z < 3; z += 0.5)
                {
                    var value = noise.Noise(x, y, z);
                    value.Should().BeGreaterThanOrEqualTo(-1.5);
                    value.Should().BeLessThanOrEqualTo(1.5);
                }
            }
        }
    }

    [Fact]
    public void SameSeed_ProducesSameValues()
    {
        var a = new PerlinNoise(42);
        var b = new PerlinNoise(42);

        for (double x = 0; x < 5; x += 0.3)
        {
            a.Noise(x, x * 0.5).Should().Be(b.Noise(x, x * 0.5));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentValues()
    {
        var a = new PerlinNoise(1);
        var b = new PerlinNoise(2);

        bool anyDifferent = false;
        for (double x = 0; x < 5; x += 0.3)
        {
            if (a.Noise(x, x) != b.Noise(x, x))
            {
                anyDifferent = true;
                break;
            }
        }

        anyDifferent.Should().BeTrue();
    }

    [Fact]
    public void Noise_AtIntegerCoordinates_ReturnsZero()
    {
        var noise = new PerlinNoise(42);

        // Perlin noise is zero at integer coordinates
        noise.Noise(0.0, 0.0, 0.0).Should().Be(0.0);
        noise.Noise(1.0, 0.0, 0.0).Should().Be(0.0);
    }

    [Fact]
    public void Fbm_ReturnsBoundedValues()
    {
        var noise = new PerlinNoise(42);

        for (double x = 0; x < 5; x += 0.3)
        {
            var value = noise.Fbm(x, x * 0.7);
            value.Should().BeGreaterThanOrEqualTo(-1.5);
            value.Should().BeLessThanOrEqualTo(1.5);
        }
    }

    [Fact]
    public void Noise_IsContinuous()
    {
        var noise = new PerlinNoise(42);

        double prev = noise.Noise(0.0);
        for (double x = 0.01; x < 1.0; x += 0.01)
        {
            double current = noise.Noise(x);
            // Adjacent samples should not differ wildly
            Math.Abs(current - prev).Should().BeLessThan(0.2);
            prev = current;
        }
    }
}

public class SimplexNoiseTests
{
    [Fact]
    public void Noise2D_ReturnsBoundedValues()
    {
        var noise = new SimplexNoise(42);

        for (double x = 0; x < 5; x += 0.5)
        {
            for (double y = 0; y < 5; y += 0.5)
            {
                var value = noise.Noise(x, y);
                value.Should().BeGreaterThanOrEqualTo(-1.5);
                value.Should().BeLessThanOrEqualTo(1.5);
            }
        }
    }

    [Fact]
    public void Noise3D_ReturnsBoundedValues()
    {
        var noise = new SimplexNoise(42);

        for (double x = 0; x < 3; x += 0.5)
        {
            for (double y = 0; y < 3; y += 0.5)
            {
                for (double z = 0; z < 3; z += 0.5)
                {
                    var value = noise.Noise(x, y, z);
                    value.Should().BeGreaterThanOrEqualTo(-1.5);
                    value.Should().BeLessThanOrEqualTo(1.5);
                }
            }
        }
    }

    [Fact]
    public void SameSeed_ProducesSameValues()
    {
        var a = new SimplexNoise(42);
        var b = new SimplexNoise(42);

        for (double x = 0; x < 5; x += 0.3)
        {
            a.Noise(x, x * 0.5).Should().Be(b.Noise(x, x * 0.5));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentValues()
    {
        var a = new SimplexNoise(1);
        var b = new SimplexNoise(2);

        bool anyDifferent = false;
        for (double x = 0; x < 5; x += 0.3)
        {
            if (a.Noise(x, x) != b.Noise(x, x))
            {
                anyDifferent = true;
                break;
            }
        }

        anyDifferent.Should().BeTrue();
    }

    [Fact]
    public void Fbm_ReturnsBoundedValues()
    {
        var noise = new SimplexNoise(42);

        for (double x = 0; x < 5; x += 0.3)
        {
            var value = noise.Fbm(x, x * 0.7);
            value.Should().BeGreaterThanOrEqualTo(-1.5);
            value.Should().BeLessThanOrEqualTo(1.5);
        }
    }

    [Fact]
    public void Noise_IsContinuous()
    {
        var noise = new SimplexNoise(42);

        double prev = noise.Noise(0.0, 0.0);
        for (double x = 0.01; x < 1.0; x += 0.01)
        {
            double current = noise.Noise(x, 0.0);
            Math.Abs(current - prev).Should().BeLessThan(0.3);
            prev = current;
        }
    }
}
