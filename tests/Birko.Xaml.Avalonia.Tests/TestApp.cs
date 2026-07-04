using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Birko.Xaml.Avalonia.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>Headless application that mirrors a real Birko app: Fluent base theme + the generated
/// Tokens.axaml and restyled ControlThemes (via BirkoTheme.axaml). Skia drawing is enabled so the
/// parity test can capture real screenshots.</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://Birko.Xaml.Avalonia/BirkoTheme.axaml"),
        });
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
