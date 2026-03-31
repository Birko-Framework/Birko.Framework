namespace Birko.AI.Tools
{
    public class AskUserTool : Tool
    {
        public override string Name => "ask_user";
        public override string Description => "Ask the user for additional input or clarification.";
        public override object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                question = new { type = "string", description = "The question to show the user" },
                context = new { type = "string", description = "Optional context to help the user understand why you're asking" }
            },
            required = new[] { "question" }
        };

        public Func<string, string, Task<string>>? PromptCallback { get; set; }

        public override string Execute(string workingDirectory, Dictionary<string, object> input)
        {
            try
            {
                var question = input["question"].ToString();
                var context = input.TryGetValue("context", out var contextVal) ? contextVal?.ToString() : null;

                if (string.IsNullOrWhiteSpace(question))
                    return "Error: question parameter is required";

                if (Options != null && !Options.Interactive)
                {
                    if (!string.IsNullOrEmpty(Options.DefaultPromptResponse))
                    {
                        SendMessage("info", $"[Non-Interactive] Auto-responding to: {question}");
                        return Options.DefaultPromptResponse;
                    }
                    else
                    {
                        return "Error: Cannot prompt for user input in non-interactive mode.";
                    }
                }

                if (PromptCallback != null)
                {
                    var fullPrompt = string.IsNullOrWhiteSpace(context)
                        ? question
                        : $"Context: {context}\n\nQuestion: {question}";

                    SendMessage("prompt", fullPrompt ?? "");

                    var timeout = Options?.PromptTimeout ?? 300;
                    var promptTask = PromptCallback(question ?? "", context ?? "");
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));

                    var completedTask = Task.WhenAny(promptTask, timeoutTask).GetAwaiter().GetResult();

                    if (completedTask == timeoutTask)
                        return $"Error: Prompt timed out after {timeout} seconds.";

                    return promptTask.GetAwaiter().GetResult();
                }

                var promptMsg = string.IsNullOrWhiteSpace(context)
                    ? $"Question: {question}"
                    : $"Context: {context}\n\nQuestion: {question}";

                SendMessage("prompt_console", promptMsg);

                return "Error: Console input not available in library mode. Use PromptCallback for interactive prompts.";
            }
            catch (Exception ex)
            {
                return $"Error getting user input: {ex.Message}";
            }
        }
    }
}
