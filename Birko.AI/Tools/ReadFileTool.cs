using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class ReadFileTool : Tool
    {
        public override string Name => "read_file";
        public override string Description => "Read the contents of a file in the workspace.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new { type = "string", description = "Path to the file relative to workspace root" }
            },
            required = new[] { "file_path" }
        };

        public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
        {
            try
            {
                var filePath = input["file_path"].ToString()?.Trim();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("file_path is required");

                var (normalizedWorkingDir, fullPath) = WorkspacePath.Resolve(workingDirectory, filePath);

                if (!PathHelper.IsPathSafe(fullPath, normalizedWorkingDir, Options?.AllowedExternalPaths))
                    return Task.FromResult("Error: Access denied. Path must be in workspace or an allowed external path.");

                return Task.FromResult(File.ReadAllText(fullPath));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error: reading file failed: {ex.Message}");
            }
        }
    }
}
