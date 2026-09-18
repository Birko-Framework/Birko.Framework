using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using Birko.Xaml.Core.Forms;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

public class ModalTests
{
    [AvaloniaFact]
    public void IsOpen_toggles_visibility()
    {
        var modal = new Modal { Title = "Edit", Content = new TextBlock { Text = "body" }, IsOpen = false };
        var window = new Window { Content = modal, Width = 600, Height = 400 };
        window.Show();
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));

        modal.IsVisible.Should().BeFalse();

        modal.IsOpen = true;
        window.Measure(new Size(600, 400));
        window.Arrange(new Rect(0, 0, 600, 400));
        modal.IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Capture_form_modal_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var form = new Form
        {
            Fields = new[]
            {
                new FormField { Name = nameof(Contact.Name), Label = "Name", Required = true },
                new FormField { Name = nameof(Contact.Active), Label = "Active", Type = FieldType.Checkbox },
            },
            Model = new Contact { Name = "Ada", Active = true },
        };
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(form);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(new Button { Content = "Save" });
        buttons.Children.Add(new Button { Content = "Cancel" });
        body.Children.Add(buttons);

        var modal = new Modal { Title = "Edit contact", Content = body, IsOpen = true };
        var page = new Border { Background = new SolidColorBrush(Color.Parse("#F8FAFC")) };
        var root = new Panel();
        root.Children.Add(page);
        root.Children.Add(modal);

        var window = new Window { Content = root, Width = 640, Height = 460 };
        window.Show();
        window.Measure(new Size(640, 460));
        window.Arrange(new Rect(0, 0, 640, 460));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "form-modal.png"));
        modal.IsVisible.Should().BeTrue();
    }
}
