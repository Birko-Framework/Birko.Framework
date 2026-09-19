namespace AuditCommon;

/// <summary>
/// Console output for the root audits — the `Write-Host -ForegroundColor` and `Format-Table -AutoSize`
/// the PowerShell originals got for free.
///
/// Colour is not decoration here. Every one of these scripts distinguishes three outcomes and the
/// originals leaned on colour to do it: green "clean", yellow/red "findings", and magenta "could not
/// be checked". That third one is the whole point — TASK-473 found this script family printing a
/// magenta "could not be compared" and a green "nothing found" underneath, which reads as a clean run
/// to anyone skimming. `Unknown()` exists so no caller has to remember which colour means that.
/// </summary>
public static class Report
{
    /// <summary>
    /// Honour NO_COLOR and a redirected stdout. The originals emitted escape codes unconditionally,
    /// which is fine at a prompt and noise in a log file or a CI annotation — and these are meant to
    /// be wired into scheduled jobs.
    /// </summary>
    private static readonly bool UseColour =
        Environment.GetEnvironmentVariable("NO_COLOR") is null && !Console.IsOutputRedirected;

    public static void Say(string text = "", ConsoleColor? colour = null)
    {
        if (colour is null || !UseColour)
        {
            Console.WriteLine(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = colour.Value;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }

    public static void Heading(string text) => Say(text, ConsoleColor.Cyan);
    public static void Good(string text) => Say(text, ConsoleColor.Green);
    public static void Warn(string text) => Say(text, ConsoleColor.Yellow);
    public static void Bad(string text) => Say(text, ConsoleColor.Red);
    public static void Faint(string text) => Say(text, ConsoleColor.DarkGray);

    /// <summary>
    /// "I could not check this" — never green, never silent. See the class remarks.
    /// </summary>
    public static void Unknown(string text) => Say(text, ConsoleColor.Magenta);

    /// <summary>
    /// `Format-Table -AutoSize`: every column as wide as its widest cell, header underlined.
    /// Written out because C# has no equivalent; this is the one thing the port genuinely lost.
    /// </summary>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0) return;

        var widths = new int[headers.Count];
        for (var c = 0; c < headers.Count; c++)
        {
            widths[c] = headers[c].Length;
            foreach (var row in rows)
            {
                if (c < row.Count) widths[c] = Math.Max(widths[c], row[c]?.Length ?? 0);
            }
        }

        Say(string.Join(" ", headers.Select((h, c) => h.PadRight(widths[c]))).TrimEnd());
        Say(string.Join(" ", widths.Select(w => new string('-', w))));

        foreach (var row in rows)
        {
            var cells = headers.Select((_, c) => (c < row.Count ? row[c] ?? "" : "").PadRight(widths[c]));
            Say(string.Join(" ", cells).TrimEnd());
        }
    }
}
