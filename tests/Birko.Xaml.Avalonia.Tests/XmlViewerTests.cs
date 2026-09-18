using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035: the XML viewer (b-xml-viewer).</summary>
public class XmlViewerTests
{
    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 420, Height = 320 };
        window.Show();
        window.Measure(new Size(420, 320));
        window.Arrange(new Rect(0, 0, 420, 320));
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static IEnumerable<string?> Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    [AvaloniaFact]
    public void Renders_elements_attributes_and_text()
    {
        var v = Show(new XmlViewer { Xml = "<user id=\"7\"><name>Ada</name></user>" });
        Texts(v).Should().Contain("<user>");
        Texts(v).Should().Contain("@id =");
        Texts(v).Should().Contain("<name>");
        Texts(v).Should().Contain("\"Ada\"");
    }

    [AvaloniaFact]
    public void Invalid_xml_falls_back_to_raw_leaf()
    {
        var v = Show(new XmlViewer { Xml = "<broken" });
        v.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Capture_xml_viewer_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var viewer = new XmlViewer
        {
            Margin = new Thickness(16),
            Xml = "<order id=\"1024\" status=\"open\">"
                + "<customer>Ada Lovelace</customer>"
                + "<lines><line sku=\"A-1\" qty=\"2\" /><line sku=\"B-9\" qty=\"1\" /></lines>"
                + "</order>",
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFFFFF")), Child = viewer };
        var window = new Window { Content = page, Width = 400, Height = 360 };
        window.Show();
        window.Measure(new Size(400, 360));
        window.Arrange(new Rect(0, 0, 400, 360));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "xml-viewer.png"));
        viewer.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
    }
}
