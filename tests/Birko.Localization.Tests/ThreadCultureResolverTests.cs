using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class ThreadCultureResolverTests
{
    [Fact]
    public void GetCurrentCulture_ReturnsCurrentUICulture()
    {
        var resolver = new ThreadCultureResolver();
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("sk");
            resolver.GetCurrentCulture().Name.Should().Be("sk");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void GetDefaultCulture_ReturnsInvariant_WhenNoOverride()
    {
        var resolver = new ThreadCultureResolver();
        resolver.GetDefaultCulture().Should().Be(CultureInfo.InvariantCulture);
    }

    [Fact]
    public void GetDefaultCulture_ReturnsConfigured()
    {
        var en = CultureInfo.GetCultureInfo("en");
        var resolver = new ThreadCultureResolver(en);
        resolver.GetDefaultCulture().Should().Be(en);
    }
}
