namespace AuditCommon;

/// <summary>
/// The `[CmdletBinding()] param(...)` the PowerShell originals declared: a `-Root` and a
/// `-FailOnFinding` switch. Both PowerShell and POSIX spellings are accepted, because the
/// documentation, the task files and eight closed task write-ups all quote the PowerShell one and
/// a reader who types it should not get a shrug.
/// </summary>
public sealed class Cli
{
    public string? Root { get; private init; }
    public bool FailOnFinding { get; private init; }

    /// <summary>
    /// Parse, or print the reason and exit 2 — never a stack trace. A tool that answers a typo with
    /// an unhandled exception buries the one line the reader needs under a frame list.
    ///
    /// Exit 2 is deliberately distinct from the 1 that `--fail-on-finding` returns: a scheduled job
    /// that cannot tell "the audit found something" from "I mistyped the flag" will eventually read
    /// the second as the first.
    /// </summary>
    public static Cli ParseOrExit(string[] args, string usage)
    {
        try
        {
            return Parse(args);
        }
        catch (HelpRequested)
        {
            Console.WriteLine(usage);
            Environment.Exit(0);
            throw; // unreachable; keeps the compiler happy about the non-null return
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(usage);
            Environment.Exit(2);
            throw; // unreachable
        }
    }

    public static Cli Parse(string[] args)
    {
        string? root = null;
        var fail = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (Normalise(arg))
            {
                case "root":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{arg} needs a path after it.");
                    root = args[++i];
                    break;

                case "failonfinding":
                    fail = true;
                    break;

                case "help":
                    throw new HelpRequested();

                default:
                    // An unrecognised argument is refused rather than ignored. A typo'd `--fail-on-findings`
                    // that silently parses as "report only" turns a gate into a no-op wearing a flag's name.
                    throw new ArgumentException($"Unrecognised argument: {arg}");
            }
        }

        return new Cli { Root = root, FailOnFinding = fail };
    }

    /// <summary>`-Root`, `--root`, `/Root` and `--fail-on-finding` all reduce to the same key.</summary>
    private static string Normalise(string arg) =>
        arg.TrimStart('-', '/').Replace("-", "").Replace("_", "").ToLowerInvariant();

    public sealed class HelpRequested : Exception;

    /// <summary>
    /// Resolve the bucket root: the explicit `--root`, else <paramref name="fallbackRelativeToScript"/>
    /// levels above the script. Throws when it does not exist, which is the originals' behaviour under
    /// `$ErrorActionPreference = 'Stop'`.
    /// </summary>
    public string ResolveRoot(string scriptDirectory, int levelsUp)
    {
        if (!string.IsNullOrWhiteSpace(Root))
        {
            var explicitRoot = Path.GetFullPath(Root);
            if (!Directory.Exists(explicitRoot)) throw new DirectoryNotFoundException($"Root not found: {explicitRoot}");
            return explicitRoot;
        }

        var path = scriptDirectory;
        for (var i = 0; i < levelsUp; i++) path = Path.GetDirectoryName(path) ?? path;

        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved)) throw new DirectoryNotFoundException($"Root not found: {resolved}");
        return resolved;
    }
}
