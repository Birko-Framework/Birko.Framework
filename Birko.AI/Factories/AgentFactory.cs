using Birko.AI.Agents;
using Birko.AI.Providers;

namespace Birko.AI.Factories
{
    /// <summary>
    /// Registration-based factory for creating agent instances.
    /// Consumers register agent factory delegates to avoid transitive dependencies on concrete agent types.
    /// Use LlmProviderFactory to create providers, then pass them here.
    /// </summary>
    public static class AgentFactory
    {
        private static readonly Dictionary<string, Func<ILlmProvider, AgentOptions, Agent>> _factories = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register an agent factory delegate.
        /// </summary>
        /// <param name="agentType">Agent type name (e.g., "coding", "csharp", "debug")</param>
        /// <param name="factory">Factory function that creates the agent instance</param>
        public static void Register(string agentType, Func<ILlmProvider, AgentOptions, Agent> factory)
        {
            if (string.IsNullOrWhiteSpace(agentType))
                throw new ArgumentException("Agent type cannot be empty", nameof(agentType));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _factories[agentType] = factory;
        }

        /// <summary>
        /// Register an alias that maps to a primary agent type.
        /// </summary>
        /// <param name="alias">Alias name (e.g., "general", "docs")</param>
        /// <param name="primaryType">Primary agent type the alias resolves to</param>
        public static void RegisterAlias(string alias, string primaryType)
        {
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException("Alias cannot be empty", nameof(alias));

            if (string.IsNullOrWhiteSpace(primaryType))
                throw new ArgumentException("Primary type cannot be empty", nameof(primaryType));

            _aliases[alias] = primaryType;
        }

        /// <summary>
        /// Resolve agent type alias to primary agent type.
        /// Returns the input if it is not an alias.
        /// </summary>
        public static string ResolveAgentType(string agentType)
        {
            if (_aliases.TryGetValue(agentType, out var primaryType))
                return primaryType;
            return agentType;
        }

        /// <summary>
        /// Create an Agent with a pre-configured LLM provider.
        /// </summary>
        /// <param name="llmProvider">The LLM provider instance</param>
        /// <param name="options">Optional agent options</param>
        /// <param name="agentType">Agent type (default: "coding")</param>
        /// <returns>Agent instance</returns>
        public static Agent Create(
            ILlmProvider llmProvider,
            AgentOptions? options = null,
            string agentType = "coding")
        {
            if (llmProvider == null)
                throw new ArgumentNullException(nameof(llmProvider));

            options ??= new AgentOptions();
            var resolved = ResolveAgentType(agentType);

            if (!_factories.TryGetValue(resolved, out var factory))
                throw new ArgumentException($"Agent type '{agentType}' is not registered. Available: {string.Join(", ", _factories.Keys)}");

            return factory(llmProvider, options);
        }

        /// <summary>
        /// Check if an agent type (or alias) is registered.
        /// </summary>
        public static bool IsRegistered(string agentType)
        {
            if (string.IsNullOrWhiteSpace(agentType))
                return false;

            var resolved = ResolveAgentType(agentType);
            return _factories.ContainsKey(resolved);
        }

        /// <summary>
        /// Get all registered agent type names (primary names only, not aliases).
        /// </summary>
        public static IEnumerable<string> GetRegisteredAgentTypes()
        {
            return _factories.Keys;
        }

        /// <summary>
        /// Get all registered aliases.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetRegisteredAliases()
        {
            return _aliases;
        }
    }
}
