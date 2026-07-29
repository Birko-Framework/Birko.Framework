using Birko.DesignTokens;

// Birko.DesignTokens — single-source design-token generator (EPIC-015 / STORY-029).
//
//   extract    Bootstrap/refresh tokens.json FROM the hand-authored CSS (one-time / verification).
//   generate   Emit all targets FROM tokens.json: the web CSS (byte-identical) + Avalonia AXAML.
//   verify     Regenerate CSS in-memory and diff against the on-disk files; non-zero exit on drift.
//
// tokens.json is the source of truth; `generate` is the everyday command.

var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var paths = Paths.Resolve(args);

return verb switch
{
    "extract" => Extract(),
    "generate" => Generate(),
    "verify" => Verify(),
    _ => Help(),
};

int Help()
{
    Console.WriteLine("Birko.DesignTokens — design-token generator");
    Console.WriteLine("Usage: dotnet run --project Birko.DesignTokens -- <extract|generate|verify> [--root <birkoRoot>]");
    Console.WriteLine();
    Console.WriteLine("  extract    (re)build tokens.json from the current CSS files (bootstrap/verification)");
    Console.WriteLine("  generate   emit CSS (byte-identical) + Avalonia AXAML from tokens.json");
    Console.WriteLine("  verify     diff regenerated CSS against on-disk files; exit 1 on any drift");
    Console.WriteLine();
    Console.WriteLine($"Resolved Birko root: {paths.Root}");
    return 0;
}

int Extract()
{
    var set = new TokenSet
    {
        Note = "SINGLE SOURCE OF TRUTH for Birko design tokens. Generated targets (web CSS, Avalonia AXAML) "
             + "are derived from this file and must never be hand-edited. Edit a token's \"value\" here, then run: "
             + "dotnet run --project Birko.DesignTokens -- generate",
    };

    foreach (var (file, theme) in Paths.SheetManifest)
    {
        string cssPath = Path.Combine(paths.WebCss, file.Replace('/', Path.DirectorySeparatorChar));
        string text = CssIo.Normalize(CssIo.Read(cssPath));
        var sheet = CssIo.Extract(text, file, theme);

        // Self-check: the extracted sheet must round-trip to the exact input.
        string roundTripped = CssIo.Emit(sheet);
        if (roundTripped != text)
        {
            Console.Error.WriteLine($"EXTRACT FAILED: {file} does not round-trip (extraction lost information).");
            return 1;
        }
        set.Sheets.Add(sheet);
    }

    CssIo.Write(paths.TokensJson, set.ToJson() + "\n");
    Console.WriteLine($"Wrote {paths.TokensJson} ({set.Sheets.Count} sheets, all round-trip clean).");
    return 0;
}

int Generate()
{
    var set = TokenSet.Load(paths.TokensJson);

    // 1) Web CSS — must be byte-identical to the hand-authored files.
    foreach (var sheet in set.Sheets)
    {
        string outPath = Path.Combine(paths.WebCss, sheet.File.Replace('/', Path.DirectorySeparatorChar));
        CssIo.Write(outPath, CssIo.Emit(sheet));
    }
    Console.WriteLine($"Wrote {set.Sheets.Count} CSS file(s) to {paths.WebCss}");

    // 2) Avalonia AXAML dictionaries.
    var axaml = AxamlEmitter.Generate(set);
    foreach (var (name, content) in axaml)
        CssIo.Write(Path.Combine(paths.XamlThemes, name), content);
    Console.WriteLine($"Wrote {axaml.Count} AXAML file(s) to {paths.XamlThemes}");
    return 0;
}

int Verify()
{
    var set = TokenSet.Load(paths.TokensJson);
    int drift = 0;
    foreach (var sheet in set.Sheets)
    {
        string outPath = Path.Combine(paths.WebCss, sheet.File.Replace('/', Path.DirectorySeparatorChar));
        string expected = CssIo.Normalize(CssIo.Read(outPath));
        string actual = CssIo.Emit(sheet);
        if (expected != actual)
        {
            drift++;
            Console.Error.WriteLine($"DRIFT: {sheet.File} differs from tokens.json regeneration.");
        }
    }
    // AXAML too. The split into per-theme dictionaries turned one generated file into six, each
    // hand-editable and — until now — unguarded, so drift here was invisible to `verify`.
    var axaml = AxamlEmitter.Generate(set);
    foreach (var (name, content) in axaml)
    {
        string outPath = Path.Combine(paths.XamlThemes, name);
        if (!File.Exists(outPath))
        {
            drift++;
            Console.Error.WriteLine($"MISSING: {name} has not been generated into {paths.XamlThemes}.");
            continue;
        }
        // Compare EOL-normalized, matching the CSS gate — generation must not depend on a
        // machine's autocrlf checkout settings.
        if (CssIo.Normalize(CssIo.Read(outPath)) != CssIo.Normalize(content))
        {
            drift++;
            Console.Error.WriteLine($"DRIFT: {name} differs from tokens.json regeneration.");
        }
    }

    if (drift == 0)
        Console.WriteLine($"OK — all {set.Sheets.Count} CSS + {axaml.Count} AXAML file(s) match tokens.json exactly.");
    return drift == 0 ? 0 : 1;
}
