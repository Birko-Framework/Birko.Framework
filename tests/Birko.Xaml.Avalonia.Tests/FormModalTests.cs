using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Forms;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-036: the FormModal page-shape (Modal + Form + Save/Cancel).</summary>
public class FormModalTests
{
    private static FormModal Shown(Contact model, RelayCommand? save = null)
    {
        var fm = new FormModal
        {
            IsOpen = true,
            Title = "Edit contact",
            Fields = new[] { new FormField { Name = nameof(Contact.Name), Label = "Name", Required = true } },
            Model = model,
            SaveCommand = save,
        };
        var window = new Window { Content = fm, Width = 600, Height = 400 };
        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();
        return fm;
    }

    [AvaloniaFact]
    public void Hosts_a_modal_with_a_form()
    {
        var fm = Shown(new Contact { Name = "Ada" });
        fm.GetVisualDescendants().OfType<Modal>().Single().IsOpen.Should().BeTrue();
        fm.GetVisualDescendants().OfType<TextBox>().Should().NotBeEmpty("the schema Form rendered an input");
    }

    [AvaloniaFact]
    public void Save_runs_command_then_closes()
    {
        bool saved = false;
        var fm = Shown(new Contact { Name = "Ada" }, new RelayCommand(() => saved = true));
        var save = fm.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Save");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        saved.Should().BeTrue();
        fm.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Cancel_closes_without_saving()
    {
        bool saved = false;
        var fm = Shown(new Contact { Name = "Ada" }, new RelayCommand(() => saved = true));
        var cancel = fm.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Cancel");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        saved.Should().BeFalse();
        fm.IsOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Capture_form_modal_page_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var fm = new FormModal
        {
            IsOpen = true,
            Title = "New contact",
            Fields = new[]
            {
                new FormField { Name = nameof(Contact.Name), Label = "Name", Required = true },
                new FormField { Name = nameof(Contact.Active), Label = "Active", Type = FieldType.Checkbox },
            },
            Model = new Contact { Name = "", Active = true },
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#F1F5F9")) };
        var root = new Panel();
        root.Children.Add(page);
        root.Children.Add(fm);
        var window = new Window { Content = root, Width = 640, Height = 440 };
        window.Show();
        window.Measure(new Size(640, 440));
        window.Arrange(new Rect(0, 0, 640, 440));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "form-modal-page.png"));
        fm.GetVisualDescendants().OfType<Modal>().Single().IsOpen.Should().BeTrue();
    }
}
