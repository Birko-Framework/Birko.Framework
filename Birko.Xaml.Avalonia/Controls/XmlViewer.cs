using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// XML viewer (the XAML port of <c>b-xml-viewer</c>): parses <see cref="Xml"/> and renders the
/// element tree over the restyled <c>TreeView</c> — elements as nodes, <c>@attributes</c> and text
/// as leaves, token-colored and monospaced. Invalid XML falls back to a raw-string leaf.
/// </summary>
public class XmlViewer : ContentControl
{
    public static readonly StyledProperty<string?> XmlProperty =
        AvaloniaProperty.Register<XmlViewer, string?>(nameof(Xml));

    static XmlViewer() => XmlProperty.Changed.AddClassHandler<XmlViewer>((v, _) => v.Rebuild());

    public string? Xml { get => GetValue(XmlProperty); set => SetValue(XmlProperty, value); }

    private void Rebuild()
    {
        var tree = new TreeView();
        tree.Bind(FontFamilyProperty, tree.GetResourceObservable("BFontMono"));

        if (string.IsNullOrWhiteSpace(Xml))
        {
            Content = tree;
            return;
        }

        try
        {
            var doc = XDocument.Parse(Xml);
            if (doc.Root is { } root)
                tree.Items.Add(BuildElement(root, 1));
        }
        catch
        {
            tree.Items.Add(new TreeViewItem { Header = Row(null, Xml!, "BTextMutedBrush") });
        }

        Content = tree;
    }

    private TreeViewItem BuildElement(XElement element, int depth)
    {
        var attrs = element.Attributes().ToList();
        var childElements = element.Elements().ToList();
        string? innerText = childElements.Count == 0 ? element.Value.Trim() : null;
        bool hasChildren = attrs.Count > 0 || childElements.Count > 0;

        var item = new TreeViewItem { IsExpanded = depth < 3 };

        if (!hasChildren)
        {
            // Leaf element: <tag> "text"
            item.Header = TagRow(element.Name.LocalName, string.IsNullOrEmpty(innerText) ? null : $"\"{innerText}\"");
            return item;
        }

        item.Header = TagRow(element.Name.LocalName, null);
        foreach (var attr in attrs)
            item.Items.Add(new TreeViewItem { Header = Row("@" + attr.Name.LocalName, $"\"{attr.Value}\"", "BColorSuccessBrush") });
        if (!string.IsNullOrEmpty(innerText))
            item.Items.Add(new TreeViewItem { Header = Row("#text", $"\"{innerText}\"", "BColorSuccessBrush") });
        foreach (var child in childElements)
            item.Items.Add(BuildElement(child, depth + 1));
        return item;
    }

    // "<tag>" in the element color, optional trailing value.
    private Control TagRow(string tag, string? value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var tagBlock = new TextBlock { Text = $"<{tag}>" };
        tagBlock.Bind(TextBlock.ForegroundProperty, tagBlock.GetResourceObservable("BColorPrimaryBrush"));
        panel.Children.Add(tagBlock);
        if (value is not null)
        {
            var val = new TextBlock { Text = value };
            val.Bind(TextBlock.ForegroundProperty, val.GetResourceObservable("BColorSuccessBrush"));
            panel.Children.Add(val);
        }
        return panel;
    }

    private Control Row(string? name, string valueText, string valueTokenKey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (name is not null)
        {
            var key = new TextBlock { Text = name + " =" };
            key.Bind(TextBlock.ForegroundProperty, key.GetResourceObservable("BTextSecondaryBrush"));
            panel.Children.Add(key);
        }
        var val = new TextBlock { Text = valueText };
        val.Bind(TextBlock.ForegroundProperty, val.GetResourceObservable(valueTokenKey));
        panel.Children.Add(val);
        return panel;
    }
}
