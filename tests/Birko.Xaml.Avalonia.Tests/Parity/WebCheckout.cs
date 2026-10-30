namespace Birko.Xaml.Avalonia.Tests.Parity;

/// <summary>
/// Finds the <c>Birko\Web</c> checkout as <c>Birko.Xaml.Core.Tests</c>' <c>DiffVectors</c> does: <c>BIRKO_SRC</c> (the
/// Framework bucket) and its sibling <c>Web</c>, else a walk up to the directory holding <c>Framework</c>. Absent, a
/// suite skips with a line saying so — unless <c>BIRKO_REQUIRE_LIVE</c> is set, where it fails, as the live suites do.
/// </summary>
internal static class WebCheckout
{
    public static string? Locate(Action<string> log)
    {
        var root = Find();
        if (root is not null) return root;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException("The Birko\\Web checkout was not found but BIRKO_REQUIRE_LIVE is set.");
        log("SKIPPED: no Birko\\Web checkout beside Birko\\Framework (set BIRKO_SRC).");
        return null;
    }

    private static string? Find()
    {
        var birkoSrc = Environment.GetEnvironmentVariable("BIRKO_SRC");
        if (!string.IsNullOrWhiteSpace(birkoSrc))
        {
            var viaEnv = Path.GetFullPath(Path.Combine(birkoSrc, "..", "Web"));
            if (IsWeb(viaEnv)) return viaEnv;
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!Directory.Exists(Path.Combine(dir.FullName, "Framework"))) continue;
            var candidate = Path.Combine(dir.FullName, "Web");
            if (IsWeb(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsWeb(string path) => Directory.Exists(Path.Combine(path, "Birko.Web.Components", "src"));

    /// <summary>The parity list, found by walking up from the test binary to the framework checkout.</summary>
    public static string ParityListPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Birko.Xaml.Avalonia", "PARITY.md");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Birko.Xaml.Avalonia/PARITY.md was not found above the test binary.");
    }

    /// <summary>The <c>b-*</c> component names in Birko.Web.Components, by file name.</summary>
    public static IReadOnlyList<string> ComponentNames(string webRoot) =>
        Directory.EnumerateFiles(Path.Combine(webRoot, "Birko.Web.Components", "src"), "b-*.ts", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".d.ts", StringComparison.Ordinal))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
}
