using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Birko.Xaml.Avalonia.Markup;
using Birko.Xaml.Core.Localization;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-032: the {l:Tr} markup extension resolves through the Core I18n singleton and
/// re-resolves live when the locale changes (proving the Core-logic / Avalonia-binding split).</summary>
public class TrExtensionTests
{
    [AvaloniaFact]
    public void Tr_binding_reresolves_on_locale_change()
    {
        I18n.Instance.AddLocale("en", new Dictionary<string, string> { ["hi"] = "Hi" });
        I18n.Instance.AddLocale("xx", new Dictionary<string, string> { ["hi"] = "Ahoj" });
        I18n.Instance.SetLocale("en");

        var binding = (Binding)new TrExtension("hi").ProvideValue(null!);
        var tb = new TextBlock();
        tb.Bind(TextBlock.TextProperty, binding);
        var window = new Window { Content = tb };
        window.Show();

        tb.Text.Should().Be("Hi");

        I18n.Instance.SetLocale("xx");
        tb.Text.Should().Be("Ahoj", "the {l:Tr} binding must re-resolve on SetLocale");

        I18n.Instance.SetLocale("en"); // reset shared singleton for other tests
    }
}
