namespace Birko.Xaml.Avalonia.Tests.Parity;

/// <summary>One row of a section of <c>Birko.Xaml.Avalonia/PARITY.md</c>.</summary>
internal sealed record ParityRow(string Web, string Status, string Desktop, string Note, int Line);

/// <summary>
/// FEATURE-021: reads a section of the parity list and checks it against the names found on the web side. Pure — no
/// I/O — so each rule is tested against a fixture string.
/// </summary>
internal static class ParityList
{
    public static readonly IReadOnlySet<string> Statuses =
        new HashSet<string>(StringComparer.Ordinal) { "built", "themed", "form-only", "needs-theme", "missing", "skipped", "n/a" };

    /// <summary>The table rows between <c>## {section}</c> and the next <c>## </c> heading.</summary>
    public static IReadOnlyList<ParityRow> Parse(string markdown, string section)
    {
        var rows = new List<ParityRow>();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inSection = false;
        var headerSeen = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line[3..].Trim() == section;
                headerSeen = false;
                continue;
            }
            if (!inSection || !line.StartsWith('|')) continue;
            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (!headerSeen)
            {
                headerSeen = true;
                continue;
            }
            if (cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'))) continue;
            string Cell(int n) => n < cells.Length ? cells[n] : "";
            rows.Add(new ParityRow(Cell(0).Trim('`'), Cell(1), Cell(2), Cell(3), i + 1));
        }
        return rows;
    }

    /// <summary>Every problem, each naming its row or web name, so one run shows the whole gap.</summary>
    public static IReadOnlyList<string> Validate(IReadOnlyList<ParityRow> rows, IEnumerable<string> webNames)
    {
        var problems = new List<string>();
        var web = new HashSet<string>(webNames, StringComparer.Ordinal);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!listed.Add(row.Web)) problems.Add($"line {row.Line}: {row.Web} has more than one row");
            if (!Statuses.Contains(row.Status))
                problems.Add($"line {row.Line}: {row.Web} has status '{row.Status}', not one of {string.Join(", ", Statuses)}");
            if (row.Status == "skipped" && row.Note.Length == 0)
                problems.Add($"line {row.Line}: {row.Web} is skipped with no reason in its Note");
            if (!web.Contains(row.Web)) problems.Add($"line {row.Line}: {row.Web} has a row but no longer exists on the web");
        }
        foreach (var name in web.Order(StringComparer.Ordinal))
            if (!listed.Contains(name)) problems.Add($"{name} exists on the web but has no row");
        return problems;
    }
}
