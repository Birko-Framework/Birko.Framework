using Birko.Helpers;

namespace Birko.AI.Tools
{
    public class EditFileTool : Tool
    {
        public override string Name => "edit_file";
        public override string Description => "Edit a file by replacing a specific text block. Read the file first to see actual content.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new { type = "string", description = "Path to the file relative to workspace root" },
                old_text = new { type = "string", description = "The exact text to find and replace" },
                new_text = new { type = "string", description = "The replacement text" }
            },
            required = new[] { "file_path", "old_text", "new_text" }
        };

        public override string Execute(string workingDirectory, Dictionary<string, object> input)
        {
            try
            {
                var filePath = input["file_path"].ToString();
                var oldText = input["old_text"].ToString() ?? string.Empty;
                var newText = input["new_text"].ToString() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(filePath))
                    throw new ArgumentException("file_path is required");
                if (string.IsNullOrEmpty(oldText))
                    throw new ArgumentException("old_text is required");

                var fullPath = Path.Combine(workingDirectory, filePath);

                if (!PathHelper.IsPathSafe(fullPath, workingDirectory, Options?.AllowedExternalPaths))
                    return "Error: Access denied. Path must be in workspace or an allowed external path.";

                if (!File.Exists(fullPath))
                    return $"Error: File does not exist: {filePath}";

                var content = File.ReadAllText(fullPath);

                if (!content.Contains(oldText))
                {
                    var contentPreview = content.Length > 8000
                        ? content[..8000] + $"\n\n... (truncated, {content.Length} chars total)"
                        : content;
                    return $"Error: old_text not found in file. Read the file content below and retry with exact text.\n\nFull file content:\n```\n{contentPreview}\n```";
                }

                var occurrences = CountOccurrences(content, oldText);
                if (occurrences > 1)
                    return $"Error: old_text appears {occurrences} times. Provide a more specific text block.";

                var newContent = content.Replace(oldText, newText);
                File.WriteAllText(fullPath, newContent);
                return "OK";
            }
            catch (Exception ex)
            {
                return $"Error editing file: {ex.Message}";
            }
        }

        private static int CountOccurrences(string text, string pattern)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }
    }
}
