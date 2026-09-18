using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Commands;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035/036: the command palette — filter, keyboard/invoke, close.</summary>
public class CommandPaletteTests
{
    private static CommandPalette Shown(out bool[] ran)
    {
        var flags = new bool[3];
        var palette = new CommandPalette
        {
            Commands = new[]
            {
                new CommandItem { Id = "new", Label = "New file", Group = "File", Run = () => flags[0] = true },
                new CommandItem { Id = "open", Label = "Open folder", Group = "File", Run = () => flags[1] = true },
                new CommandItem { Id = "save", Label = "Save all", Group = "File", Run = () => flags[2] = true },
            },
            IsOpen = true,
        };
        var window = new Window { Content = palette, Width = 600, Height = 400 };
        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();
        ran = flags;
        return palette;
    }

    [AvaloniaFact]
    public void Shows_all_commands_when_empty()
    {
        var palette = Shown(out _);
        palette.FilteredCommands.Should().HaveCount(3);
    }

    [AvaloniaFact]
    public void Filters_by_search_text()
    {
        var palette = Shown(out _);
        palette.SearchText = "save";
        palette.FilteredCommands.Should().ContainSingle(c => c.Id == "save");
    }

    [AvaloniaFact]
    public void Invoke_runs_selected_and_closes()
    {
        var palette = Shown(out var ran);
        palette.SearchText = "save";
        palette.InvokeSelected();
        ran[2].Should().BeTrue("the selected command's Run executed");
        palette.IsOpen.Should().BeFalse("the palette closes after invoking");
    }

    [AvaloniaFact]
    public void Capture_command_palette_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var palette = new CommandPalette
        {
            Commands = new[]
            {
                new CommandItem { Id = "n", Label = "New contact", Group = "Contacts" },
                new CommandItem { Id = "i", Label = "Import CSV", Group = "Data" },
                new CommandItem { Id = "s", Label = "Settings", Group = "App" },
                new CommandItem { Id = "t", Label = "Toggle theme", Group = "App" },
            },
            IsOpen = true,
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F1F5F9")) };
        var root = new Panel();
        root.Children.Add(page);
        root.Children.Add(palette);
        var window = new Window { Content = root, Width = 640, Height = 460 };
        window.Show();
        window.Measure(new Size(640, 460));
        window.Arrange(new Rect(0, 0, 640, 460));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "command-palette.png"));
        palette.FilteredCommands.Should().HaveCount(4);
    }
}
