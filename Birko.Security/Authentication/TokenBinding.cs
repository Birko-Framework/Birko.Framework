using System.Collections.Generic;

namespace Birko.Security.Authentication
{
    /// <summary>
    /// Token binding with IP restriction
    /// </summary>
    public class TokenBinding
    {
        /// <summary>
        /// Gets or sets the authentication token
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of allowed IP addresses for this token
        /// </summary>
        public List<string> AllowedIps { get; set; } = new();
    }
}
