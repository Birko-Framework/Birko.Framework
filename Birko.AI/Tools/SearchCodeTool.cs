using Birko.Helpers;
using System.Text.RegularExpressions;

namespace Birko.AI.Tools
{
    public class SearchCodeTool : Tool
    {
        /// <summary>How long one line may take to match a regex query (TASK-160, SH-M168).</summary>
        public static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

        public override string Name => "search_code";
        public override string Description => "Search text in files within the workspace and return matching lines with file paths and line numbers.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "Text or regex to search for" },
                directory = new { type = "string", description = "Optional subdirectory to search in" },
                pattern = new { type = "string", description = "Optional file glob pattern (e.g., *.cs)", @default = "*" },
                recursive = new { type = "boolean", description = "Search recursively", @default = true },
                regex = new { type = "boolean", description = "Treat query as regular expression", @default = false },
                case_sensitive = new { type = "boolean", description = "Case sensitive search", @default = false }
            },
            required = new[] { "query" }
        };

        public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
        {
            try
            {
                var query = input["query"].ToString();
                var relDir = input.TryGetValue("directory", out var dirVal) ? dirVal?.ToString() : null;
                var filePattern = input.TryGetValue("pattern", out var patVal) ? (patVal?.ToString() ?? "*") : "*";
                var recursive = !input.TryGetValue("recursive", out var recVal) || !bool.TryParse(recVal?.ToString(), out var recParsed) || recParsed;
                var useRegex = input.TryGetValue("regex", out var regexVal) && bool.TryParse(regexVal?.ToString(), out var regexParsed) && regexParsed;
                var caseSensitive = input.TryGetValue("case_sensitive", out var csVal) && bool.TryParse(csVal?.ToString(), out var csParsed) && csParsed;

                if (string.IsNullOrWhiteSpace(query))
                    throw new ArgumentException("query is required");

                var targetDir = string.IsNullOrWhiteSpace(relDir) ? workingDirectory : Path.Combine(workingDirectory, relDir!);

                // SH-M167: the same allow-list every other file tool honours.
                if (!PathHelper.IsPathSafe(targetDir, workingDirectory, Options?.AllowedExternalPaths))
                    return Task.FromResult($"Error: Access denied. Directory must be in {workingDirectory} or an allowed external path");

                if (!Directory.Exists(targetDir))
                    return Task.FromResult($"Error: Directory not found: {relDir ?? "."}");

                // SH-L172: an unreadable subdirectory is skipped; it aborted the whole search.
                var files = Directory.EnumerateFiles(targetDir, filePattern, new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true }).ToList();
                var results = new List<string>();

                Regex? rx = null;
                if (useRegex)
                    // SH-M168: the pattern comes from the model; a catastrophic one must not pin a CPU for good.
                    rx = new Regex(query, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, MatchTimeout);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".pdf" or ".zip")
                        continue;

                    string[] lines;
                    try { lines = File.ReadAllLines(file); }
                    catch { continue; }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        bool match = useRegex
                            ? rx!.IsMatch(line)
                            : (caseSensitive ? line.Contains(query) : line.Contains(query, StringComparison.InvariantCultureIgnoreCase));
                        if (match)
                        {
                            var rel = Path.GetRelativePath(workingDirectory, file);
                            results.Add($"{rel}:{i + 1}: {line}");
                        }
                    }
                }

                // SH-L173: "" meant "no match" and "nothing was searched" alike.
                if (results.Count == 0)
                    return Task.FromResult($"No matches for '{query}' in {files.Count} file{(files.Count == 1 ? "" : "s")} searched ({filePattern}).");
                return Task.FromResult(string.Join(Environment.NewLine, results));
            }
            catch (RegexMatchTimeoutException)
            {
                return Task.FromResult($"Error: the regex timed out after {MatchTimeout.TotalSeconds:0} seconds on one line; simplify the pattern.");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return Task.FromResult($"Error: searching code failed: {ex.Message}");
            }
        }
    }
}
