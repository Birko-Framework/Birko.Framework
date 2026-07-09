namespace Birko.AI.Tools
{
    public class DisplayTextTool : Tool
    {
        public override string Name => "display_text";
        public override string Description => "Display text or information to the user without writing to a file.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                text = new { type = "string", description = "The text to display" },
                title = new { type = "string", description = "Optional title or header" }
            },
            required = new[] { "text" }
        };

        public override Task<string> ExecuteAsync(string workingDirectory, Dictionary<string, object> input, CancellationToken cancellationToken = default)
        {
            try
            {
                var text = input["text"].ToString();
                var title = input.TryGetValue("title", out var titleVal) ? titleVal?.ToString() : null;

                if (string.IsNullOrWhiteSpace(text))
                    return Task.FromResult("Error: text parameter is required");

                var displayMsg = string.IsNullOrWhiteSpace(title) ? text : $"{title}\n{text}";
                SendMessage("display", displayMsg ?? "");

                return Task.FromResult("Text displayed successfully");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Error displaying text: {ex.Message}");
            }
        }
    }
}
