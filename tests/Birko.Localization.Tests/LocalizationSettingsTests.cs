using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class LocalizationSettingsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var settings = LocalizationSettings.Default;

        settings.DefaultCulture.Should().Be(CultureInfo.InvariantCulture);
        settings.FallbackToParentCulture.Should().BeTrue();
        settings.MissingKeyBehavior.Should().Be(MissingKeyBehavior.ReturnKey);
        settings.KeyPrefix.Should().BeNull();
    }

    [Fact]
    public void WithDefaultCulture_ReturnsNewInstance()
    {
        var sk = CultureInfo.GetCultureInfo("sk");
        var settings = LocalizationSettings.Default.WithDefaultCulture(sk);

        settings.DefaultCulture.Should().Be(sk);
        settings.FallbackToParentCulture.Should().BeTrue();
        LocalizationSettings.Default.DefaultCulture.Should().Be(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void WithFallbackToParentCulture_ReturnsNewInstance()
    {
        var settings = LocalizationSettings.Default.WithFallbackToParentCulture(false);
        settings.FallbackToParentCulture.Should().BeFalse();
    }

    [Fact]
    public void WithMissingKeyBehavior_ReturnsNewInstance()
    {
        var settings = LocalizationSettings.Default.WithMissingKeyBehavior(MissingKeyBehavior.ThrowException);
        settings.MissingKeyBehavior.Should().Be(MissingKeyBehavior.ThrowException);
    }

    [Fact]
    public void WithKeyPrefix_ReturnsNewInstance()
    {
        var settings = LocalizationSettings.Default.WithKeyPrefix("app");
        settings.KeyPrefix.Should().Be("app");
    }

    [Fact]
    public void Chaining_PreservesAllValues()
    {
        var sk = CultureInfo.GetCultureInfo("sk");
        var settings = LocalizationSettings.Default
            .WithDefaultCulture(sk)
            .WithFallbackToParentCulture(false)
            .WithMissingKeyBehavior(MissingKeyBehavior.ReturnEmpty)
            .WithKeyPrefix("module");

        settings.DefaultCulture.Should().Be(sk);
        settings.FallbackToParentCulture.Should().BeFalse();
        settings.MissingKeyBehavior.Should().Be(MissingKeyBehavior.ReturnEmpty);
        settings.KeyPrefix.Should().Be("module");
    }
}
