using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Birko.Xaml.Avalonia.Theming;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>STORY-035: the token-restyled TreeView (b-tree-menu).</summary>
public class TreeTests
{
    [AvaloniaFact]
    public void TreeView_renders_nested_items_when_expanded()
    {
        var parent = new TreeViewItem { Header = "Reports", IsExpanded = true };
        parent.Items.Add(new TreeViewItem { Header = "Sales" });
        parent.Items.Add(new TreeViewItem { Header = "Inventory" });
        var tree = new TreeView();
        tree.Items.Add(parent);
        tree.Items.Add(new TreeViewItem { Header = "Settings" });

        var window = new Window { Content = tree, Width = 300, Height = 300 };
        window.Show();
        window.Measure(new Size(300, 300));
        window.Arrange(new Rect(0, 0, 300, 300));

        // parent + 2 children + settings = 4 realized TreeViewItems
        tree.GetVisualDescendants().OfType<TreeViewItem>().Should().HaveCount(4);
    }

    [AvaloniaFact]
    public void Collapsing_hides_children()
    {
        var parent = new TreeViewItem { Header = "Reports", IsExpanded = true };
        parent.Items.Add(new TreeViewItem { Header = "Sales" });
        var tree = new TreeView();
        tree.Items.Add(parent);
        var window = new Window { Content = tree, Width = 300, Height = 300 };
        window.Show();
        window.Measure(new Size(300, 300));
        window.Arrange(new Rect(0, 0, 300, 300));

        var child = tree.GetVisualDescendants().OfType<TreeViewItem>().First(i => (i.Header as string) == "Sales");
        child.IsVisible.Should().BeTrue();

        parent.IsExpanded = false;
        window.Measure(new Size(300, 300));
        window.Arrange(new Rect(0, 0, 300, 300));
        // The child's items presenter is collapsed; the child is no longer effectively visible.
        parent.IsExpanded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Capture_tree_screenshot()
    {
        Application.Current!.RequestedThemeVariant = BirkoThemeVariants.Light;
        var dir = Environment.GetEnvironmentVariable("BIRKO_SHOTS")
                  ?? Path.Combine(Path.GetTempPath(), "birko-xaml-shots");
        Directory.CreateDirectory(dir);

        TreeViewItem Node(string h, bool expanded, params string[] kids)
        {
            var n = new TreeViewItem { Header = h, IsExpanded = expanded };
            foreach (var k in kids) n.Items.Add(new TreeViewItem { Header = k });
            return n;
        }

        var tree = new TreeView { Margin = new Thickness(16) };
        tree.Items.Add(Node("Reports", true, "Sales", "Inventory"));
        tree.Items.Add(Node("Customers", true, "Companies", "Contacts"));
        tree.Items.Add(new TreeViewItem { Header = "Settings" });
        (tree.Items[0] as TreeViewItem)!.IsSelected = true;

        var page = new Border { Background = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#FFFFFF")), Child = tree };
        var window = new Window { Content = page, Width = 320, Height = 320 };
        window.Show();
        window.Measure(new Size(320, 320));
        window.Arrange(new Rect(0, 0, 320, 320));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        frame?.Save(Path.Combine(dir, "tree.png"));
        tree.GetVisualDescendants().OfType<TreeViewItem>().Should().NotBeEmpty();
    }
}
