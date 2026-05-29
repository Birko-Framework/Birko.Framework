using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class AppendToFileTool : Tool
    {
        public override string Name => "append_to_file";
        public override string Description => "Append content to the end of an existing file. Creates the file if it doesn't exist.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new { type = "string", description = "Path to the file relative to workspace root" },
                content = new { type = "string", description = "Content to append" },
                create_directories = new { type = "boolean", description = "Create directories if they do not exist", @default = true }
            },
            required = new[] { "file_path", "content" }
        };

        public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input)
        {
            try
            {
                var filePath = input["file_path"].ToString();
                var content = input.TryGetValue("content", out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
                var createDirs = !input.TryGetValue("create_directories", out var cdv) || !bool.TryParse(cdv?.ToString(), out var cdp) || cdp;

                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("file_path is required");

                var fullPath = Path.Combine(workingDirectory, filePath);

                if (!PathHelper.IsPathSafe(fullPath, workingDirectory, Options?.AllowedExternalPaths))
                    return Task.FromResult("Error: Access denied. Path must be in workspace or an allowed external path.");

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    if (createDirs) Directory.CreateDirectory(dir);
                    else return Task.FromResult($"Error: Directory does not exist: {dir}");
                }

                File.AppendAllText(fullPath, content);
                return Task.FromResult("OK");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error appending to file: {ex.Message}");
            }
        }
    }
}
