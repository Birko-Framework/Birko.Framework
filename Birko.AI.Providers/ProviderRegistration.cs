using Birko.AI.Factories;
using Birko.Communication.OAuth;
using Birko.Communication.OAuth.Providers;

namespace Birko.AI.Providers
{
    /// <summary>
    /// Registers all built-in LLM providers with LlmProviderFactory.
    /// Call RegisterAll() at application startup.
    /// </summary>
    public static class ProviderRegistration
    {
        private static bool _registered;

        /// <summary>
        /// Register all built-in providers with LlmProviderFactory.
        /// Safe to call multiple times — subsequent calls are no-ops.
        /// </summary>
        public static void RegisterAll()
        {
            if (_registered)
                return;

            LlmProviderFactory.Register("openai", config =>
            {
                var c = new ConfigHelper(config);
                return new OpenAiProvider(c.Get("apiKey"), c.Get("model", "gpt-4o"), c.Get("baseUrl", "https://api.openai.com/v1/chat/completions"));
            });

            LlmProviderFactory.Register("azureopenai", config =>
            {
                var c = new ConfigHelper(config);
                return new AzureOpenAiProvider(c.Get("endpoint"), c.Get("apiKey"), c.Get("deployment", "gpt-4"));
            });

            LlmProviderFactory.Register("claude", config =>
            {
                var c = new ConfigHelper(config);
                return new ClaudeProvider(c.Get("apiKey"), c.Get("model", "claude-3-5-sonnet-latest"), c.Get("baseUrl", "https://api.anthropic.com/v1/messages"));
            });

            LlmProviderFactory.Register("gemini", config =>
            {
                var c = new ConfigHelper(config);
                return new GeminiProvider(c.Get("apiKey"), c.Get("model", "gemini-2.0-flash-exp"), c.Get("baseUrl", "https://generativelanguage.googleapis.com/v1beta/models/"));
            });

            LlmProviderFactory.Register("ollama", config =>
            {
                var c = new ConfigHelper(config);
                return new OllamaProvider(c.Get("model", "llama3.2"), c.Get("baseUrl", "http://localhost:11434"));
            });

            LlmProviderFactory.Register("llamacpp", config =>
            {
                var c = new ConfigHelper(config);
                return new LlamaCppProvider(c.Get("model", "default"), c.Get("baseUrl", "http://localhost:8080"), c.GetOrNull("apiKey"));
            });

            LlmProviderFactory.Register("vllm", config =>
            {
                var c = new ConfigHelper(config);
                return new VllmProvider(c.Get("model", "default"), c.Get("baseUrl", "http://localhost:8000"), c.GetOrNull("apiKey"));
            });

            LlmProviderFactory.Register("sglang", config =>
            {
                var c = new ConfigHelper(config);
                return new SglangProvider(c.Get("model", "default"), c.Get("baseUrl", "http://localhost:30000"), c.GetOrNull("apiKey"));
            });

            LlmProviderFactory.Register("githubcopilot", config =>
            {
                var c = new ConfigHelper(config);
                return new GitHubCopilotProvider(
                    GitHubOAuthProvider.CreateDeviceFlowClient(c.Get("clientId")),
                    c.Get("model", "gpt-4o"),
                    c.Get("baseUrl", "https://api.githubcopilot.com/chat/completions"));
            });

            LlmProviderFactory.Register("zai", config =>
            {
                var c = new ConfigHelper(config);
                return new ZAiProvider(
                    c.Get("apiKey"),
                    c.Get("model", ZAiProvider.DefaultModel),
                    c.GetOrNull("baseUrl"),
                    c.Get("deepThinking", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                    c.Get("useCodingEndpoint", "false").Equals("true", StringComparison.OrdinalIgnoreCase));
            });

            // Aliases
            LlmProviderFactory.Register("zhipu", config => LlmProviderFactory.Create("zai", config));
            LlmProviderFactory.Register("zhipuai", config => LlmProviderFactory.Create("zai", config));

            _registered = true;
        }

        /// <summary>
        /// Helper to extract config values with defaults.
        /// </summary>
        private readonly struct ConfigHelper
        {
            private readonly Dictionary<string, string>? _config;

            public ConfigHelper(Dictionary<string, string>? config)
            {
                _config = config;
            }

            public string Get(string key, string defaultValue = "")
            {
                if (_config != null && _config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
                return defaultValue;
            }

            public string? GetOrNull(string key)
            {
                if (_config != null && _config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
                return null;
            }
        }
    }
}
