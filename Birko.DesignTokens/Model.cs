using System.Text.Json;
using System.Text.Json.Serialization;

namespace Birko.DesignTokens;

/// <summary>
/// The single source of truth for all Birko design tokens, deserialized from tokens.json.
///
/// Design goals (see CLAUDE.md):
///  - LANGUAGE-NEUTRAL: plain data only, no C#-specific constructs. A future TypeScript
///    generator must be able to read this same JSON unchanged.
///  - VALUES are single-sourced: a token's <see cref="Node.Value"/> is the one place a
///    value lives; both the CSS and the AXAML targets derive from it.
///  - Byte-identical CSS round-trip: each CSS file is modeled as prologue + selector + body
///    lines + epilogue, where every physical body line is one <see cref="Node"/> (a
///    structured <c>var</c> or verbatim <c>raw</c>), so regeneration reproduces the
///    hand-authored file exactly (comments, blank lines, comment-column alignment).
/// </summary>
public sealed class TokenSet
{
    /// <summary>Human note carried into the JSON so editors know the file is source-of-truth.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("sheets")]
    public List<Sheet> Sheets { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TokenSet Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TokenSet>(json, Options)
               ?? throw new InvalidOperationException($"tokens.json at {path} deserialized to null.");
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

/// <summary>One generated CSS file (light base or a theme).</summary>
public sealed class Sheet
{
    /// <summary>Path relative to the css/ directory, e.g. "tokens.css" or "themes/dark.css".</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>Theme id: light | dark | neon | finstat | inverse.</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = string.Empty;

    /// <summary>CSS selector wrapping the tokens, e.g. ":root" or "[data-theme=\"dark\"]".</summary>
    [JsonPropertyName("selector")]
    public string Selector { get; set; } = string.Empty;

    /// <summary>Line-ending style of this file: "crlf" or "lf". The source files are inconsistent
    /// (tokens.css is CRLF, the theme files are LF), so it is preserved per-sheet for byte parity.</summary>
    [JsonPropertyName("eol")]
    public string Eol { get; set; } = "lf";

    /// <summary>The actual newline sequence for <see cref="Eol"/>.</summary>
    [JsonIgnore]
    public string Newline => Eol == "crlf" ? "\r\n" : "\n";

    /// <summary>Verbatim text (incl. its own CRLFs) appearing before the "{selector} {" line. May be empty.</summary>
    [JsonPropertyName("prologue")]
    public string Prologue { get; set; } = string.Empty;

    /// <summary>Verbatim text appearing after the closing "}" line (e.g. prose + @media). May be empty.</summary>
    [JsonPropertyName("epilogue")]
    public string Epilogue { get; set; } = string.Empty;

    /// <summary>Body lines, in order. Each element is exactly one physical CSS line.</summary>
    [JsonPropertyName("body")]
    public List<Node> Body { get; set; } = new();
}

/// <summary>One physical line inside a sheet body.</summary>
public sealed class Node
{
    /// <summary>"var" (a token declaration) or "raw" (verbatim comment / blank / anything else).</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "raw";

    // --- var ---
    /// <summary>Token name incl. leading dashes, e.g. "--b-color-primary".</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>The single-sourced token value, e.g. "#2563eb" or "0.75rem".</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Verbatim text after the terminating ';' — leading whitespace + inline comment
    /// (preserves the hand-authored comment-column alignment). Null/empty when the line has none.</summary>
    [JsonPropertyName("trail")]
    public string? Trail { get; set; }

    // --- raw ---
    /// <summary>Verbatim line text (no trailing CRLF) for comments, blank lines ("") and anything non-var.</summary>
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    public bool IsVar => Kind == "var";
}
