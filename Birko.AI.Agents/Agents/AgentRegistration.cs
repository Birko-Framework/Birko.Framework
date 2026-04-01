using Birko.AI.Agents.Coding;
using Birko.AI.Agents.Coding.Specialized;
using Birko.AI.Agents.Media;
using Birko.AI.Factories;
using Birko.AI.Providers;

namespace Birko.AI.Agents
{
    /// <summary>
    /// Registers all built-in agents with AgentFactory and provides convenience creation methods.
    /// Call RegisterAll() at application startup.
    /// </summary>
    public static class AgentRegistration
    {
        private static bool _registered;

        /// <summary>
        /// Agent types that benefit from coding-optimized endpoints (e.g., Z.AI coding endpoint).
        /// </summary>
        private static readonly HashSet<string> CodingAgentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "coding", "csharp", "cpp", "assembler", "javascript", "typescript",
            "css", "html", "react", "angular", "php", "python", "debug",
            "refactor", "test", "svg"
        };

        /// <summary>
        /// Checks if an agent type is a coding agent.
        /// </summary>
        public static bool IsCodingAgent(string agentType) => CodingAgentTypes.Contains(agentType);

        /// <summary>
        /// Register all built-in agents with AgentFactory.
        /// Also ensures providers are registered via ProviderRegistration.RegisterAll().
        /// Safe to call multiple times — subsequent calls are no-ops.
        /// </summary>
        public static void RegisterAll()
        {
            if (_registered)
                return;

            ProviderRegistration.RegisterAll();

            // Core coding agents
            AgentFactory.Register("coding", (llm, opts) => new CodingAgent(llm, opts));

            // Language-specific coding agents
            AgentFactory.Register("csharp", (llm, opts) => new CSharpCodingAgent(llm, opts));
            AgentFactory.Register("cpp", (llm, opts) => new CppCodingAgent(llm, opts));
            AgentFactory.Register("assembler", (llm, opts) => new AssemblerCodingAgent(llm, opts));
            AgentFactory.Register("javascript", (llm, opts) => new JavaScriptTypeScriptCodingAgent(llm, opts));
            AgentFactory.Register("typescript", (llm, opts) => new JavaScriptTypeScriptCodingAgent(llm, opts));
            AgentFactory.Register("css", (llm, opts) => new CssCodingAgent(llm, opts));
            AgentFactory.Register("html", (llm, opts) => new HtmlCodingAgent(llm, opts));
            AgentFactory.Register("react", (llm, opts) => new ReactCodingAgent(llm, opts));
            AgentFactory.Register("angular", (llm, opts) => new AngularCodingAgent(llm, opts));
            AgentFactory.Register("php", (llm, opts) => new PhpCodingAgent(llm, opts));
            AgentFactory.Register("python", (llm, opts) => new PythonCodingAgent(llm, opts));

            // Task agents
            AgentFactory.Register("documentation", (llm, opts) => new DocumentationAgent(llm, opts));
            AgentFactory.Register("debug", (llm, opts) => new DebugAgent(llm, opts));
            AgentFactory.Register("refactor", (llm, opts) => new RefactorAgent(llm, opts));
            AgentFactory.Register("test", (llm, opts) => new TestAgent(llm, opts));

            // Media agents
            AgentFactory.Register("diagramming", (llm, opts) => new DiagrammingAgent(llm, opts));
            AgentFactory.Register("media", (llm, opts) => new MediaAgent(llm, opts));
            AgentFactory.Register("image", (llm, opts) => new ImageAgent(llm, opts));
            AgentFactory.Register("svg", (llm, opts) => new SvgAgent(llm, opts));
            AgentFactory.Register("bitmap", (llm, opts) => new BitmapAgent(llm, opts));

            // Aliases
            AgentFactory.RegisterAlias("general", "coding");
            AgentFactory.RegisterAlias("docs", "documentation");
            AgentFactory.RegisterAlias("debugging", "debug");
            AgentFactory.RegisterAlias("refactoring", "refactor");
            AgentFactory.RegisterAlias("testing", "test");
            AgentFactory.RegisterAlias("diagram", "diagramming");

            _registered = true;
        }

        /// <summary>
        /// Convenience method: create an Agent by provider name, config, and agent type.
        /// Uses LlmProviderFactory for provider creation and AgentFactory for agent creation.
        /// Ensures both registrations are initialized.
        /// </summary>
        /// <param name="provider">Provider name: "openai", "claude", "gemini", etc.</param>
        /// <param name="options">Optional agent options</param>
        /// <param name="config">Provider configuration (apiKey, model, baseUrl, etc.)</param>
        /// <param name="agentType">Agent type (default: "coding")</param>
        /// <returns>Agent instance</returns>
        public static Agent Create(
            string provider,
            AgentOptions? options = null,
            Dictionary<string, string>? config = null,
            string agentType = "coding")
        {
            EnsureRegistered();

            config ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // For Z.AI, inject coding endpoint hint based on agent type
            if (provider.Equals("zai", StringComparison.OrdinalIgnoreCase)
                || provider.Equals("zhipu", StringComparison.OrdinalIgnoreCase)
                || provider.Equals("zhipuai", StringComparison.OrdinalIgnoreCase))
            {
                if (IsCodingAgent(AgentFactory.ResolveAgentType(agentType))
                    && !config.ContainsKey("useCodingEndpoint"))
                {
                    config["useCodingEndpoint"] = "true";
                }
            }

            var llm = LlmProviderFactory.Create(provider, config);
            return AgentFactory.Create(llm, options, agentType);
        }

        /// <summary>
        /// Ensure both provider and agent registrations are initialized.
        /// </summary>
        private static void EnsureRegistered()
        {
            RegisterAll();
        }
    }
}
