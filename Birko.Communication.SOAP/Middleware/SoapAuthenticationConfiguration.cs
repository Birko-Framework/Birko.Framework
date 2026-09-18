using System.Collections.Generic;
using Birko.Security.Authentication;

namespace Birko.Communication.SOAP.Middleware
{
    /// <summary>
    /// Configuration for SOAP service authentication
    /// </summary>
    public class SoapAuthenticationConfiguration : AuthenticationConfiguration
    {
        /// <summary>
        /// Gets or sets the SOAP header name containing authentication (default: "Authentication")
        /// </summary>
        public string? SoapHeaderName { get; set; } = "Authentication";

        /// <summary>
        /// Gets or sets the XML element name containing the token (default: "Token")
        /// </summary>
        public string? TokenElementName { get; set; } = "Token";

        /// <summary>
        /// Gets or sets whether to allow tokens via query parameter (default: true)
        /// </summary>
        public bool AllowQueryToken { get; set; } = true;

        /// <summary>
        /// Gets or sets the query parameter name for tokens (default: "token")
        /// </summary>
        public string? QueryTokenName { get; set; } = "token";
    }
}
