using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class ListFilesTool : Tool
    {
        public override string Name => "list_files";
        public override string Description => "List files in the workspace or a subdirectory.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                directory = new { type = "string", description = "Optional path relative to workspace root" },
                recursive = new { type = "boolean", description = "List files recursively", @default = false }
            }
        };

        public override string Execute(string workingDirectory, Dictionary<string, object> input)
        {
            try
            {
                var relDir = input != null && input.TryGetValue("directory", out var dirVal) ? dirVal?.ToString()?.Trim() : null;
                var recursive = input != null && input.TryGetValue("recursive", out var recVal) && bool.TryParse(recVal?.ToString(), out var recParsed) && recParsed;

                var normalizedWorkingDir = Path.GetFullPath(workingDirectory);

                string targetDir;
                if (string.IsNullOrWhiteSpace(relDir) || relDir == "." || relDir == "./")
                {
                    targetDir = normalizedWorkingDir;
                }
                else
                {
                    var combinedPath = relDir!.StartsWith('/') && !relDir.StartsWith("//")
                        ? relDir.Substring(1)
                        : relDir;
                    targetDir = Path.GetFullPath(Path.Combine(normalizedWorkingDir, combinedPath));
                }

                if (!PathHelper.IsPathSafe(targetDir, normalizedWorkingDir, Options?.AllowedExternalPaths))
                    return $"Error: Access denied. Path must be in workspace or an allowed external path.";

                if (!Directory.Exists(targetDir))
                    return $"Error: Directory not found: {relDir ?? "."}";

                var files = Directory.EnumerateFiles(targetDir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Select(p => Path.GetRelativePath(normalizedWorkingDir, p))
                    .OrderBy(p => p)
                    .ToList();

                if (files.Count == 0)
                    return $"No files found in: {relDir ?? "."}";

                var result = new System.Text.StringBuilder();
                result.AppendLine($"Files in {(string.IsNullOrEmpty(relDir) ? "." : relDir)}:");
                result.AppendLine();

                foreach (var file in files)
                    result.AppendLine($"  {file}");

                result.AppendLine();
                result.AppendLine($"Total: {files.Count} file(s)");

                return result.ToString();
            }
            catch (Exception ex)
            {
                return $"Error listing files: {ex.Message}";
            }
        }
    }
}
