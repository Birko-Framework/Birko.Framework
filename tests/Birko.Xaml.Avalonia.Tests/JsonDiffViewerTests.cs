using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Controls;
using Birko.Xaml.Core.Diff;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>TASK-772 (FEATURE-020): JsonDiffViewer's tree view, the XAML port of b-json-diff's tree.</summary>
public class JsonDiffViewerTests
{
    private const string A = """{"Name":"x","Url":"a","Same":{"k":1},"Items":[{"Id":1,"v":1},{"Id":2,"v":2}],"gone":true}""";
    private const string B = """{"Name":"y","Url":"b","Same":{"k":1},"Items":[{"Id":2,"v":2},{"Id":1,"v":5}],"extra":1}""";

    private static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 900, Height = 600 };
        window.Show();
        Pump(window);
        return control;
    }

    private static void Pump(Control c)
    {
        var root = (Window)c.GetVisualRoot()!;
        root.Measure(new Size(900, 600));
        root.Arrange(new Rect(0, 0, 900, 600));
        Dispatcher.UIThread.RunJobs();
    }

    private static List<StackPanel> Rows(Control c, string status) =>
        c.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("row") && p.Classes.Contains("st-" + status)).ToList();

    private static IEnumerable<string?> Texts(Control c) => c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text);

    private static T Find<T>(Control c, string cls) where T : Control =>
        c.GetVisualDescendants().OfType<T>().First(x => x.Classes.Contains(cls));

    private static JsonDiffViewer Viewer() =>
        new() { LabelA = "production", LabelB = "localhost", Options = new JsonDiffOptions { Ignore = ["Url"], Arrays = [new JsonArrayRule("Items") { Key = "Id" }] } };

    [AvaloniaFact]
    public void Before_data_it_says_there_is_nothing_to_compare()
    {
        var v = Show(new JsonDiffViewer());

        Texts(v).Should().Contain("Nothing to compare");
        v.Result.Should().BeNull();
    }

    [AvaloniaFact]
    public void Rows_carry_the_engines_statuses_and_the_summary_its_counts()
    {
        var v = Show(Viewer());
        JsonDiffResult? computed = null;
        v.DiffComputed += (_, r) => computed = r;

        v.SetData(A, B);
        Pump(v);

        computed.Should().NotBeNull();
        v.Result!.Summary.Should().Be(new JsonDiffSummary(1, 1, 2));
        Rows(v, "added").Should().ContainSingle();
        Rows(v, "removed").Should().ContainSingle();
        Rows(v, "changed").Should().HaveCount(2, "Name, and Items[1].v once the modified Items opens");
        Rows(v, "ignored").Should().ContainSingle();
        Texts(Find<StackPanel>(v, "summary")).Should().Contain(["1 added", "1 removed", "2 changed"]);
        Texts(Find<StackPanel>(v, "legend")).Should().Contain(["− production", "+ localhost"]);
    }

    [AvaloniaFact]
    public void A_changed_value_shows_old_and_new_and_states_its_status_as_text()
    {
        var v = Show(Viewer());
        v.SetData(A, B);
        Pump(v);

        var name = Rows(v, "changed").Single(r => Texts(r).Contains("Name"));
        Texts(name).Should().ContainInOrder("\"x\"", "→", "\"y\"");
        name.GetVisualDescendants().OfType<TextBlock>().First().FontFamily.ToString().Should().Contain("monospace", "the body is set in BFontMono");
        AutomationProperties.GetName(name).Should().Be("changed Name");
    }

    [AvaloniaFact]
    public void Containers_with_changes_start_open_and_unchanged_ones_closed()
    {
        var v = Show(Viewer());
        v.SetData(A, B);
        Pump(v);

        var items = Rows(v, "modified").Single(r => Texts(r).Contains("Items"));
        items.Children.OfType<ToggleButton>().Single().IsChecked.Should().BeTrue();
        var same = Rows(v, "equal").Single(r => Texts(r).Contains("Same"));
        same.Children.OfType<ToggleButton>().Single().IsChecked.Should().BeFalse();
        Texts(v).Should().NotContain("k", "a closed container does not render its children");
        Texts(items).Should().Contain("(1 change)");
    }

    [AvaloniaFact]
    public void Only_changes_hides_equal_and_ignored_rows()
    {
        var v = Show(Viewer());
        v.SetData(A, B);
        Pump(v);

        Find<ToggleButton>(v, "only-changes").IsChecked = true;
        Pump(v);

        v.OnlyChanges.Should().BeTrue();
        Rows(v, "equal").Should().BeEmpty();
        Rows(v, "ignored").Should().BeEmpty();
        Rows(v, "changed").Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Expand_all_opens_every_container_and_collapse_all_closes_them()
    {
        var v = Show(Viewer());
        v.SetData(A, B);
        Pump(v);

        v.ExpandAll();
        Pump(v);
        v.GetVisualDescendants().OfType<ToggleButton>().Where(t => t.Classes.Contains("toggle")).Should().OnlyContain(t => t.IsChecked == true);
        Texts(v).Should().Contain("k");

        v.CollapseAll();
        Pump(v);
        v.GetVisualDescendants().OfType<ToggleButton>().Where(t => t.Classes.Contains("toggle")).Should().OnlyContain(t => t.IsChecked == false);
    }

    [AvaloniaFact]
    public void A_toggle_opens_and_closes_its_node()
    {
        var v = Show(Viewer());
        v.SetData(A, B);
        Pump(v);

        Rows(v, "equal").Single(r => Texts(r).Contains("Same")).Children.OfType<ToggleButton>().Single().IsChecked = true;
        Pump(v);

        Texts(v).Should().Contain("k");
    }

    [AvaloniaFact]
    public void A_side_that_does_not_parse_names_itself_and_there_is_no_result()
    {
        var v = Show(Viewer());
        v.SetData("{\"a\":", "{}");
        Pump(v);

        v.Result.Should().BeNull();
        Texts(v).Should().Contain(t => t != null && t.StartsWith("production: JSON parse error:"));
    }

    [AvaloniaFact]
    public void A_malformed_option_is_shown_not_thrown()
    {
        var v = Show(new JsonDiffViewer { Options = new JsonDiffOptions { Ignore = ["["] } });
        v.SetData("{}", "{}");
        Pump(v);

        v.Result.Should().BeNull();
        v.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Classes.Contains("error"));
    }

    [AvaloniaFact]
    public void Xml_mode_diffs_the_converted_trees_and_says_when_a_side_is_not_xml()
    {
        var v = Show(new JsonDiffViewer { Format = JsonDiffFormat.Xml });
        v.SetData("<R><Offices><Count>3</Count></Offices><Empty/></R>", "<R><Offices><Count>4</Count></Offices><Empty/><New>1</New></R>");
        Pump(v);

        v.Result!.Changes.Select(c => c.ToString()).Should().Equal("changed Offices.Count", "added New");
        Find<TextBlock>(v, "title").Text.Should().Be("XML diff");

        v.SetData("{\"a\":1}", "<!DOCTYPE html><html><body>x");
        Pump(v);
        v.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("error")).Select(t => t.Text)
            .Should().Equal("A: not XML", "B: not XML");
    }

    [AvaloniaFact]
    public void Switching_format_re_reads_the_bodies()
    {
        var v = Show(new JsonDiffViewer());
        v.SetData("<r><a>1</a></r>", "<r><a>2</a></r>");
        Pump(v);
        v.Result.Should().BeNull("without Format = Xml a string is JSON");

        v.Format = JsonDiffFormat.Xml;
        Pump(v);
        v.Result!.Changes.Select(c => c.ToString()).Should().Equal("changed a");
    }

    [AvaloniaFact]
    public void Changing_the_options_compares_again()
    {
        var v = Show(new JsonDiffViewer());
        v.SetData("""{"a":1,"Url":1}""", """{"a":1,"Url":2}""");
        Pump(v);
        v.Result!.Equal.Should().BeFalse();

        v.Options = new JsonDiffOptions { Ignore = ["Url"] };
        Pump(v);
        v.Result!.Equal.Should().BeTrue();
        Texts(Find<StackPanel>(v, "summary")).Should().Contain("No differences");
    }

    [AvaloniaFact]
    public void Equal_bodies_with_only_changes_say_so()
    {
        var v = Show(new JsonDiffViewer { OnlyChanges = true });
        v.SetData("""{"a":[1]}""", """{"a":[1]}""");
        Pump(v);

        Texts(Find<StackPanel>(v, "body")).Should().Contain("No differences");
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("Neon")]
    [InlineData("Finstat")]
    public void Capture_json_diff_screenshot(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme switch
        {
            "Dark" => Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Dark,
            "Neon" => Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Neon,
            "Finstat" => Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Finstat,
            _ => Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light,
        };
        try
        {
            var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS") ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
            Directory.CreateDirectory(dir);

            var viewer = Viewer();
            viewer.Margin = new Thickness(16);
            var page = new Border { Child = viewer };
            page.Bind(Border.BackgroundProperty, page.GetResourceObservable("BBgBrush"));
            var window = new Window { Content = page, Width = 760, Height = 420 };
            window.Show();
            viewer.SetData(A, B);
            window.Measure(new Size(760, 420));
            window.Arrange(new Rect(0, 0, 760, 420));
            Dispatcher.UIThread.RunJobs();

            var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            frame?.Save(Path.Combine(dir, $"json-diff-{theme.ToLowerInvariant()}.png"));
            viewer.Result.Should().NotBeNull();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Birko.Xaml.Avalonia.Theming.BirkoThemeVariants.Light;
        }
    }

    [AvaloniaFact]
    public void Values_are_shown_as_json_with_non_ascii_kept_and_long_ones_cut()
    {
        JsonDiffViewer.Inline(System.Text.Json.Nodes.JsonNode.Parse("\"Výroba\"")).Should().Be("\"Výroba\"");
        JsonDiffViewer.Inline(null).Should().Be("null");
        var longText = JsonDiffViewer.Inline(System.Text.Json.Nodes.JsonValue.Create(new string('x', 300)));
        longText.Should().HaveLength(120).And.EndWith("…");
    }
}
