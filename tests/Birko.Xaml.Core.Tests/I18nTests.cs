using System.ComponentModel;
using Birko.Xaml.Core.Localization;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

public class I18nTests
{
    private static I18n Make() => new I18n()
        .AddLocale("en", new Dictionary<string, string> { ["greeting"] = "Hello {name}", ["save"] = "Save" })
        .AddLocale("sk", new Dictionary<string, string> { ["greeting"] = "Ahoj {name}", ["save"] = "Uložiť" });

    [Fact]
    public void Indexer_resolves_active_locale()
    {
        var i18n = Make();
        i18n["save"].Should().Be("Save");
        i18n.SetLocale("sk");
        i18n["save"].Should().Be("Uložiť");
    }

    [Fact]
    public void Missing_key_returns_the_key()
    {
        Make()["does.not.exist"].Should().Be("does.not.exist");
    }

    [Fact]
    public void Fallback_locale_is_consulted()
    {
        var i18n = new I18n { FallbackLocale = "en" };
        i18n.AddLocale("en", new Dictionary<string, string> { ["only.en"] = "English" });
        i18n.AddLocale("sk", new Dictionary<string, string>());
        i18n.SetLocale("sk");
        i18n["only.en"].Should().Be("English");
    }

    [Fact]
    public void Translate_interpolates_placeholders()
    {
        Make().Translate("greeting", new Dictionary<string, object?> { ["name"] = "Fero" })
            .Should().Be("Hello Fero");
    }

    [Fact]
    public void SetLocale_raises_indexer_and_locale_changed()
    {
        var i18n = Make();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)i18n).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        bool localeEvent = false;
        i18n.LocaleChanged += (_, _) => localeEvent = true;

        i18n.SetLocale("sk");

        changed.Should().Contain("Item[]", "indexer bindings must refresh");
        changed.Should().Contain(nameof(I18n.Locale));
        localeEvent.Should().BeTrue();
    }

    [Fact]
    public void SetLocale_to_same_locale_is_a_noop()
    {
        var i18n = Make();
        bool raised = false;
        i18n.LocaleChanged += (_, _) => raised = true;
        i18n.SetLocale("en"); // already en
        raised.Should().BeFalse();
    }
}
