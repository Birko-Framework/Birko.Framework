namespace Birko.DesignTokens;

/// <summary>
/// Resolves the on-disk locations the tool reads/writes. Follows the Birko $(BirkoSrc)/BIRKO_SRC
/// convention: honor an explicit --root, then the BIRKO_SRC env var (points at the Framework
/// checkout; its parent is the Birko root), then walk up from the running assembly to find the
/// Birko root (a directory holding both "Framework" and "Web" buckets).
/// </summary>
public sealed class Paths
{
    /// <summary>The Birko root — parent of the Framework and Web buckets.</summary>
    public required string Root { get; init; }
    public required string TokensJson { get; init; }
    public required string WebCss { get; init; }
    public required string XamlThemes { get; init; }

    /// <summary>The five CSS sheets and their theme ids, in generation order.</summary>
    public static readonly (string File, string Theme)[] SheetManifest =
    {
        ("tokens.css", "light"),
        ("themes/dark.css", "dark"),
        ("themes/neon.css", "neon"),
        ("themes/finstat.css", "finstat"),
        ("themes/inverse.css", "inverse"),
    };

    public static Paths Resolve(string[] args)
    {
        string? root = ArgValue(args, "--root");

        if (root is null)
        {
            string? birkoSrc = Environment.GetEnvironmentVariable("BIRKO_SRC");
            if (!string.IsNullOrWhiteSpace(birkoSrc))
                root = Directory.GetParent(birkoSrc.TrimEnd('\\', '/'))?.FullName;
        }

        root ??= WalkUpForRoot(AppContext.BaseDirectory)
                 ?? WalkUpForRoot(Directory.GetCurrentDirectory())
                 ?? throw new InvalidOperationException(
                     "Could not locate the Birko root (a folder containing 'Framework' and 'Web'). "
                     + "Pass --root <path> or set BIRKO_SRC.");

        return new Paths
        {
            Root = root,
            TokensJson = Path.Combine(root, "Framework", "Birko.DesignTokens", "tokens.json"),
            WebCss = Path.Combine(root, "Web", "Birko.Web.Components", "css"),
            XamlThemes = Path.Combine(root, "Framework", "Birko.Xaml.Avalonia", "Themes"),
        };
    }

    private static string? WalkUpForRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Framework"))
                && Directory.Exists(Path.Combine(dir.FullName, "Web")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ArgValue(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
