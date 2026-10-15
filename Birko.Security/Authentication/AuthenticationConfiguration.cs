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

        /// <summary>
        /// Gets or sets the proxies whose forwarding headers are believed: an address (<c>10.0.0.5</c>) or a CIDR range
        /// (<c>10.0.0.0/8</c>), environment variables like <c>${ENV_VAR}</c> supported. Empty by default, which trusts
        /// no proxy: the client address is the connection's own, and <c>X-Forwarded-For</c>, <c>X-Real-IP</c> and
        /// <c>CF-Connecting-IP</c> are ignored. A caller could otherwise name any address and satisfy a token
        /// binding's allow-list (SH-M249).
        /// </summary>
        public List<string> TrustedProxies { get; set; } = new();
    }
}
