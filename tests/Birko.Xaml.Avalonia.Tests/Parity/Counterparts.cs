using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Birko.Xaml.Core.Forms;

namespace Birko.Xaml.Avalonia.Tests.Parity;

/// <summary>
/// TASK-791 (FEATURE-021 D8): every name in backticks in a row's Desktop column must exist, by the rule its status
/// sets — see the head of <c>PARITY.md</c>. Needs a running Avalonia app (an <c>[AvaloniaFact]</c>) to load the theme.
/// </summary>
internal sealed class Counterparts
{
    private static readonly Assembly[] Birko =
    [
        typeof(FormField).Assembly,
        typeof(Controls.TagInput).Assembly,
        typeof(Shell.ViewLocator).Assembly,
    ];

    private static readonly Assembly[] AvaloniaControls = [typeof(Button).Assembly, typeof(DataGrid).Assembly];

    private readonly ResourceDictionary _theme;
    private readonly IReadOnlyList<string> _gridSelectors;

    public Counterparts()
    {
        // Loaded on their own, not read off the app: there Fluent's themes answer for every control.
        _theme = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Birko.Xaml.Avalonia/BirkoTheme.axaml"));
        var grid = (Styles)AvaloniaXamlLoader.Load(new Uri("avares://Birko.Xaml.Avalonia/Controls/DataGridStyles.axaml"));
        _gridSelectors = grid.OfType<Style>().Select(s => s.Selector?.ToString() ?? "").ToList();
    }

    public static IReadOnlyList<string> Names(string desktop) =>
        Regex.Matches(desktop, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();

    public IReadOnlyList<string> Validate(IEnumerable<ParityRow> rows)
    {
        var problems = new List<string>();
        foreach (var row in rows)
        {
            var names = Names(row.Desktop);
            if (names.Count == 0 && row.Status is "built" or "themed" or "form-only")
                problems.Add($"line {row.Line}: {row.Web} is {row.Status} but names no counterpart in backticks");
            foreach (var name in names)
                if (Problem(row.Status, name) is { } why) problems.Add($"line {row.Line}: {row.Web} ({row.Status}) — `{name}` {why}");
        }
        return problems;
    }

    private string? Problem(string status, string name) => status switch
    {
        "built" => ResolvesIn(Birko, name) ? null : "is not a type or member in the Birko.Xaml assemblies",
        "form-only" => name.StartsWith("FieldType.", StringComparison.Ordinal) && ResolvesIn(Birko, name)
            ? null : "is not a FieldType member",
        "themed" => IsThemeKey(name) || Find(AvaloniaControls, name) is { } t && IsStyled(t)
            ? null : "is neither a Birko theme key nor an Avalonia control the Birko theme styles",
        "needs-theme" => Find(AvaloniaControls, name) switch
        {
            null => "is not an Avalonia control",
            { } t when IsStyled(t) => "is already styled by the Birko theme — the row is themed",
            _ => null,
        },
        _ => null,
    };

    private bool IsThemeKey(string name) => _theme.TryGetResource(name, null, out _);

    private bool IsStyled(Type control) =>
        _theme.TryGetResource(control, null, out var r) && r is ControlTheme
        || _gridSelectors.Any(s => Regex.IsMatch(s, $@"(^|[^A-Za-z]){control.Name}($|[^A-Za-z])"));

    private static bool ResolvesIn(Assembly[] assemblies, string name)
    {
        if (Find(assemblies, name) is not null) return true;
        var dot = name.LastIndexOf('.');
        if (dot < 0 || Find(assemblies, name[..dot]) is not { } type) return false;
        return type.GetMember(name[(dot + 1)..], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length > 0;
    }

    private static Type? Find(Assembly[] assemblies, string name) =>
        assemblies.SelectMany(a => a.GetExportedTypes())
            .FirstOrDefault(t => (t.Name.IndexOf('`') is >= 0 and var tick ? t.Name[..tick] : t.Name) == name);
}
