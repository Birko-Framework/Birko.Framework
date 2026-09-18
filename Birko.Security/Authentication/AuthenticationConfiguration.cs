using System.Collections.Generic;

namespace Birko.Security.Authentication
{
    /// <summary>
    /// Base configuration for authentication services
    /// </summary>
    public abstract class AuthenticationConfiguration
    {
        /// <summary>
        /// Gets or sets whether authentication is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the list of valid tokens (supports environment variables like ${ENV_VAR})
        /// </summary>
        public List<string> Tokens { get; set; } = new();

        /// <summary>
        /// Gets or sets the list of token bindings with IP restrictions
        /// </summary>
        public List<TokenBinding> TokenBindings { get; set; } = new();
    }
}
