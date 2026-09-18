using System;
using System.Xml;
using Microsoft.Extensions.Logging;
using Birko.Security.Authentication;

namespace Birko.Communication.SOAP.Middleware
{
    /// <summary>
    /// SOAP-specific authentication adapter that extracts tokens from SOAP envelopes
    /// </summary>
    public class SoapAuthenticationService
    {
        private readonly AuthenticationService _authService;
        private readonly SoapAuthenticationConfiguration _config;
        private readonly ILogger<SoapAuthenticationService>? _logger;

        /// <summary>
        /// Initializes a new instance of the SoapAuthenticationService class
        /// </summary>
        /// <param name="config">The SOAP authentication configuration</param>
        /// <param name="logger">Logger for this service</param>
        /// <param name="authLogger">Logger for the authentication service</param>
        public SoapAuthenticationService(
            SoapAuthenticationConfiguration config,
            ILogger<SoapAuthenticationService>? logger = null,
            ILogger<AuthenticationService>? authLogger = null)
        {
            _config = config ?? new SoapAuthenticationConfiguration();
            _logger = logger;
            _authService = new AuthenticationService(_config, authLogger);
        }

        /// <summary>
        /// Checks if authentication is enabled
        /// </summary>
        public bool IsAuthenticationEnabled() => _authService.IsAuthenticationEnabled();

        /// <summary>
        /// Validates a token
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <param name="clientIp">The client IP address</param>
        /// <returns>True if valid; otherwise, false</returns>
        public bool ValidateToken(string? token, string? clientIp) => _authService.ValidateToken(token, clientIp);

        /// <summary>
        /// Extracts the authentication token from a SOAP envelope
        /// </summary>
        /// <param name="soapEnvelope">The SOAP envelope XML</param>
        /// <returns>The extracted token or null</returns>
        public string? ExtractTokenFromSoapEnvelope(string soapEnvelope)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(soapEnvelope);

                var nsManager = new XmlNamespaceManager(doc.NameTable);
                nsManager.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");

                // Try to extract token from SOAP header
                var headerNode = doc.SelectSingleNode("//soap:Header", nsManager);
                if (headerNode != null && !string.IsNullOrEmpty(_config.SoapHeaderName))
                {
                    var authNode = headerNode.SelectSingleNode($"*[local-name()='{_config.SoapHeaderName}']");
                    if (authNode != null && !string.IsNullOrEmpty(_config.TokenElementName))
                    {
                        var tokenNode = authNode.SelectSingleNode($"*[local-name()='{_config.TokenElementName}']");
                        if (tokenNode != null)
                        {
                            return tokenNode.InnerText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error extracting token from SOAP envelope");
            }

            return null;
        }

        /// <summary>
        /// Extracts the authentication token from a query string
        /// </summary>
        /// <param name="queryString">The query string</param>
        /// <returns>The extracted token or null</returns>
        public string? ExtractTokenFromQueryString(string queryString)
        {
            if (!_config.AllowQueryToken || string.IsNullOrEmpty(_config.QueryTokenName))
            {
                return null;
            }

            try
            {
                var parameters = queryString.TrimStart('?').Split('&');
                foreach (var param in parameters)
                {
                    // Split on the first '=' only so a token whose value contains '=' (base64 padding
                    // like "abc==") isn't rejected by the old parts.Length == 2 guard (CR-L088).
                    var idx = param.IndexOf('=');
                    if (idx > 0 && param[..idx] == _config.QueryTokenName)
                    {
                        return Uri.UnescapeDataString(param[(idx + 1)..]);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error extracting token from query string");
            }

            return null;
        }

        /// <summary>
        /// Extracts the client IP address from an HTTP listener request
        /// </summary>
        /// <param name="request">The HTTP listener request</param>
        /// <returns>The client IP address or null</returns>
        public string? GetClientIpAddress(System.Net.HttpListenerRequest request)
        {
            return AuthenticationService.GetClientIpAddress(
                request.Headers.Get,
                request.UserHostAddress
            );
        }

        /// <summary>
        /// Creates a SOAP fault response for authentication failures
        /// </summary>
        /// <param name="message">The error message</param>
        /// <returns>A SOAP fault envelope</returns>
        public static string CreateAuthenticationFault(string message)
            => SoapXml.BuildFault("Client", message); // CR-L086: shared helper

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose() => _authService.Dispose();
    }
}
