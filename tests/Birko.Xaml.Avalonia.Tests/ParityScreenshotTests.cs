using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Gallery;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// Renders the real gallery view under each theme and writes a PNG per theme — visual evidence for
/// the STORY-031 parity gate. Output goes to the scratchpad dir (BIRKO_SHOTS override), skipped
/// gracefully if headless Skia can't produce a frame in this environment.
/// </summary>
public class ParityScreenshotTests
{
    private static string OutDir =>
        Environment.GetEnvironmentVariable("BIRKO_SHOTS")
        ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");

    [AvaloniaFact]
    public void Capture_gallery_screenshot_per_theme()
    {
        Directory.CreateDirectory(OutDir);
        var view = new GalleryView();
        var window = new Window { Content = view, Width = 520, Height = 620 };
        window.Show();

        (string id, global::Avalonia.Styling.ThemeVariant v)[] themes =
        {
            ("light", BirkoThemeVariants.Light),
            ("dark", BirkoThemeVariants.Dark),
            ("neon", BirkoThemeVariants.Neon),
            ("finstat", BirkoThemeVariants.Finstat),
        };

        int written = 0;
        foreach (var (id, variant) in themes)
        {
            Application.Current!.RequestedThemeVariant = variant;
            window.Measure(new Size(520, 620));
            window.Arrange(new Rect(0, 0, 520, 620));
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            if (frame is null) continue;
            string path = Path.Combine(OutDir, $"gallery-{id}.png");
            frame.Save(path);
            written++;
        }

        // At minimum the app + view must render without throwing; screenshots are best-effort.
        view.IsInitialized.Should().BeTrue();
        if (written > 0)
            written.Should().Be(4, "every theme should capture if any does");
    }
}
