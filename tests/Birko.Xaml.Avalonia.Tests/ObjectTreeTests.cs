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

/// <summary>STORY-035: the object / JSON viewer (b-object-tree + b-json-viewer).</summary>
public class ObjectTreeTests
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

    /// <summary>Texts of a node's OWN header row only — <see cref="Texts"/> walks the whole subtree,
    /// so a parent would also match its descendants' values.</summary>
    private static IEnumerable<string?> HeaderTexts(TreeViewItem item) =>
        item.Header is Control header ? Texts(header) : Enumerable.Empty<string?>();

    [AvaloniaFact]
    public void Renders_json_as_a_tree()
    {
        var tree = Show(new ObjectTree { Json = "{\"name\":\"Ada\",\"age\":36,\"tags\":[\"x\",\"y\"]}" });
        tree.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
        Texts(tree).Should().Contain("name:");
        Texts(tree).Should().Contain("\"Ada\"");
        Texts(tree).Should().Contain("tags:");
    }

    [AvaloniaFact]
    public void Renders_object_properties()
    {
        var tree = Show(new ObjectTree { Source = new Contact { Name = "Grace", Active = true } });
        Texts(tree).Should().Contain("Name:");
        Texts(tree).Should().Contain("\"Grace\"");
        Texts(tree).Should().Contain("Active:");
    }

    [AvaloniaFact]
    public void Invalid_json_falls_back_to_raw_string()
    {
        var tree = Show(new ObjectTree { Json = "{ not valid" });
        // Doesn't throw; shows the raw text as a leaf.
        tree.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Selecting_a_node_exposes_its_value_and_path()
    {
        var tree = Show(new ObjectTree { Json = "{\"user\":{\"name\":\"Ada\"},\"roles\":[\"admin\"]}" });

        tree.SelectedValue.Should().BeNull("nothing is selected yet");
        tree.SelectedPath.Should().BeNull();

        var raised = 0;
        tree.SelectionChanged += (_, _) => raised++;

        // Drill to roles[0] — the deepest leaf under the second root node.
        var items = tree.GetVisualDescendants().OfType<TreeViewItem>().ToList();
        var leaf = items.Single(i => HeaderTexts(i).Contains("\"admin\""));
        leaf.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        tree.SelectedPath.Should().Be("roles[0]");
        tree.SelectedValue?.ToString().Should().Be("admin");
        raised.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_selected_null_node_is_distinguishable_from_no_selection()
    {
        var tree = Show(new ObjectTree { Json = "{\"lastLogin\":null}" });

        var leaf = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Single(i => HeaderTexts(i).Contains("lastLogin:"));
        leaf.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        // Both are null-valued, so the path is what says "a node IS selected".
        tree.SelectedValue.Should().BeNull();
        tree.SelectedPath.Should().Be("lastLogin");
    }

    [AvaloniaFact]
    public void Rebuilding_clears_the_selection()
    {
        var tree = Show(new ObjectTree { Source = new Contact { Name = "Grace", Active = true } });
        tree.GetVisualDescendants().OfType<TreeViewItem>()
            .Single(i => HeaderTexts(i).Contains("Name:")).IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        tree.SelectedPath.Should().Be("Name");

        tree.Source = new Contact { Name = "Ada", Active = false };
        Dispatcher.UIThread.RunJobs();

        tree.SelectedPath.Should().BeNull("the nodes the selection pointed at no longer exist");
        tree.SelectedValue.Should().BeNull();
    }

    // ── TASK-652: HideNullMembers ──

    [AvaloniaFact]
    public void A_null_property_is_shown_by_default_and_hidden_with_HideNullMembers()
    {
        var shown = Show(new ObjectTree { Source = new Contact { Name = "Grace", Role = null } });
        Texts(shown).Should().Contain("Role:");

        var hidden = Show(new ObjectTree { HideNullMembers = true, Source = new Contact { Name = "Grace", Role = null } });
        Texts(hidden).Should().NotContain("Role:");
        Texts(hidden).Should().Contain("Name:");
        Texts(hidden).Should().Contain("Active:", "a non-null value type is not a null member");
    }

    [AvaloniaFact]
    public void A_null_json_field_is_hidden_and_the_shown_ones_select_as_before()
    {
        var tree = Show(new ObjectTree { HideNullMembers = true, Json = "{\"a\":1,\"b\":null}" });

        Texts(tree).Should().NotContain("b:");
        var a = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(i => HeaderTexts(i).Contains("a:"));
        a.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        tree.SelectedPath.Should().Be("a");
        tree.SelectedValue?.ToString().Should().Be("1");
    }

    [AvaloniaFact]
    public void A_null_dictionary_entry_is_hidden()
    {
        var tree = Show(new ObjectTree
        {
            HideNullMembers = true,
            Source = new Dictionary<string, object?> { ["kept"] = "x", ["gone"] = null },
        });

        Texts(tree).Should().Contain("kept:");
        Texts(tree).Should().NotContain("gone:");
    }

    [AvaloniaFact]
    public void A_container_summary_counts_only_the_shown_members()
    {
        const string json = "{\"o\":{\"a\":1,\"b\":null}}";
        TreeViewItem Header(ObjectTree t) =>
            t.GetVisualDescendants().OfType<TreeViewItem>().Single(i => HeaderTexts(i).Contains("o:"));

        HeaderTexts(Header(Show(new ObjectTree { Json = json }))).Should().Contain("{2}");
        HeaderTexts(Header(Show(new ObjectTree { HideNullMembers = true, Json = json }))).Should().Contain("{1}");
    }

    [AvaloniaFact]
    public void A_null_array_item_keeps_its_index_with_HideNullMembers()
    {
        var tree = Show(new ObjectTree { HideNullMembers = true, Json = "{\"items\":[1,null,3]}" });
        var items = tree.GetVisualDescendants().OfType<TreeViewItem>().ToList();

        HeaderTexts(items.Single(i => HeaderTexts(i).Contains("items:"))).Should().Contain("[3]");
        var nullItem = items.Single(i => HeaderTexts(i).Contains("[1]:"));
        HeaderTexts(nullItem).Should().Contain("null");

        items.Single(i => HeaderTexts(i).Contains("[2]:")).IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        tree.SelectedPath.Should().Be("items[2]", "hiding an item would have shifted this path");
    }

    [AvaloniaFact]
    public void Toggling_HideNullMembers_rebuilds_a_built_tree()
    {
        var tree = Show(new ObjectTree { Source = new Contact { Name = "Grace", Role = null } });
        Texts(tree).Should().Contain("Role:");

        tree.HideNullMembers = true;
        Dispatcher.UIThread.RunJobs();
        Texts(tree).Should().NotContain("Role:");

        tree.HideNullMembers = false;
        Dispatcher.UIThread.RunJobs();
        Texts(tree).Should().Contain("Role:");
    }

    [AvaloniaFact]
    public void A_null_source_still_shows_the_null_leaf_with_HideNullMembers()
    {
        var tree = Show(new ObjectTree { HideNullMembers = true, Source = new Contact { Name = "Grace" } });
        tree.Source = null;
        Dispatcher.UIThread.RunJobs();

        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().Should().ContainSingle().Subject;
        HeaderTexts(item).Should().Equal("null");
    }

    [AvaloniaFact]
    public void Capture_object_tree_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        var viewer = new ObjectTree
        {
            Margin = new Thickness(16),
            Json = "{\"user\":{\"name\":\"Ada Lovelace\",\"age\":36,\"active\":true},"
                 + "\"roles\":[\"admin\",\"editor\"],\"lastLogin\":null}",
        };
        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFFFFF")), Child = viewer };
        var window = new Window { Content = page, Width = 380, Height = 340 };
        window.Show();
        window.Measure(new Size(380, 340));
        window.Arrange(new Rect(0, 0, 380, 340));
        Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "object-tree.png"));
        viewer.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
    }
}
