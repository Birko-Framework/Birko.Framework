using Birko.Xaml.Core.Localization;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>Enforces WPF-addendum constraint #1: Birko.Xaml.Core must not depend on Avalonia, so a
/// future WPF skin can reuse it unchanged. Guards against an accidental <c>using Avalonia.*</c>.</summary>
public class CoreIsAvaloniaFreeTests
{
    [Fact]
    public void Core_assembly_references_no_avalonia()
    {
        var refs = typeof(I18n).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        refs.Should().NotContain(n => n != null && n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase),
            "Birko.Xaml.Core must stay platform-neutral (Avalonia-free)");
    }
}
