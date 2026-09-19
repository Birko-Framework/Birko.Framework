namespace AuditCommon;

/// <summary>
/// The ONE producer for path handling across the root audits.
///
/// This type exists because of TASK-476: the four PowerShell originals each carried their own path
/// expressions, and every one of them was Windows-only. Three failed on Linux in the silent-wrong-answer
/// direction — a bucket that does not exist is simply skipped, and a guard written as
/// `if (-not $buckets) { throw }` cannot fire while a SIBLING bucket still resolves. That is TASK-474's
/// rule arriving a second time: a guard for "none" is not a guard for "fewer than asked for".
///
/// So: no separator literal belongs anywhere outside this file. If an audit needs to reason about a
/// path, it asks here.
/// </summary>
public static class Paths
{
    /// <summary>
    /// Turn an MSBuild path into one this OS can open.
    ///
    /// MSBuild writes `..\Birko.Configuration\Birko.Configuration.projitems` with backslashes on every
    /// platform — that is the file format, not a Windows artifact, and all 173 projitems in this tree
    /// use it. On Windows the separator happens to be correct already; on Linux a backslash is an
    /// ordinary filename character, so `Test-Path` answered false, `GetFileNameWithoutExtension`
    /// returned the whole string, and audit-consumer-versions reported every consumer as importing
    /// nothing. Its own header calls that "a defect in this script, not a clean result".
    /// </summary>
    public static string FromMsBuild(string msbuildPath) =>
        msbuildPath.Replace('\\', Path.DirectorySeparatorChar)
                   .Replace('/', Path.DirectorySeparatorChar);

    /// <summary>
    /// Is this path inside a build-output directory?
    ///
    /// Replaces the originals' `-notmatch '\\(bin|obj)\\'`, which tests for backslash-delimited
    /// segments and therefore never matched a `/`-separated path — so on Linux the scans silently
    /// admitted generated sources and restored package copies. Compared segment-wise so the answer
    /// does not depend on the separator, and case-insensitively on Windows only, matching how the
    /// filesystem itself would answer.
    /// </summary>
    public static bool IsUnderBinObj(string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var segment in Split(path))
        {
            if (segment.Equals("bin", comparison) || segment.Equals("obj", comparison))
                return true;
        }
        return false;
    }

    /// <summary>Is this path inside any of the named directories? Segment-wise, separator-neutral.</summary>
    public static bool IsUnderAny(string path, params string[] directoryNames)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var segment in Split(path))
        {
            foreach (var name in directoryNames)
            {
                if (segment.Equals(name, comparison)) return true;
            }
        }
        return false;
    }

    private static string[] Split(string path) =>
        path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The key a "have I already seen this file?" set should use.
    ///
    /// The PowerShell original lower-cased the full path unconditionally. On Linux paths are
    /// case-sensitive, so two genuinely distinct files could collapse into one entry and the second
    /// would be silently skipped. Case-folding is therefore applied only where the filesystem does it.
    /// </summary>
    public static string IdentityKey(string fullPath) =>
        OperatingSystem.IsWindows() ? fullPath.ToLowerInvariant() : fullPath;

    /// <summary>
    /// Enumerate files beneath <paramref name="root"/>, skipping build output.
    /// Returns an empty sequence when the directory is absent rather than throwing, mirroring the
    /// originals' `-ErrorAction SilentlyContinue` — callers that need absence to be loud check first.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern, bool skipBinObj = true)
    {
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (var file in found)
        {
            if (skipBinObj && IsUnderBinObj(file)) continue;
            yield return file;
        }
    }
}
