using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-773 (FEATURE-020 D7): JsonDiffViewer's side-by-side view — the web's split view (two whole bodies, changed lines
/// tinted, not lined up) plus panes that scroll together. The cases mirror the Playground's json-diff-smoke / xml-diff-smoke.
/// </summary>
public class JsonDiffViewerSplitTests(ITestOutputHelper output)
{
    private const string A = """{"Name":"x","Url":"a","Same":{"k":1},"Items":[{"Id":1,"v":1},{"Id":2,"v":2}],"gone":true}""";
    private const string B = """{"Name":"y","Url":"b","Same":{"k":1},"Items":[{"Id":2,"v":2},{"Id":1,"v":5}],"extra":1}""";
    private const string Xml = "<EliteCZResult xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><Ico>00177041</Ico><Offices><Count>3</Count></Offices><Empty/></EliteCZResult>";

    private static JsonDiffViewer Show(JsonDiffViewer v, int height = 600)
    {
        var window = new Window { Content = v, Width = 900, Height = height };
        window.Show();
        Pump(v, height);
        return v;
    }

    private static void Pump(Control c, int height = 600)
    {
        var root = (Window)c.GetVisualRoot()!;
        root.Measure(new Size(900, height));
        root.Arrange(new Rect(0, 0, 900, height));
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Pane(Control v, string cls) => v.GetVisualDescendants().OfType<DockPanel>().Single(p => p.Classes.Contains(cls));

    private static List<string> Marked(Control pane, string mark) =>
        pane.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("line") && b.Classes.Contains("mk-" + mark))
            .Select(b => ((TextBlock)b.Child!).Text!.Trim()).ToList();

    private static List<string> Lines(Control pane) =>
        pane.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("line")).Select(b => ((TextBlock)b.Child!).Text!.Trim()).ToList();

    private static JsonDiffViewer Split() => new()
    {
        View = JsonDiffView.SideBySide,
        LabelA = "production",
        LabelB = "localhost",
        Options = new JsonDiffOptions { Ignore = ["Url"], Arrays = [new JsonArrayRule("Items") { Key = "Id" }] },
    };

    [AvaloniaFact]
    public void Two_labelled_panes_each_show_their_whole_body()
    {
        var v = Show(Split());
        v.SetData(A, B);
        Pump(v);

        Pane(v, "pane-a").GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("pane-label")).Text.Should().Be("PRODUCTION");
        Pane(v, "pane-b").GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("pane-label")).Text.Should().Be("LOCALHOST");
        Lines(Pane(v, "pane-a")).Should().StartWith(["{", "\"Name\": \"x\","]).And.Contain("\"gone\": true");
        Lines(Pane(v, "pane-b")).Should().Contain("\"extra\": 1").And.NotContain("\"gone\": true");
    }

    [AvaloniaFact]
    public void Lines_are_tinted_by_the_diff_on_their_own_side()
    {
        var v = Show(Split());
        v.SetData(A, B);
        Pump(v);
        var a = Pane(v, "pane-a");
        var b = Pane(v, "pane-b");

        Marked(a, "removed").Should().Equal("\"gone\": true");
        Marked(b, "added").Should().Equal("\"extra\": 1");
        // Items[1] on B pairs with Items[0] on A (by Id): A marks the moved item's v at its own index.
        Marked(a, "changed").Should().Equal("\"Name\": \"x\",", "\"v\": 1");
        Marked(b, "changed").Should().Equal("\"Name\": \"y\",", "\"v\": 5");
        Marked(a, "ignored").Should().Equal("\"Url\": \"a\",");
        Marked(b, "ignored").Should().Equal("\"Url\": \"b\",");
    }

    [AvaloniaFact]
    public void Xml_mode_shows_the_original_xml_with_its_changes_marked()
    {
        var v = Show(new JsonDiffViewer { View = JsonDiffView.SideBySide, Format = JsonDiffFormat.Xml });
        v.SetData(Xml, Xml.Replace("<Count>3</Count>", "<Count>4</Count>").Replace("<Empty/>", "<Empty/><New><X>1</X></New>"));
        Pump(v);

        Lines(Pane(v, "pane-a"))[0].Should().StartWith("<EliteCZResult xmlns:xsi=");
        Marked(Pane(v, "pane-a"), "changed").Should().Equal("<Count>3</Count>");
        Marked(Pane(v, "pane-b"), "changed").Should().Equal("<Count>4</Count>");
        Marked(Pane(v, "pane-b"), "added").Should().Equal("<New>", "<X>1</X>", "</New>");
    }

    [AvaloniaFact]
    public void A_value_replaced_by_an_element_marks_the_lines_inside_it()
    {
        var v = Show(new JsonDiffViewer { View = JsonDiffView.SideBySide, Format = JsonDiffFormat.Xml });
        v.SetData("<r><a>1</a><z>0</z></r>", "<r><a><x>1</x><y>2</y></a><z>0</z></r>");
        Pump(v);

        Marked(Pane(v, "pane-b"), "changed").Should().Equal("<a>", "<x>1</x>", "<y>2</y>", "</a>");
    }

    [AvaloniaFact]
    public void A_side_that_does_not_parse_shows_its_error_and_raw_text_and_the_other_side_still_shows()
    {
        var v = Show(Split());
        v.SetData("{\"a\":", B);
        Pump(v);

        var a = Pane(v, "pane-a");
        a.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Classes.Contains("error") && t.Text!.StartsWith("production: JSON parse error"));
        Lines(a).Should().Equal("{\"a\":");
        Lines(Pane(v, "pane-b")).Should().Contain("\"extra\": 1");
    }

    [AvaloniaFact]
    public void The_toolbar_switches_views_and_the_tree_only_actions_hide_in_side_by_side()
    {
        var v = Show(new JsonDiffViewer());
        v.SetData(A, B);
        Pump(v);
        v.GetVisualDescendants().OfType<ToggleButton>().Should().Contain(t => t.Classes.Contains("only-changes"));

        v.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Classes.Contains("view-split")).IsChecked = true;
        Pump(v);

        v.View.Should().Be(JsonDiffView.SideBySide);
        v.GetVisualDescendants().OfType<ToggleButton>().Should().NotContain(t => t.Classes.Contains("only-changes"));
        v.GetVisualDescendants().OfType<Button>().Should().NotContain(b => b.Classes.Contains("expand-all"));
        v.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Classes.Contains("view-split")).IsChecked.Should().BeTrue();

        // Clicking the current view again keeps it.
        v.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Classes.Contains("view-split")).IsChecked = false;
        Pump(v);
        v.View.Should().Be(JsonDiffView.SideBySide);

        v.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Classes.Contains("view-tree")).IsChecked = true;
        Pump(v);
        v.View.Should().Be(JsonDiffView.Tree);
        v.GetVisualDescendants().OfType<ToggleButton>().Should().Contain(t => t.Classes.Contains("only-changes"));
    }

    private static string Big(int items, int tweak)
    {
        var sb = new StringBuilder("{\"Items\":[");
        for (var i = 0; i < items; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"Id\":{i},\"Name\":\"Company {i} s.r.o.\",\"Ico\":\"{10000000 + i}\",\"Url\":\"https://www.finstat.sk/{i}\",\"Revenue\":{i * 1000 + (i % 97 == 0 ? tweak : 0)},\"Tags\":[\"a\",\"b\"]}}");
        }
        return sb.Append("]}").ToString();
    }

    [AvaloniaFact]
    public void The_panes_scroll_together()
    {
        var v = Show(Split(), height: 400);
        v.SetData(Big(200, 0), Big(200, 1));
        Pump(v, 400);
        var a = Pane(v, "pane-a").GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Classes.Contains("pane-scroll"));
        var b = Pane(v, "pane-b").GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Classes.Contains("pane-scroll"));
        a.Extent.Height.Should().BeGreaterThan(a.Viewport.Height, "the body must be taller than the pane for this test to mean anything");

        a.Offset = new Vector(0, 900);
        Pump(v, 400);
        b.Offset.Y.Should().Be(900);

        b.Offset = new Vector(0, 300);
        Pump(v, 400);
        a.Offset.Y.Should().Be(300);
    }

    [AvaloniaFact]
    public void A_large_body_is_shown_in_reasonable_time()
    {
        // A FinStat-sized response: 3 000 records, about 400 KB, some 30 000 lines a side.
        var a = Big(3000, 0);
        var b = Big(3000, 7);
        var v = Show(Split());

        var sw = Stopwatch.StartNew();
        v.SetData(a, b);
        Pump(v);
        var split = sw.ElapsedMilliseconds;

        sw.Restart();
        v.View = JsonDiffView.Tree;
        Pump(v);
        var tree = sw.ElapsedMilliseconds;

        output.WriteLine($"body {a.Length / 1024} KB a side; diff + side-by-side render {split} ms; switch to tree {tree} ms; " +
                         $"{v.Result!.Summary.Total} changes");
        v.Result.Summary.Changed.Should().Be(31);
        // The open Items array has 3 000 rows; only the ones in view may be built (the tree took 4.1 s when all were).
        var built = v.GetVisualDescendants().OfType<StackPanel>().Count(p => p.Classes.Contains("row"));
        output.WriteLine($"tree rows built: {built}");
        built.Should().BeInRange(1, 200);
        split.Should().BeLessThan(10_000, "a generous ceiling; the measured value is in the test output");
    }

    [AvaloniaTheory]
    [InlineData("Light", false)]
    [InlineData("Dark", false)]
    [InlineData("Light", true)]
    public void Capture_side_by_side_screenshot(string theme, bool xml)
    {
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark : Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);
            var v = xml ? new JsonDiffViewer { View = JsonDiffView.SideBySide, Format = JsonDiffFormat.Xml, LabelA = "production", LabelB = "localhost" } : Split();
            v.Margin = new Thickness(16);
            var page = new Border { Child = v };
            page.Bind(Border.BackgroundProperty, page.GetResourceObservable("BBgBrush"));
            var window = new Window { Content = page, Width = 900, Height = 520 };
            window.Show();
            if (xml) v.SetData(Xml, Xml.Replace("<Count>3</Count>", "<Count>4</Count>").Replace("<Empty/>", "<Empty/><New><X>1</X></New>"));
            else v.SetData(A, B);
            window.Measure(new Size(900, 520));
            window.Arrange(new Rect(0, 0, 900, 520));
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)?.Save(Path.Combine(dir, $"json-diff-split-{theme.ToLowerInvariant()}{(xml ? "-xml" : "")}.png"));
            v.Result.Should().NotBeNull();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }

    [Fact]
    public void JsonLines_pretty_prints_in_the_engines_key_order_and_inherits_marks()
    {
        var lines = JsonDiffViewer.JsonLines(JsonNode.Parse("""{"b":{"x":1},"2":[],"1":{}}"""),
            new Dictionary<string, DiffLineMark> { ["b"] = DiffLineMark.Added });

        lines.Select(l => l.Text).Should().Equal("{", "  \"1\": {},", "  \"2\": [],", "  \"b\": {", "    \"x\": 1", "  }", "}");
        lines.Where(l => l.Mark == DiffLineMark.Added).Select(l => l.Text.Trim()).Should().Equal("\"b\": {", "\"x\": 1", "}");
    }
}
