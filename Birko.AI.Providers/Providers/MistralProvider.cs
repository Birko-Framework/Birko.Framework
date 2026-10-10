using Birko.AI.Models;

namespace Birko.AI.Providers
{
    /// <summary>
    /// Provider for Mistral AI models (OpenAI-compatible API).
    /// Default endpoint: https://api.mistral.ai/v1/chat/completions
    /// </summary>
    public class MistralProvider : OpenAiCompatibleProviderBase
    {
        protected override string ProviderName => "Mistral";

        public MistralProvider(string apiKey, string model = "mistral-large-latest", string? baseUrl = null)
            : base(model, baseUrl ?? "https://api.mistral.ai/v1/chat/completions", apiKey)
        {
        }

        /// <summary>
        /// Mistral rejects a <c>user</c> message right after a <c>tool</c> message ("Unexpected role 'user' after
        /// role 'tool'"), which is what a hook's <c>AppendUserMessage</c> after a tool batch produces. A tool and a
        /// user message cannot be merged, so a placeholder assistant message (<c>" "</c>; an empty one is not
        /// accepted) goes between them — the request only, never the agent's conversation (TASK-546; the same
        /// workaround Zed ships, zed-industries/zed#31736).
        /// </summary>
        protected override List<object> BuildMessages(List<Message> messages, string systemPrompt)
        {
            var built = base.BuildMessages(messages, systemPrompt);
            var result = new List<object>(built.Count);
            for (var i = 0; i < built.Count; i++)
            {
                result.Add(built[i]);
                if (RoleOf(built[i]) == "tool" && i + 1 < built.Count && RoleOf(built[i + 1]) == "user")
                    result.Add(new { role = "assistant", content = " " });
            }
            return result;
        }

        // The built messages are anonymous objects, each with a `role` property.
        private static string? RoleOf(object message)
            => message.GetType().GetProperty("role")?.GetValue(message) as string;
    }
}
