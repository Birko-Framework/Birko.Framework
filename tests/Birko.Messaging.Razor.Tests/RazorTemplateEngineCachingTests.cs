using System;
using System.Reflection;
using Birko.Messaging.Razor;
using FluentAssertions;
using RazorLight;
using Xunit;

namespace Birko.Messaging.Razor.Tests;

/// <summary>
/// Regressions for CR-H122: RazorTemplateOptions.EnableCaching must actually control whether the
/// RazorLight compiled-template cache is installed. Previously UseMemoryCachingProvider() was called
/// unconditionally and then again in the !EnableCaching branch (an inverted/dead condition), so
/// caching could never be disabled.
/// </summary>
public class RazorTemplateEngineCachingTests
{
    private static bool IsCachingEnabled(RazorTemplateEngine engine)
    {
        var field = typeof(RazorTemplateEngine).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var razorEngine = (RazorLightEngine)field.GetValue(engine)!;
        return razorEngine.Handler.IsCachingEnabled;
    }

    [Fact]
    public void EnableCaching_True_InstallsCachingProvider()
    {
        using var engine = new RazorTemplateEngine(new RazorTemplateOptions { EnableCaching = true });

        IsCachingEnabled(engine).Should().BeTrue();
    }

    [Fact]
    public void EnableCaching_False_DisablesCaching()
    {
        using var engine = new RazorTemplateEngine(new RazorTemplateOptions { EnableCaching = false });

        IsCachingEnabled(engine).Should().BeFalse();
    }

    [Fact]
    public void EnableCaching_DefaultsToTrue()
    {
        using var engine = new RazorTemplateEngine();

        IsCachingEnabled(engine).Should().BeTrue();
    }
}
