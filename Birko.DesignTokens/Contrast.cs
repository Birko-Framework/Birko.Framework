using System.Globalization;

namespace Birko.DesignTokens;

/// <summary>What a foreground/background pair is, which decides the contrast it needs.</summary>
public enum PairKind
{
    /// <summary>Body, secondary and muted text on a page or card surface.</summary>
    SurfaceText,
    /// <summary>Page text on a semantic tint: <c>b-stale-banner</c>, <c>b-sync-status</c>, a markdown <c>&lt;mark&gt;</c>.</summary>
    TintText,
    /// <summary>Semantic text on its own tint: <c>b-badge</c>, <c>b-stat</c>.</summary>
    TintBadge,
    /// <summary>Text on a solid semantic fill: <c>b-toast</c>, <c>b-button</c>, a selected day or segment.</summary>
    FillText,
    /// <summary><c>b-tooltip</c>.</summary>
    Tooltip,
}

public sealed record ContrastPair(PairKind Kind, string Foreground, string Background, string UsedBy);

/// <summary>
/// A theme as the browser applies it. Each level is one element; its sheets are listed winner first. A partial
/// theme (<c>inverse</c>) is its own inner level, so whatever it does not declare is inherited from the page theme.
/// </summary>
public sealed record Composition(string Name, IReadOnlyList<IReadOnlyList<Sheet>> Levels);

public sealed record Measurement(string Composition, ContrastPair Pair, string ForegroundHex, string BackgroundHex,
    double Ratio, double Required)
{
    public bool Passes => Ratio >= Required;

    public override string ToString() =>
        $"{Composition}: {Pair.Foreground} {ForegroundHex} on {Pair.Background} {BackgroundHex} = "
        + $"{Ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1, needs {Required.ToString("0.0", CultureInfo.InvariantCulture)} "
        + $"({Pair.Kind}, {Pair.UsedBy})";
}

/// <summary>A failing pair accepted on purpose: its ratio is recorded, and it is re-measured on every run.</summary>
public sealed record ContrastException(string Composition, string Foreground, string Background, double Recorded,
    string Reason);

public sealed record ContrastReport(
    IReadOnlyList<Measurement> Failures,
    IReadOnlyList<(ContrastException Exception, Measurement Now)> Worsened,
    IReadOnlyList<(ContrastException Exception, Measurement? Now)> Stale,
    IReadOnlyList<Measurement> Accepted,
    int Measured)
{
    public bool Clean => Failures.Count == 0 && Worsened.Count == 0 && Stale.Count == 0;

    /// <summary>Every problem in one text, so one run shows all of them (TASK-130).</summary>
    public string Describe()
    {
        var lines = new List<string> { $"{Measured} pairs measured." };
        if (Failures.Count > 0)
        {
            lines.Add($"{Failures.Count} below the floor:");
            lines.AddRange(Failures.Select(f => "  " + f));
        }
        if (Worsened.Count > 0)
        {
            lines.Add($"{Worsened.Count} recorded exception(s) got worse:");
            lines.AddRange(Worsened.Select(w =>
                $"  {w.Now} (recorded {w.Exception.Recorded.ToString("0.00", CultureInfo.InvariantCulture)}: {w.Exception.Reason})"));
        }
        if (Stale.Count > 0)
        {
            lines.Add($"{Stale.Count} recorded exception(s) no longer apply; remove them:");
            lines.AddRange(Stale.Select(s =>
                $"  {s.Exception.Composition}: {s.Exception.Foreground} on {s.Exception.Background} "
                + (s.Now is null ? "is no longer measured" : $"now {s.Now.Ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1, passes")));
        }
        if (Accepted.Count > 0)
        {
            lines.Add($"{Accepted.Count} accepted exception(s), re-measured:");
            lines.AddRange(Accepted.Select(a => "  " + a));
        }
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Measures every shipped theme's text pairs against WCAG 2.1 contrast (TASK-130). Values come from tokens.json, so
/// the generated CSS and AXAML are both covered: the AXAML resolves the same <c>var()</c> chain per theme.
/// </summary>
public static class Contrast
{
    private static readonly string[] Semantic = { "primary", "success", "warning", "danger", "info" };
    private static readonly string[] Fills = { "success", "warning", "danger", "info" };

    /// <summary>The pairs components render as text. Extend it when a component starts rendering a new one.</summary>
    public static IReadOnlyList<ContrastPair> Pairs { get; } = BuildPairs();

    /// <summary>
    /// Failing pairs accepted on purpose, each with its measured ratio and why. Empty: every shipped pair passes.
    /// An entry fails the gate if its pair gets worse than recorded, or once it passes (remove it then).
    /// </summary>
    public static IReadOnlyList<ContrastException> Exceptions { get; } = Array.Empty<ContrastException>();

    /// <summary>
    /// The floor for a kind. Every kind here is text at normal size, so all take AA's 4.5:1: the 3:1 "large text"
    /// allowance needs 24px, or 18.66px bold. A toast is <c>--b-text-sm</c> medium; a badge, a stat delta and a
    /// tooltip are <c>--b-text-xs</c> (11px) medium, the smallest text in the catalogue and the least able to take an
    /// allowance. A kind stays separate so a large-text use can get its own floor without loosening the others.
    /// </summary>
    public static double Required(PairKind kind) => kind switch
    {
        PairKind.SurfaceText => 4.5,
        PairKind.TintText => 4.5,
        PairKind.TintBadge => 4.5,
        PairKind.FillText => 4.5,
        PairKind.Tooltip => 4.5,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The four page themes, then <c>inverse</c> composed over each of them.</summary>
    public static IReadOnlyList<Composition> Compositions(TokenSet tokens)
    {
        Sheet ByTheme(string theme) => tokens.Sheets.Single(s => s.Theme == theme);
        var root = ByTheme("light");
        var pages = new List<Composition> { new("light", new[] { new[] { root } }) };
        foreach (var theme in new[] { "dark", "neon", "finstat" })
            pages.Add(new(theme, new[] { new[] { ByTheme(theme), root } }));

        var inverse = ByTheme("inverse");
        var all = new List<Composition>(pages);
        foreach (var page in pages)
            all.Add(new($"inverse/{page.Name}", new IReadOnlyList<Sheet>[] { new[] { inverse } }.Concat(page.Levels).ToList()));
        return all;
    }

    /// <summary>
    /// A token's value as the browser computes it. A declaration's <c>var()</c> resolves on the element that declares
    /// it, so a value inherited from the page is resolved against the page, never against the partial theme above it.
    /// </summary>
    public static string Resolve(Composition composition, string name)
    {
        for (int level = 0; level < composition.Levels.Count; level++)
        {
            string? value = composition.Levels[level].Select(s => Declared(s, name)).FirstOrDefault(v => v is not null);
            if (value is null) continue;
            if (!value.StartsWith("var(", StringComparison.Ordinal)) return value;
            string inner = value[4..^1].Split(',')[0].Trim();
            return Resolve(composition with { Levels = composition.Levels.Skip(level).ToList() }, inner);
        }
        throw new InvalidOperationException($"{name} is not defined in {composition.Name}");
    }

    public static IReadOnlyList<Measurement> Measure(TokenSet tokens)
    {
        var result = new List<Measurement>();
        foreach (var composition in Compositions(tokens))
        {
            foreach (var pair in Pairs)
            {
                string? fg = TryResolve(composition, pair.Foreground);
                string? bg = TryResolve(composition, pair.Background);
                // An undefined token is a failure to report beside the others, not an exception that hides them.
                double ratio = fg is null || bg is null ? 0 : Ratio(fg, bg);
                result.Add(new(composition.Name, pair, fg ?? "(undefined)", bg ?? "(undefined)", ratio, Required(pair.Kind)));
            }
        }
        return result;
    }

    public static ContrastReport Evaluate(IReadOnlyList<Measurement> measured, IEnumerable<ContrastException> exceptions)
    {
        var byKey = measured.ToDictionary(m => (m.Composition, m.Pair.Foreground, m.Pair.Background));
        var excepted = new HashSet<(string, string, string)>();
        var worsened = new List<(ContrastException, Measurement)>();
        var stale = new List<(ContrastException, Measurement?)>();
        var accepted = new List<Measurement>();

        foreach (var ex in exceptions)
        {
            var key = (ex.Composition, ex.Foreground, ex.Background);
            excepted.Add(key);
            if (!byKey.TryGetValue(key, out var now) || now.Passes) stale.Add((ex, now));
            // Two decimals is what is recorded; anything below that is a real move, not rounding.
            else if (Math.Round(now.Ratio, 2) < ex.Recorded) worsened.Add((ex, now));
            else accepted.Add(now);
        }

        var failures = measured.Where(m => !m.Passes && !excepted.Contains((m.Composition, m.Pair.Foreground, m.Pair.Background)))
            .ToList();
        return new(failures, worsened, stale, accepted, measured.Count);
    }

    /// <summary>WCAG 2.1 contrast ratio of two opaque <c>#rrggbb</c> colours.</summary>
    public static double Ratio(string a, string b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, null, out _))
            throw new FormatException($"Contrast is measured on opaque #rrggbb colours, got '{hex}'.");
        double Channel(int i)
        {
            double c = int.Parse(hex.AsSpan(1 + 2 * i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(1) + 0.0722 * Channel(2);
    }

    private static string? TryResolve(Composition composition, string name)
    {
        try { return Resolve(composition, name); }
        catch (InvalidOperationException) { return null; }
    }

    private static string? Declared(Sheet sheet, string name) =>
        sheet.Body.LastOrDefault(n => n.IsVar && n.Name == name)?.Value?.Trim();

    private static List<ContrastPair> BuildPairs()
    {
        var pairs = new List<ContrastPair>();
        foreach (var text in new[] { "--b-text", "--b-text-secondary", "--b-text-muted" })
            foreach (var surface in new[] { "--b-bg", "--b-bg-secondary", "--b-bg-tertiary", "--b-bg-elevated" })
                pairs.Add(new(PairKind.SurfaceText, text, surface, "page and card text"));
        foreach (var name in Semantic)
        {
            pairs.Add(new(PairKind.TintText, "--b-text", $"--b-color-{name}-light", "b-stale-banner, b-sync-status"));
            pairs.Add(new(PairKind.TintBadge, $"--b-color-{name}-text", $"--b-color-{name}-light", "b-badge, b-stat"));
        }
        pairs.Add(new(PairKind.FillText, "--b-text-on-primary", "--b-color-primary", "b-button, pickers, b-option-group"));
        pairs.Add(new(PairKind.FillText, "--b-text-on-primary", "--b-color-primary-hover", "b-button hover"));
        foreach (var name in Fills)
            pairs.Add(new(PairKind.FillText, $"--b-text-on-{name}", $"--b-color-{name}", "b-toast, b-button"));
        pairs.Add(new(PairKind.Tooltip, "--b-tooltip-text", "--b-tooltip-bg", "b-tooltip"));
        return pairs;
    }
}
