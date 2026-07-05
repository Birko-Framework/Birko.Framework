using System.Collections;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>
/// Recursive object / JSON viewer (the XAML port of <c>b-object-tree</c> + <c>b-json-viewer</c>):
/// renders <see cref="Source"/> (any object graph) or <see cref="Json"/> (a JSON string) as an
/// expandable tree over the restyled <c>TreeView</c>, with type-colored, monospaced values. Walks
/// <c>JsonNode</c>, dictionaries, enumerables and plain objects (public properties).
/// </summary>
public class ObjectTree : ContentControl
{
    public static readonly StyledProperty<object?> SourceProperty =
        AvaloniaProperty.Register<ObjectTree, object?>(nameof(Source));

    public static readonly StyledProperty<string?> JsonProperty =
        AvaloniaProperty.Register<ObjectTree, string?>(nameof(Json));

    static ObjectTree()
    {
        SourceProperty.Changed.AddClassHandler<ObjectTree>((t, _) => t.Rebuild());
        JsonProperty.Changed.AddClassHandler<ObjectTree>((t, _) => t.Rebuild());
    }

    /// <summary>An object graph to display.</summary>
    public object? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>A JSON string to parse and display (takes precedence over <see cref="Source"/>).</summary>
    public string? Json { get => GetValue(JsonProperty); set => SetValue(JsonProperty, value); }

    private void Rebuild()
    {
        object? root = !string.IsNullOrWhiteSpace(Json) ? SafeParse(Json!) : Source;
        var tree = new TreeView();
        tree.Bind(FontFamilyProperty, tree.GetResourceObservable("BFontMono"));

        if (root is null)
        {
            tree.Items.Add(Leaf(null, null));
        }
        else if (IsContainer(root))
        {
            foreach (var (name, value) in Enumerate(root))
                tree.Items.Add(BuildNode(name, value, 1));
        }
        else
        {
            tree.Items.Add(Leaf(null, root));
        }

        Content = tree;
    }

    private static object? SafeParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch { return json; } // not valid JSON → show the raw string as a leaf
    }

    private TreeViewItem BuildNode(string name, object? value, int depth)
    {
        if (!IsContainer(value))
            return Leaf(name, value);

        var item = new TreeViewItem { Header = Row(name, Summary(value), "BTextMutedBrush"), IsExpanded = depth < 2 };
        foreach (var (childName, childValue) in Enumerate(value!))
            item.Items.Add(BuildNode(childName, childValue, depth + 1));
        return item;
    }

    private TreeViewItem Leaf(string? name, object? value) =>
        new() { Header = Row(name, LeafText(value), ValueTokenFor(value)) };

    // ── Header row: "name: value", name secondary, value type-colored ──
    private Control Row(string? name, string valueText, string valueTokenKey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (name is not null)
        {
            var key = new TextBlock { Text = name + ":" };
            key.Bind(TextBlock.ForegroundProperty, key.GetResourceObservable("BTextSecondaryBrush"));
            panel.Children.Add(key);
        }
        var val = new TextBlock { Text = valueText };
        val.Bind(TextBlock.ForegroundProperty, val.GetResourceObservable(valueTokenKey));
        panel.Children.Add(val);
        return panel;
    }

    // ── Type helpers ──
    private static bool IsContainer(object? value) => value switch
    {
        null => false,
        string => false,
        JsonValue => false,
        JsonObject => true,
        JsonArray => true,
        IDictionary => true,
        IEnumerable => true,
        _ => HasProperties(value),
    };

    private static bool HasProperties(object value) =>
        !value.GetType().IsPrimitive
        && value is not decimal and not DateTime and not Guid and not Enum
        && value.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length > 0;

    private static IEnumerable<(string Name, object? Value)> Enumerate(object value)
    {
        switch (value)
        {
            case JsonObject jo:
                foreach (var kv in jo) yield return (kv.Key, kv.Value);
                break;
            case JsonArray ja:
                for (int i = 0; i < ja.Count; i++) yield return ($"[{i}]", ja[i]);
                break;
            case IDictionary dict:
                foreach (DictionaryEntry e in dict) yield return (e.Key?.ToString() ?? "", e.Value);
                break;
            case IEnumerable seq:
                int idx = 0;
                foreach (var item in seq) yield return ($"[{idx++}]", item);
                break;
            default:
                foreach (var p in value.GetType().GetProperties(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    object? v;
                    try { v = p.GetValue(value); } catch { continue; }
                    yield return (p.Name, v);
                }
                break;
        }
    }

    private static string Summary(object? value) => value switch
    {
        JsonArray ja => $"[{ja.Count}]",
        IDictionary d => $"{{{d.Count}}}",
        JsonObject jo => $"{{{jo.Count}}}",
        ICollection c => $"[{c.Count}]",
        IEnumerable e => $"[{e.Cast<object?>().Count()}]",
        _ => "{…}",
    };

    private static string LeafText(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        JsonValue jv => JsonValueText(jv),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string JsonValueText(JsonValue jv)
    {
        if (jv.TryGetValue<string>(out var s)) return $"\"{s}\"";
        if (jv.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        return jv.ToJsonString();
    }

    private static string ValueTokenFor(object? value) => value switch
    {
        null => "BTextMutedBrush",
        string => "BColorSuccessBrush",
        bool => "BColorWarningBrush",
        JsonValue jv when jv.TryGetValue<string>(out _) => "BColorSuccessBrush",
        JsonValue jv when jv.TryGetValue<bool>(out _) => "BColorWarningBrush",
        JsonValue => "BColorPrimaryBrush",
        _ => "BColorPrimaryBrush", // numbers / dates
    };
}
