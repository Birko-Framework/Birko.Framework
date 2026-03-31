using Birko.AI.Agents.Coding;
using Birko.AI.Agents.Coding.Specialized;
using Birko.AI.Agents.Media;
using Birko.AI.Providers;

namespace Birko.AI.Agents
{
    public static class AgentFactory
    {
        /// <summary>
        /// Supported LLM provider names
        /// </summary>
        public static readonly string[] SupportedProviders =
            ["openai", "azureopenai", "claude", "gemini", "ollama", "llamacpp", "vllm", "sglang", "githubcopilot", "zai"];

        /// <summary>
        /// All supported agent types (primary names only, not aliases).
        /// This is the authoritative list used by AgentTypeValidator.
        /// </summary>
        public static readonly string[] SupportedAgentTypes =
        [
            "coding", "csharp", "cpp", "assembler", "javascript", "typescript",
            "css", "html", "react", "angular", "php", "python",
            "documentation", "debug", "refactor", "test",
            "diagramming", "media", "image", "svg", "bitmap"
        ];

        /// <summary>
        /// Agent type aliases that map to primary agent types.
        /// Key: alias, Value: primary agent type
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> AgentTypeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "general", "coding" },
            { "docs", "documentation" },
            { "debugging", "debug" },
            { "refactoring", "refactor" },
            { "testing", "test" },
            { "diagram", "diagramming" }
        };

        /// <summary>
        /// Agent types that benefit from coding-optimized endpoints
        /// </summary>
        private static readonly HashSet<string> CodingAgentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "coding", "csharp", "cpp", "assembler", "javascript", "typescript",
            "css", "html", "react", "angular", "php", "python", "debug",
            "refactor", "test", "svg"
        };

        /// <summary>
        /// Checks if an agent type is a coding agent
        /// </summary>
        public static bool IsCodingAgent(string agentType) => CodingAgentTypes.Contains(agentType);

        /// <summary>
        /// Creates an LLM provider instance based on provider name and configuration.
        /// </summary>
        /// <param name="provider">Provider name: openai, azureopenai, claude, gemini, ollama, llamacpp, vllm, sglang, githubcopilot</param>
        /// <param name="config">Provider configuration (apiKey, model, baseUrl, etc.)</param>
        /// <param name="agentType">Optional agent type to optimize provider settings (e.g., use coding endpoint for Z.AI)</param>
        /// <returns>ILlmProvider instance</returns>
        public static ILlmProvider CreateLlmProvider(string provider, Dictionary<string, string>? config = null, string? agentType = null)
        {
            config ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string C(string key, string def = "") =>
                config.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

            // Determine if we should use Z.AI coding endpoint
            bool useCodingEndpoint = !string.IsNullOrEmpty(agentType) && IsCodingAgent(agentType);

            return provider.ToLowerInvariant() switch
            {
                "openai" => new OpenAiProvider(C("apiKey"), C("model", "gpt-4o"), C("baseUrl", "https://api.openai.com/v1/chat/completions")),
                "azureopenai" => new AzureOpenAiProvider(C("endpoint"), C("apiKey"), C("deployment", "gpt-4")),
                "claude" => new ClaudeProvider(C("apiKey"), C("model", "claude-3-5-sonnet-latest"), C("baseUrl", "https://api.anthropic.com/v1/messages")),
                "gemini" => new GeminiProvider(C("apiKey"), C("model", "gemini-2.0-flash-exp"), C("baseUrl", "https://generativelanguage.googleapis.com/v1beta/models/")),
                "ollama" => new OllamaProvider(C("model", "llama3.2"), C("baseUrl", "http://localhost:11434")),
                "llamacpp" => new LlamaCppProvider(C("model", "default"), C("baseUrl", "http://localhost:8080"), C("apiKey")),
                "vllm" => new VllmProvider(C("model", "default"), C("baseUrl", "http://localhost:8000"), C("apiKey")),
                "sglang" => new SglangProvider(C("model", "default"), C("baseUrl", "http://localhost:30000"), C("apiKey")),
                "githubcopilot" => new GitHubCopilotProvider(C("clientId"), C("model", "gpt-4o"), C("baseUrl", "https://api.githubcopilot.com/chat/completions")),
                "zai" or "zhipu" or "zhipuai" => new ZAiProvider(
                    C("apiKey"),
                    C("model", ZAiProvider.DefaultModel),
                    config.TryGetValue("baseUrl", out var zaiUrl) && !string.IsNullOrWhiteSpace(zaiUrl) ? zaiUrl : null, // null = let ZAiProvider choose based on useCodingEndpoint
                    C("deepThinking", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
                    useCodingEndpoint),
                _ => throw new ArgumentException($"Unknown provider '{provider}'. Supported: {string.Join(", ", SupportedProviders)}")
            };
        }

        // Create an Agent with a specific provider name and configuration using AgentOptions
        // provider: "openai", "azureopenai", "claude", "gemini", "ollama", "llamacpp", "vllm", "sglang", "githubcopilot"
        // agentType: "coding", "csharp", "cpp", "assembler", "javascript", "css", "html", "react", "angular", "php", "python", "documentation", "debug", "refactor", "test", "diagramming", "media", "image", "svg", "bitmap"
        public static Agents.Agent Create(
            string provider,
            AgentOptions? options = null,
            Dictionary<string, string>? config = null,
            string agentType = "coding")
        {
            options ??= new AgentOptions();
            config ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            ILlmProvider llm = CreateLlmProvider(provider, config, agentType);

            return agentType.ToLowerInvariant() switch
            {
                "coding" or "general" => new CodingAgent(llm, options),
                "csharp" => new CSharpCodingAgent(llm, options),
                "cpp" => new CppCodingAgent(llm, options),
                "assembler" => new AssemblerCodingAgent(llm, options),
                "javascript" or "typescript" => new JavaScriptTypeScriptCodingAgent(llm, options),
                "css" => new CssCodingAgent(llm, options),
                "html" => new HtmlCodingAgent(llm, options),
                "react" => new ReactCodingAgent(llm, options),
                "angular" => new AngularCodingAgent(llm, options),
                "php" => new PhpCodingAgent(llm, options),
                "python" => new PythonCodingAgent(llm, options),
                "documentation" or "docs" => new DocumentationAgent(llm, options),
                "debug" or "debugging" => new DebugAgent(llm, options),
                "refactor" or "refactoring" => new RefactorAgent(llm, options),
                "test" or "testing" => new TestAgent(llm, options),
                "diagramming" or "diagram" => new DiagrammingAgent(llm, options),
                "media" => new MediaAgent(llm, options),
                "image" => new ImageAgent(llm, options),
                "svg" => new SvgAgent(llm, options),
                "bitmap" => new BitmapAgent(llm, options),
                _ => throw new ArgumentException($"Unknown agent type '{agentType}'. Supported: {string.Join(", ", SupportedAgentTypes)}")
            };
        }

        /// <summary>
        /// Create an Agent with a pre-configured LLM provider (e.g., wrapped with tracking).
        /// </summary>
        public static Agents.Agent Create(
            ILlmProvider llmProvider,
            AgentOptions? options = null,
            string agentType = "coding")
        {
            options ??= new AgentOptions();

            return agentType.ToLowerInvariant() switch
            {
                "coding" or "general" => new CodingAgent(llmProvider, options),
                "csharp" => new CSharpCodingAgent(llmProvider, options),
                "cpp" => new CppCodingAgent(llmProvider, options),
                "assembler" => new AssemblerCodingAgent(llmProvider, options),
                "javascript" or "typescript" => new JavaScriptTypeScriptCodingAgent(llmProvider, options),
                "css" => new CssCodingAgent(llmProvider, options),
                "html" => new HtmlCodingAgent(llmProvider, options),
                "react" => new ReactCodingAgent(llmProvider, options),
                "angular" => new AngularCodingAgent(llmProvider, options),
                "php" => new PhpCodingAgent(llmProvider, options),
                "python" => new PythonCodingAgent(llmProvider, options),
                "documentation" or "docs" => new DocumentationAgent(llmProvider, options),
                "debug" or "debugging" => new DebugAgent(llmProvider, options),
                "refactor" or "refactoring" => new RefactorAgent(llmProvider, options),
                "test" or "testing" => new TestAgent(llmProvider, options),
                "diagramming" or "diagram" => new DiagrammingAgent(llmProvider, options),
                "media" => new MediaAgent(llmProvider, options),
                "image" => new ImageAgent(llmProvider, options),
                "svg" => new SvgAgent(llmProvider, options),
                "bitmap" => new BitmapAgent(llmProvider, options),
                _ => throw new ArgumentException($"Unknown agent type '{agentType}'. Supported: {string.Join(", ", SupportedAgentTypes)}")
            };
        }
    }
}
