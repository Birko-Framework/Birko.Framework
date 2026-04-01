using Birko.AI.Providers;

namespace Birko.AI.Factories
{
    /// <summary>
    /// Registration-based factory for creating LLM provider instances.
    /// Consumers register provider factory delegates to avoid transitive dependencies.
    /// </summary>
    public static class LlmProviderFactory
    {
        private static readonly Dictionary<string, Func<Dictionary<string, string>?, ILlmProvider>> _factories = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register a provider factory delegate.
        /// </summary>
        /// <param name="providerName">Provider name (e.g., "claude", "openai", "copilot")</param>
        /// <param name="factory">Factory function that creates the provider instance</param>
        public static void Register(string providerName, Func<Dictionary<string, string>?, ILlmProvider> factory)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name cannot be empty", nameof(providerName));

            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            _factories[providerName] = factory;
        }

        /// <summary>
        /// Create a provider instance by name.
        /// </summary>
        /// <param name="providerName">Provider name</param>
        /// <param name="config">Optional configuration dictionary</param>
        /// <returns>ILlmProvider instance</returns>
        public static ILlmProvider Create(string providerName, Dictionary<string, string>? config = null)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name cannot be empty", nameof(providerName));

            if (!_factories.TryGetValue(providerName, out var factory))
                throw new ArgumentException($"Provider '{providerName}' is not registered. Available providers: {string.Join(", ", _factories.Keys)}");

            return factory(config);
        }

        /// <summary>
        /// Check if a provider is registered.
        /// </summary>
        public static bool IsRegistered(string providerName)
        {
            return !string.IsNullOrWhiteSpace(providerName) && _factories.ContainsKey(providerName);
        }

        /// <summary>
        /// Get all registered provider names.
        /// </summary>
        public static IEnumerable<string> GetRegisteredProviders()
        {
            return _factories.Keys;
        }
    }
}
