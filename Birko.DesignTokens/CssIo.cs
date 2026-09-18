using System.Text;

namespace Birko.DesignTokens;

/// <summary>
/// Extracts hand-authored CSS token files into the neutral <see cref="Sheet"/> model, and
/// emits them back byte-identically. All I/O is UTF-8 (no BOM), CRLF line endings, with a
/// trailing newline — matching the existing files exactly.
/// </summary>
public static class CssIo
{
    /// <summary>UTF-8 without a BOM — the source files have none.</summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Parse one CSS file's text into a <see cref="Sheet"/>. <paramref name="file"/>/<paramref name="theme"/>
    /// are metadata carried into the model (they cannot be inferred reliably from content alone).</summary>
    public static Sheet Extract(string text, string file, string theme)
    {
        // The source files are internally consistent but differ across files (tokens.css is CRLF,
        // the theme files are LF), so detect per-file and preserve it.
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string openMarker = " {" + eol;       // end of the "selector {" line
        string closeMarker = eol + "}" + eol;  // the "}" on its own line

        int openIdx = text.IndexOf(openMarker, StringComparison.Ordinal);
        if (openIdx < 0)
            throw new InvalidOperationException($"No 'selector {{' opening line found in {file}.");

        // Start of the selector line = just after the previous newline (or 0).
        int selStart = text.LastIndexOf(eol, openIdx, StringComparison.Ordinal);
        selStart = selStart < 0 ? 0 : selStart + eol.Length;

        string prologue = text.Substring(0, selStart);
        string selector = text.Substring(selStart, openIdx - selStart);
        int bodyStart = openIdx + openMarker.Length;

        int closeIdx = text.IndexOf(closeMarker, bodyStart, StringComparison.Ordinal);
        if (closeIdx < 0)
            throw new InvalidOperationException($"No closing '}}' line found in {file}.");

        string bodyText = text.Substring(bodyStart, closeIdx - bodyStart);
        string epilogue = text.Substring(closeIdx + closeMarker.Length);

        var sheet = new Sheet
        {
            File = file,
            Theme = theme,
            Selector = selector,
            Eol = eol == "\r\n" ? "crlf" : "lf",
            Prologue = prologue,
            Epilogue = epilogue,
        };

        foreach (var line in bodyText.Split(eol))
            sheet.Body.Add(ParseLine(line));

        return sheet;
    }

    private static Node ParseLine(string line)
    {
        // A var line: two-space indent, "--name", ": ", value, ";", optional trailing comment.
        if (line.StartsWith("  --", StringComparison.Ordinal))
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                int semi = line.IndexOf(';', colon + 2);
                if (semi > 0)
                {
                    string name = line.Substring(2, colon - 2);
                    string value = line.Substring(colon + 2, semi - (colon + 2));
                    string trail = line.Substring(semi + 1);
                    return new Node
                    {
                        Kind = "var",
                        Name = name,
                        Value = value,
                        Trail = trail.Length == 0 ? null : trail,
                    };
                }
            }
        }

        return new Node { Kind = "raw", Raw = line };
    }

    /// <summary>Render a <see cref="Sheet"/> back to its exact CSS text.</summary>
    public static string Emit(Sheet sheet)
    {
        string eol = sheet.Newline;
        var sb = new StringBuilder(sheet.Prologue.Length + sheet.Epilogue.Length + sheet.Body.Count * 48);
        sb.Append(sheet.Prologue);
        sb.Append(sheet.Selector).Append(" {").Append(eol);

        for (int i = 0; i < sheet.Body.Count; i++)
        {
            if (i > 0) sb.Append(eol);
            sb.Append(RenderLine(sheet.Body[i]));
        }

        sb.Append(eol).Append('}').Append(eol);
        sb.Append(sheet.Epilogue);
        return sb.ToString();
    }

    private static string RenderLine(Node node)
    {
        if (node.IsVar)
            return "  " + node.Name + ": " + node.Value + ";" + (node.Trail ?? string.Empty);
        return node.Raw ?? string.Empty;
    }

    public static string Read(string path) => File.ReadAllText(path, Utf8NoBom);

    /// <summary>Normalize CRLF to LF. The repo canonically stores these files as LF (autocrlf may
    /// present CRLF in a working tree); the token model is kept in the canonical LF form so
    /// generation is byte-stable regardless of a machine's line-ending checkout settings.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n");

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8NoBom);
    }
}
