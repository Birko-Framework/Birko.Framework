using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class WriteFileTool : Tool
    {
        public override string Name => "write_file";
        public override string Description => "Write text content to a file in the workspace. Use edit_file for existing files.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new { type = "string", description = "Path to the file relative to workspace root" },
                content = new { type = "string", description = "Text content to write" },
                create_directories = new { type = "boolean", description = "Create directories if they do not exist", @default = true },
                check_exists = new { type = "boolean", description = "Warn if file already exists", @default = true }
            },
            required = new[] { "file_path", "content" }
        };

        public override string Execute(string workingDirectory, Dictionary<string, object> input)
        {
            try
            {
                var filePath = input["file_path"].ToString();
                var content = input.TryGetValue("content", out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
                var createDirs = !input.TryGetValue("create_directories", out var cdv) || !bool.TryParse(cdv?.ToString(), out var cdp) || cdp;
                var checkExists = !input.TryGetValue("check_exists", out var cev) || !bool.TryParse(cev?.ToString(), out var cep) || cep;

                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("file_path is required");

                var fullPath = Path.Combine(workingDirectory, filePath);

                if (!PathHelper.IsPathSafe(fullPath, workingDirectory, Options?.AllowedExternalPaths))
                    return "Error: Access denied. Path must be in workspace or an allowed external path.";

                if (checkExists && File.Exists(fullPath))
                    return $"Warning: File '{filePath}' already exists. Use edit_file to modify it, or call write_file with check_exists=false to overwrite.";

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    if (createDirs) Directory.CreateDirectory(dir);
                    else return $"Error: Directory does not exist: {dir}";
                }

                File.WriteAllText(fullPath, content);
                return "OK";
            }
            catch (Exception ex)
            {
                return $"Error writing file: {ex.Message}";
            }
        }
    }
}
