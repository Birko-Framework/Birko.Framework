using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Birko.Xaml.Core.Diff;
using Birko.Xaml.Core.Localization;

namespace Birko.Xaml.Avalonia.Controls;

/// <summary>How <see cref="JsonDiffViewer"/> reads a string body.</summary>
public enum JsonDiffFormat { Json, Xml }

/// <summary>Which view <see cref="JsonDiffViewer"/> shows.</summary>
public enum JsonDiffView
{
    /// <summary>The merged tree of differences.</summary>
    Tree,

    /// <summary>Both bodies whole, side by side, with the changed lines tinted (TASK-773).</summary>
    SideBySide,
}

/// <summary>A side-by-side line's tint: what happened to the path it shows, on its own side.</summary>
public enum DiffLineMark { None, Added, Removed, Changed, Ignored }

/// <summary>One line of a side-by-side pane: its text and its tint.</summary>
public sealed record DiffLine(string Text, DiffLineMark Mark);

/// <summary>
/// Compares two bodies — side A ("old") and side B ("new") — and shows the differences as a tree (the XAML port of
/// <c>b-json-diff</c>'s tree view). The comparison is <see cref="JsonDiff"/> from Birko.Xaml.Core, so its paths, statuses
/// and counts are the web engine's. With <see cref="Format"/> = <see cref="JsonDiffFormat.Xml"/> a body is read with
/// <see cref="XmlTree"/>. Containers holding a difference start open; <see cref="OnlyChanges"/> hides equal and ignored
/// branches. A side that does not parse shows its error instead of a tree.
/// </summary>
/// <remarks>
/// Rows carry the style class <c>st-{status}</c> (<c>st-added</c>, <c>st-removed</c>, <c>st-changed</c>,
/// <c>st-modified</c>, <c>st-equal</c>, <c>st-ignored</c>) for consumer styling, and an automation name that states the
/// status as text, since colour alone carries it on screen. Strings are localized under <c>bxaml.jsonDiff.*</c>, with the
/// web's English as the fallback.
/// </remarks>
public class JsonDiffViewer : ContentControl
{
    /// <summary>Longest inline rendering of a value before it is cut, as on the web.</summary>
    private const int InlineMax = 120;

    private static readonly JsonSerializerOptions InlineJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static readonly StyledProperty<string?> RawAProperty = AvaloniaProperty.Register<JsonDiffViewer, string?>(nameof(RawA));
    public static readonly StyledProperty<string?> RawBProperty = AvaloniaProperty.Register<JsonDiffViewer, string?>(nameof(RawB));
    public static readonly StyledProperty<JsonDiffFormat> FormatProperty = AvaloniaProperty.Register<JsonDiffViewer, JsonDiffFormat>(nameof(Format));
    public static readonly StyledProperty<string?> LabelAProperty = AvaloniaProperty.Register<JsonDiffViewer, string?>(nameof(LabelA));
    public static readonly StyledProperty<string?> LabelBProperty = AvaloniaProperty.Register<JsonDiffViewer, string?>(nameof(LabelB));
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<JsonDiffViewer, string?>(nameof(Title));
    public static readonly StyledProperty<bool> OnlyChangesProperty = AvaloniaProperty.Register<JsonDiffViewer, bool>(nameof(OnlyChanges));
    public static readonly StyledProperty<JsonDiffView> ViewProperty = AvaloniaProperty.Register<JsonDiffViewer, JsonDiffView>(nameof(View));
    public static readonly StyledProperty<JsonDiffOptions?> OptionsProperty = AvaloniaProperty.Register<JsonDiffViewer, JsonDiffOptions?>(nameof(Options));
    public static readonly StyledProperty<XmlTreeOptions?> XmlOptionsProperty = AvaloniaProperty.Register<JsonDiffViewer, XmlTreeOptions?>(nameof(XmlOptions));

    public static readonly DirectProperty<JsonDiffViewer, JsonDiffResult?> ResultProperty =
        AvaloniaProperty.RegisterDirect<JsonDiffViewer, JsonDiffResult?>(nameof(Result), v => v.Result);

    static JsonDiffViewer()
    {
        foreach (var p in new AvaloniaProperty[] { RawAProperty, RawBProperty, FormatProperty, OptionsProperty, XmlOptionsProperty })
            p.Changed.AddClassHandler<JsonDiffViewer>((v, _) => v.Recompute());
        foreach (var p in new AvaloniaProperty[] { LabelAProperty, LabelBProperty, TitleProperty, OnlyChangesProperty, ViewProperty })
            p.Changed.AddClassHandler<JsonDiffViewer>((v, _) => v.Render());
    }

    public JsonDiffViewer() => Render();

    // Subscribed only while attached: I18n.Instance is process-wide and would otherwise keep every viewer alive.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        I18n.Instance.LocaleChanged += OnLocaleChanged;
        Render();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        I18n.Instance.LocaleChanged -= OnLocaleChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnLocaleChanged(object? sender, EventArgs e) => Render();

    /// <summary>Side A's body, as text: JSON, or XML with <see cref="Format"/> = Xml.</summary>
    public string? RawA { get => GetValue(RawAProperty); set => SetValue(RawAProperty, value); }

    /// <summary>Side B's body.</summary>
    public string? RawB { get => GetValue(RawBProperty); set => SetValue(RawBProperty, value); }

    /// <summary>How the bodies are read. A change re-reads both.</summary>
    public JsonDiffFormat Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    /// <summary>Side A's name in the legend and in errors (default "A").</summary>
    public string? LabelA { get => GetValue(LabelAProperty); set => SetValue(LabelAProperty, value); }

    /// <summary>Side B's name (default "B").</summary>
    public string? LabelB { get => GetValue(LabelBProperty); set => SetValue(LabelBProperty, value); }

    /// <summary>The header title (default "JSON diff" / "XML diff", following <see cref="Format"/>).</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Tree (default) or side by side. The toolbar switches it too.</summary>
    public JsonDiffView View { get => GetValue(ViewProperty); set => SetValue(ViewProperty, value); }

    /// <summary>Hides equal and ignored branches (tree view; the side-by-side view always shows both bodies whole, as on the web).</summary>
    public bool OnlyChanges { get => GetValue(OnlyChangesProperty); set => SetValue(OnlyChangesProperty, value); }

    /// <summary>The comparison's options (ignored paths, normalizers, array pairing). A malformed pattern is shown, not thrown.</summary>
    public JsonDiffOptions? Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }

    /// <summary>How an XML body is read.</summary>
    public XmlTreeOptions? XmlOptions { get => GetValue(XmlOptionsProperty); set => SetValue(XmlOptionsProperty, value); }

    private JsonDiffResult? _result;

    /// <summary>The last comparison, or <c>null</c> before both sides are set, when a side does not parse, or when an option is malformed.</summary>
    public JsonDiffResult? Result { get => _result; private set => SetAndRaise(ResultProperty, ref _result, value); }

    /// <summary>Raised after each comparison that produced a <see cref="Result"/>.</summary>
    public event EventHandler<JsonDiffResult>? DiffComputed;

    /// <summary>Sets both bodies and compares them once.</summary>
    public void SetData(string? rawA, string? rawB)
    {
        _batching = true;
        try
        {
            RawA = rawA;
            RawB = rawB;
        }
        finally
        {
            _batching = false;
        }
        Recompute();
    }

    /// <summary>Opens every container.</summary>
    public void ExpandAll()
    {
        if (Result is null) return;
        void Walk(JsonDiffNode n)
        {
            if (n.Children is not { Count: > 0 }) return;
            _expanded.Add(NodeKey(n));
            foreach (var c in n.Children) Walk(c);
        }
        Walk(Result.Tree);
        Render();
    }

    /// <summary>Closes every container.</summary>
    public void CollapseAll()
    {
        _expanded.Clear();
        Render();
    }

    private sealed record Side(JsonNode? Data, string? Error, bool NotXml, string Raw);

    private bool _batching;
    private Side? _a;
    private Side? _b;
    private string? _optionsError;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    /// <summary>
    /// Identity of a node for expansion state. The path alone is not unique below an array paired by key or unordered:
    /// a removed item is listed under its side-A index, which another row may hold on side B.
    /// </summary>
    private static string NodeKey(JsonDiffNode n) =>
        $"{(n.Status == JsonDiffStatus.Removed ? '-' : '+')}{n.Path}|{(n.OldSegments is null ? "" : JsonPath.Format(n.OldSegments))}";

    private Side? Read(string? raw)
    {
        if (raw is null) return null;
        if (Format == JsonDiffFormat.Xml)
            return XmlTree.TryToTree(raw, out var tree, XmlOptions) ? new Side(tree, null, false, raw) : new Side(null, "not XML", true, raw);
        try
        {
            return new Side(JsonNode.Parse(raw), null, false, raw);
        }
        catch (JsonException e)
        {
            return new Side(null, e.Message, false, raw);
        }
    }

    private void Recompute()
    {
        if (_batching) return;
        _a = Read(RawA);
        _b = Read(RawB);
        _optionsError = null;
        JsonDiffResult? result = null;
        if (_a is { Error: null } && _b is { Error: null })
        {
            try
            {
                result = JsonDiff.Diff(_a.Data, _b.Data, Options);
            }
            catch (FormatException e)
            {
                _optionsError = e.Message;
            }
        }
        Result = result;
        SeedExpansion();
        Render();
        if (result is not null) DiffComputed?.Invoke(this, result);
    }

    /// <summary>Containers holding a difference start open; everything else starts closed.</summary>
    private void SeedExpansion()
    {
        _expanded.Clear();
        if (Result is null) return;
        void Walk(JsonDiffNode n)
        {
            if (n.Status != JsonDiffStatus.Modified) return;
            _expanded.Add(NodeKey(n));
            if (n.Children is null) return;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(Result.Tree);
    }

    // ── Strings ──

    private static string L(string key, string fallback, IReadOnlyDictionary<string, object?>? args = null)
    {
        var text = I18n.Instance.Translate(key, args);
        if (text != key) return text;
        if (args is null) return fallback;
        foreach (var (k, v) in args) fallback = fallback.Replace("{" + k + "}", Convert.ToString(v, System.Globalization.CultureInfo.CurrentCulture));
        return fallback;
    }

    private string SideA => LabelA ?? L("bxaml.jsonDiff.sideA", "A");
    private string SideB => LabelB ?? L("bxaml.jsonDiff.sideB", "B");
    private string TitleText => Title ?? (Format == JsonDiffFormat.Xml ? L("bxaml.jsonDiff.titleXml", "XML diff") : L("bxaml.jsonDiff.title", "JSON diff"));

    private static string StatusText(JsonDiffStatus s) => s switch
    {
        JsonDiffStatus.Added => L("bxaml.jsonDiff.statusAdded", "added"),
        JsonDiffStatus.Removed => L("bxaml.jsonDiff.statusRemoved", "removed"),
        JsonDiffStatus.Changed => L("bxaml.jsonDiff.statusChanged", "changed"),
        JsonDiffStatus.Modified => L("bxaml.jsonDiff.statusModified", "contains changes"),
        JsonDiffStatus.Ignored => L("bxaml.jsonDiff.statusIgnored", "ignored"),
        _ => "",
    };

    // ── Rendering ──

    private void Render()
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        DockPanel.SetDock(actions, Dock.Right);
        var split = View == JsonDiffView.SideBySide;
        ToggleButton ViewToggle(string label, string cls, JsonDiffView view)
        {
            var t = Themed(new ToggleButton { Content = label, IsChecked = View == view, Classes = { cls } }, "BToolbarToggle");
            // A pressed view button stays pressed: clicking the current view does nothing, as on the web.
            t.IsCheckedChanged += (_, _) => { if (t.IsChecked == true) View = view; else if (View == view) t.IsChecked = true; };
            return t;
        }
        actions.Children.Add(ViewToggle(L("bxaml.jsonDiff.viewTree", "Tree"), "view-tree", JsonDiffView.Tree));
        actions.Children.Add(ViewToggle(L("bxaml.jsonDiff.viewSplit", "Side by side"), "view-split", JsonDiffView.SideBySide));
        if (!split)
        {
            var only = Themed(new ToggleButton { Content = L("bxaml.jsonDiff.onlyChanges", "Only changes"), IsChecked = OnlyChanges, Classes = { "only-changes" } }, "BToolbarToggle");
            only.IsCheckedChanged += (_, _) => OnlyChanges = only.IsChecked == true;
            var expand = Themed(new Button { Content = L("bxaml.common.expand", "Expand"), Classes = { "expand-all" } }, "BToolbarButton");
            expand.Click += (_, _) => ExpandAll();
            var collapse = Themed(new Button { Content = L("bxaml.common.collapse", "Collapse"), Classes = { "collapse-all" } }, "BToolbarButton");
            collapse.Click += (_, _) => CollapseAll();
            actions.Children.Add(only);
            actions.Children.Add(expand);
            actions.Children.Add(collapse);
        }
        header.Children.Add(actions);

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = TitleText, FontWeight = FontWeight.SemiBold, Classes = { "title" } };
        titleRow.Children.Add(title);
        titleRow.Children.Add(Summary());
        header.Children.Add(titleRow);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        if (split)
        {
            var splitView = SplitView();
            splitView.Bind(TextElement.FontFamilyProperty, splitView.GetResourceObservable("BFontMono"));
            root.Children.Add(splitView);
            Content = root;
            return;
        }
        var body = new StackPanel { Spacing = 4, Classes = { "body" } };
        body.Bind(TextElement.FontFamilyProperty, body.GetResourceObservable("BFontMono"));
        foreach (var c in BodyContent()) body.Children.Add(c);
        root.Children.Add(new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        Content = root;
    }

    // ── Side-by-side view (TASK-773) ──

    private Control SplitView()
    {
        if (_a is null || _b is null) return Message(L("bxaml.jsonDiff.noData", "Nothing to compare"));
        var (marksA, marksB) = Marks();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*"), Classes = { "split" } };
        var paneA = Pane(SideA, _a, marksA, "pane-a");
        var paneB = Pane(SideB, _b, marksB, "pane-b");
        Grid.SetColumn(paneB.Root, 2);
        grid.Children.Add(paneA.Root);
        grid.Children.Add(paneB.Root);
        SyncScroll(paneA.Scroller, paneB.Scroller);
        if (_optionsError is null) return grid;
        var withError = new DockPanel();
        var error = Error(_optionsError);
        DockPanel.SetDock(error, Dock.Top);
        withError.Children.Add(error);
        withError.Children.Add(grid);
        return withError;
    }

    /// <summary>The paths each side marks, as the web's <c>_renderSplit</c>: A by its own (side-A) paths, B by side B's.</summary>
    private (Dictionary<string, DiffLineMark> A, Dictionary<string, DiffLineMark> B) Marks()
    {
        var a = new Dictionary<string, DiffLineMark>(StringComparer.Ordinal);
        var b = new Dictionary<string, DiffLineMark>(StringComparer.Ordinal);
        if (Result is null) return (a, b);
        void Walk(JsonDiffNode n)
        {
            var pathA = n.OldSegments is null ? n.Path : JsonPath.Format(n.OldSegments);
            if (n.Status is JsonDiffStatus.Removed or JsonDiffStatus.Changed or JsonDiffStatus.Ignored && n.HasOld)
                a[pathA] = n.Status switch { JsonDiffStatus.Ignored => DiffLineMark.Ignored, JsonDiffStatus.Changed => DiffLineMark.Changed, _ => DiffLineMark.Removed };
            if (n.Status is JsonDiffStatus.Added or JsonDiffStatus.Changed or JsonDiffStatus.Ignored && n.HasNew)
                b[n.Path] = n.Status switch { JsonDiffStatus.Ignored => DiffLineMark.Ignored, JsonDiffStatus.Changed => DiffLineMark.Changed, _ => DiffLineMark.Added };
            if (n.Children is null) return;
            foreach (var c in n.Children) Walk(c);
        }
        Walk(Result.Tree);
        return (a, b);
    }

    private sealed record PaneParts(Control Root, ScrollViewer Scroller);

    private PaneParts Pane(string label, Side side, Dictionary<string, DiffLineMark> marks, string cls)
    {
        var heading = Text(label.ToUpperInvariant(), "BTextMutedBrush", "pane-label");
        heading.FontSize = 11;
        heading.FontWeight = FontWeight.SemiBold;
        heading.Margin = new Thickness(0, 0, 0, 4);
        var top = new StackPanel();
        top.Children.Add(heading);
        if (side.Error is not null) top.Children.Add(Error(ParseError(label, side)));

        IReadOnlyList<DiffLine> lines = side.Error is not null
            ? side.Raw.Split('\n').Select(l => new DiffLine(l.TrimEnd('\r'), DiffLineMark.None)).ToList()
            : Format == JsonDiffFormat.Xml ? XmlLines(side.Raw, marks) : JsonLines(side.Data, marks);

        // Virtualized: only the lines in view are built, so a long body stays cheap to show and to scroll.
        var items = new ItemsControl
        {
            ItemsSource = lines,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<DiffLine>((line, _) => LineView(line), supportsRecycling: false),
            Classes = { "lines" },
        };
        var scroller = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Classes = { "pane-scroll" },
        };
        var root = new DockPanel { Classes = { "pane", cls } };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(scroller);
        AutomationProperties.SetName(root, label);
        return new PaneParts(root, scroller);
    }

    private static Control LineView(DiffLine? line)
    {
        if (line is null) return new Control();
        var text = Text(line.Text.Length == 0 ? " " : line.Text, "BTextBrush");
        var border = new Border { Child = text, Padding = new Thickness(4, 0), Classes = { "line" } };
        var tint = line.Mark switch
        {
            DiffLineMark.Added => "BColorSuccessAlphaBgBrush",
            DiffLineMark.Removed => "BColorDangerAlphaBgBrush",
            DiffLineMark.Changed => "BColorWarningAlphaBgBrush",
            _ => null,
        };
        if (line.Mark != DiffLineMark.None) border.Classes.Add("mk-" + line.Mark.ToString().ToLowerInvariant());
        if (tint is not null) border.Bind(Border.BackgroundProperty, border.GetResourceObservable(tint));
        if (line.Mark == DiffLineMark.Ignored) border.Opacity = 0.6;
        return border;
    }

    /// <summary>Scrolling one pane scrolls the other — the desktop addition to the web view (FEATURE-020 D7).</summary>
    private static void SyncScroll(ScrollViewer a, ScrollViewer b)
    {
        var busy = false;
        void Follow(ScrollViewer from, ScrollViewer to)
        {
            if (busy) return;
            busy = true;
            try { to.Offset = from.Offset; }
            finally { busy = false; }
        }
        a.ScrollChanged += (_, _) => Follow(a, b);
        b.ScrollChanged += (_, _) => Follow(b, a);
    }

    /// <summary>
    /// A body pretty-printed one line per value, two spaces per level, each line marked by the path it belongs to or the
    /// nearest marked container around it — the web's <c>_prettyLines</c>. Keys follow the engine's order.
    /// </summary>
    public static IReadOnlyList<DiffLine> JsonLines(JsonNode? value, IReadOnlyDictionary<string, DiffLineMark> marks)
    {
        var o = new List<DiffLine>();
        void Walk(JsonNode? v, List<JsonPathSegment> segs, string indent, string prefix, string comma, DiffLineMark inherited)
        {
            var mark = marks.TryGetValue(JsonPath.Format(segs), out var m) ? m : inherited;
            if (v is JsonObject or JsonArray)
            {
                var entries = v is JsonArray arr
                    ? arr.Select((item, i) => ((JsonPathSegment)i, item, "")).ToList()
                    : JsonDiff.OwnKeys((JsonObject)v).Select(k => ((JsonPathSegment)k, v[k], JsonSerializer.Serialize(k, InlineJson) + ": ")).ToList();
                var (open, close) = v is JsonArray ? ("[", "]") : ("{", "}");
                if (entries.Count == 0) { o.Add(new DiffLine($"{indent}{prefix}{open}{close}{comma}", mark)); return; }
                o.Add(new DiffLine($"{indent}{prefix}{open}", mark));
                for (var i = 0; i < entries.Count; i++)
                {
                    var (k, item, keyText) = entries[i];
                    segs.Add(k);
                    Walk(item, segs, indent + "  ", keyText, i < entries.Count - 1 ? "," : "", mark);
                    segs.RemoveAt(segs.Count - 1);
                }
                o.Add(new DiffLine($"{indent}{close}{comma}", mark));
                return;
            }
            o.Add(new DiffLine($"{indent}{prefix}{(v is null ? "null" : v.ToJsonString(InlineJson))}{comma}", mark));
        }
        Walk(value, [], "", "", "", DiffLineMark.None);
        return o;
    }

    /// <summary>The original XML, pretty-printed by <see cref="XmlTree.ToLines"/>, each line taking the mark of the nearest marked path it shows — the web's <c>_xmlLines</c>.</summary>
    private IReadOnlyList<DiffLine> XmlLines(string xml, IReadOnlyDictionary<string, DiffLineMark> marks)
    {
        var lines = XmlTree.ToLines(xml, XmlOptions) ?? [];
        DiffLineMark MarkOf(IReadOnlyList<JsonPathSegment> segs)
        {
            for (var n = segs.Count; n >= 0; n--)
                if (marks.TryGetValue(JsonPath.Format(segs.Take(n)), out var m)) return m;
            return DiffLineMark.None;
        }
        return lines.Select(l => new DiffLine(l.Text, l.Paths.Select(MarkOf).FirstOrDefault(m => m != DiffLineMark.None))).ToList();
    }

    private Control Summary()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "summary" }, VerticalAlignment = VerticalAlignment.Center };
        if (Result is null) return panel;
        if (Result.Equal)
        {
            panel.Children.Add(Text(L("bxaml.jsonDiff.noDifferences", "No differences"), "BTextMutedBrush"));
            return panel;
        }
        var s = Result.Summary;
        panel.Children.Add(Text(L("bxaml.jsonDiff.countAdded", "{count} added", Count(s.Added)), "BColorSuccessTextBrush", "n-added"));
        panel.Children.Add(Text(", ", "BTextMutedBrush"));
        panel.Children.Add(Text(L("bxaml.jsonDiff.countRemoved", "{count} removed", Count(s.Removed)), "BColorDangerTextBrush", "n-removed"));
        panel.Children.Add(Text(", ", "BTextMutedBrush"));
        panel.Children.Add(Text(L("bxaml.jsonDiff.countChanged", "{count} changed", Count(s.Changed)), "BColorWarningTextBrush", "n-changed"));
        AutomationProperties.SetLiveSetting(panel, AutomationLiveSetting.Polite);
        return panel;
    }

    private static Dictionary<string, object?> Count(int n) => new() { ["count"] = n };

    private IEnumerable<Control> BodyContent()
    {
        if (Problem() is { } problem)
        {
            foreach (var p in problem) yield return p;
            yield break;
        }
        var result = Result!;
        yield return Legend();
        if (OnlyChanges && result.Equal)
        {
            yield return Message(L("bxaml.jsonDiff.noDifferences", "No differences"));
            yield break;
        }
        var tree = result.Tree;
        // The root container is not drawn as a row: its children are the top level.
        var rootIsContainer = tree.Children is not null && tree.Type != JsonNodeType.Value
            && tree.Status is not (JsonDiffStatus.Changed or JsonDiffStatus.Ignored);
        var rows = new List<TreeRow>();
        if (rootIsContainer) AddChildRows(tree, 0, OnlyChanges, rows);
        else AddNodeRows(tree, 0, OnlyChanges, rows);
        // Virtualized over the flattened visible rows: an open array of thousands of records builds only the rows in
        // view (TASK-773 measured 4.1 s for 3 000 records when every row was built up front).
        var list = new ItemsControl
        {
            ItemsSource = rows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<TreeRow>((row, _) => RowView(row), supportsRecycling: false),
            Classes = { "tree" },
        };
        AutomationProperties.SetName(list, TitleText);
        yield return list;
    }

    /// <summary>A visible tree row: a node at a depth, or (no node) the "(empty)" note under an open container.</summary>
    private sealed record TreeRow(JsonDiffNode? Node, int Depth);

    private List<Control>? Problem()
    {
        if (_a is null || _b is null) return [Message(L("bxaml.jsonDiff.noData", "Nothing to compare"))];
        var errors = new List<Control>();
        if (_a.Error is not null) errors.Add(Error(ParseError(SideA, _a)));
        if (_b.Error is not null) errors.Add(Error(ParseError(SideB, _b)));
        if (_optionsError is not null) errors.Add(Error(_optionsError));
        return errors.Count > 0 ? errors : null;
    }

    private static string ParseError(string side, Side s) => s.NotXml
        ? L("bxaml.jsonDiff.notXml", "{side}: not XML", new Dictionary<string, object?> { ["side"] = side })
        : L("bxaml.jsonDiff.parseError", "{side}: JSON parse error: {message}", new Dictionary<string, object?> { ["side"] = side, ["message"] = s.Error });

    private Control Legend()
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Classes = { "legend" }, Margin = new Thickness(0, 0, 0, 4) };
        legend.Children.Add(Chip("− " + SideA, removed: true));
        legend.Children.Add(Chip("+ " + SideB, removed: false));
        return legend;
    }

    private void AddChildRows(JsonDiffNode node, int depth, bool onlyChanges, List<TreeRow> rows)
    {
        var any = false;
        foreach (var c in node.Children ?? [])
        {
            if (onlyChanges && c.Status is JsonDiffStatus.Equal or JsonDiffStatus.Ignored) continue;
            any = true;
            AddNodeRows(c, depth, onlyChanges, rows);
        }
        if (!any) rows.Add(new TreeRow(null, depth));
    }

    private static bool IsExpandable(JsonDiffNode node) =>
        node.Type != JsonNodeType.Value && node.Status != JsonDiffStatus.Changed && node.Children is { Count: > 0 };

    private void AddNodeRows(JsonDiffNode node, int depth, bool onlyChanges, List<TreeRow> rows)
    {
        rows.Add(new TreeRow(node, depth));
        if (!IsExpandable(node) || !_expanded.Contains(NodeKey(node))) return;
        // Inside a container that holds changes, "only changes" keeps filtering; inside anything else it shows all.
        AddChildRows(node, depth + 1, onlyChanges && node.Status == JsonDiffStatus.Modified, rows);
    }

    private Control RowView(TreeRow? r)
    {
        if (r is null) return new Control();
        var indent = new Thickness(r.Depth * 18, 0, 0, 0);
        if (r.Node is null)
        {
            var empty = Message(L("bxaml.jsonDiff.empty", "(empty)"));
            empty.Margin = new Thickness(r.Depth * 18 + 30, 0, 0, 0);
            return empty;
        }
        var row = NodeView(r.Node);
        row.Margin = indent;
        return row;
    }

    private StackPanel NodeView(JsonDiffNode node)
    {
        var expandable = IsExpandable(node);
        var key = NodeKey(node);
        var open = expandable && _expanded.Contains(key);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Classes = { "row", "st-" + node.Status.ToString().ToLowerInvariant() } };
        if (node.Status == JsonDiffStatus.Ignored) row.Opacity = 0.6;

        if (expandable)
        {
            var toggle = Themed(new ToggleButton { Content = open ? "▾" : "▸", IsChecked = open, Classes = { "toggle" } }, "BTreeNodeToggle");
            AutomationProperties.SetName(toggle, node.Key?.ToString() ?? L("bxaml.jsonDiff.root", "root"));
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (toggle.IsChecked == true) _expanded.Add(key); else _expanded.Remove(key);
                Render();
            };
            row.Children.Add(toggle);
        }
        else
        {
            row.Children.Add(new Control { Width = 16 }); // the toggle's slot, so keys line up
        }

        var (marker, markerBrush) = node.Status switch
        {
            JsonDiffStatus.Added => ("+", "BColorSuccessTextBrush"),
            JsonDiffStatus.Removed => ("−", "BColorDangerTextBrush"),
            JsonDiffStatus.Changed or JsonDiffStatus.Modified => ("~", "BColorWarningTextBrush"),
            _ => (" ", "BTextMutedBrush"),
        };
        var markerText = Text(marker, markerBrush, "marker");
        markerText.Width = 10;
        row.Children.Add(markerText);

        if (node.Key is { } k)
        {
            row.Children.Add(Text(k.IsIndex ? k.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : k.Key!,
                k.IsIndex ? "BTextMutedBrush" : "BColorInfoTextBrush", k.IsIndex ? "index" : "key"));
            row.Children.Add(Text(":", "BTextMutedBrush"));
        }
        foreach (var v in ValueViews(node, expandable)) row.Children.Add(v);

        var status = StatusText(node.Status);
        var name = string.Join(" ", new[] { status, node.Path.Length > 0 ? node.Path : null }.Where(s => !string.IsNullOrEmpty(s)));
        if (name.Length > 0) AutomationProperties.SetName(row, name);
        return row;
    }

    private IEnumerable<Control> ValueViews(JsonDiffNode node, bool expandable)
    {
        var shown = node.HasNew ? node.NewValue : node.OldValue;
        if (expandable)
        {
            var meta = ContainerSummary(shown);
            if (node.Status == JsonDiffStatus.Added) { yield return Chip(meta, removed: false); yield break; }
            if (node.Status == JsonDiffStatus.Removed) { yield return Chip(meta, removed: true); yield break; }
            yield return Text(meta, "BTextMutedBrush", "meta");
            if (node.Status == JsonDiffStatus.Modified)
            {
                var n = node.Summary.Total;
                var fallback = n == 1 ? "({count} change)" : "({count} changes)";
                yield return Text(L(n == 1 ? "bxaml.jsonDiff.changeCount.one" : "bxaml.jsonDiff.changeCount.other", fallback, Count(n)),
                    "BColorWarningTextBrush", "count");
            }
            yield break;
        }
        switch (node.Status)
        {
            case JsonDiffStatus.Added:
                yield return Chip(Inline(node.NewValue), removed: false);
                break;
            case JsonDiffStatus.Removed:
                yield return Chip(Inline(node.OldValue), removed: true);
                break;
            case JsonDiffStatus.Changed:
                yield return Chip(Inline(node.OldValue), removed: true);
                yield return Text("→", "BTextMutedBrush", "arrow");
                yield return Chip(Inline(node.NewValue), removed: false);
                break;
            case JsonDiffStatus.Ignored:
                yield return Text(Inline(shown), "BTextBrush", "value");
                yield return Text(StatusText(node.Status), "BTextMutedBrush", "note");
                break;
            default:
                yield return Text(Inline(shown), "BTextBrush", "value");
                break;
        }
    }

    private static string ContainerSummary(JsonNode? value) => value switch
    {
        JsonArray a => $"Array({a.Count})",
        JsonObject o => $"{{ {o.Count} {(o.Count == 1 ? "key" : "keys")} }}",
        _ => "{ 0 keys }",
    };

    /// <summary>The web's <c>JSON.stringify</c> rendering (non-ASCII kept readable), cut at <see cref="InlineMax"/>.</summary>
    public static string Inline(JsonNode? value)
    {
        var text = value is null ? "null" : value.ToJsonString(InlineJson);
        return text.Length > InlineMax ? text[..(InlineMax - 1)] + "…" : text;
    }

    // ── Pieces ──

    /// <summary>Applies a Birko ControlTheme by key (DataViewers.axaml); left on the default theme when the app has not merged it.</summary>
    private static T Themed<T>(T control, string key) where T : StyledElement
    {
        if (Application.Current is { } app && app.TryGetResource(key, null, out var theme) && theme is ControlTheme ct) control.Theme = ct;
        return control;
    }

    private static TextBlock Text(string text, string brushKey, string? cls = null)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (cls is not null) t.Classes.Add(cls);
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(brushKey));
        return t;
    }

    /// <summary>A side-coloured value: removed / side A in the danger tint (web <c>del</c>), added / side B in the success tint (<c>ins</c>).</summary>
    private static Border Chip(string text, bool removed)
    {
        var t = Text(text, removed ? "BColorDangerTextBrush" : "BColorSuccessTextBrush");
        var b = new Border { Child = t, Padding = new Thickness(4, 0), CornerRadius = new CornerRadius(3), Classes = { removed ? "del" : "ins" } };
        b.Bind(Border.BackgroundProperty, b.GetResourceObservable(removed ? "BColorDangerAlphaBgBrush" : "BColorSuccessAlphaBgBrush"));
        return b;
    }

    private static TextBlock Message(string text)
    {
        var t = Text(text, "BTextMutedBrush", "message");
        t.FontStyle = FontStyle.Italic;
        return t;
    }

    private static TextBlock Error(string text)
    {
        var t = Text(text, "BColorDangerTextBrush", "error");
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }
}
