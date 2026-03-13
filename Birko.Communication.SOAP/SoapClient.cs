using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Birko.Communication.SOAP
{
    /// <summary>
    /// SOAP client for consuming web services
    /// </summary>
    public class SoapClient : IDisposable
    {
        private static readonly Dictionary<string, SoapClient> _clients = new Dictionary<string, SoapClient>();

        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// Gets the URI endpoint for this SOAP client
        /// </summary>
        public string URI { get; private set; }

        /// <summary>
        /// Gets or sets the default credentials for authentication
        /// </summary>
        public ICredentials? Credentials { get; set; }

        /// <summary>
        /// Gets or sets the timeout for requests in milliseconds
        /// </summary>
        public int Timeout
        {
            get => (int)_httpClient.Timeout.TotalMilliseconds;
            set => _httpClient.Timeout = TimeSpan.FromMilliseconds(value);
        }

        /// <summary>
        /// Event raised when a SOAP request is sent
        /// </summary>
        public event EventHandler<SoapRequestEventArgs>? OnRequest;

        /// <summary>
        /// Event raised when a SOAP response is received
        /// </summary>
        public event EventHandler<SoapResponseEventArgs>? OnResponse;

        /// <summary>
        /// Gets a cached SOAP client instance for the specified URI
        /// </summary>
        /// <param name="uri">The SOAP service endpoint URI</param>
        /// <returns>A cached or new SoapClient instance</returns>
        public static SoapClient GetClient(string uri)
        {
            if (!_clients.ContainsKey(uri))
            {
                _clients.Add(uri, new SoapClient(uri));
            }
            return _clients[uri];
        }

        /// <summary>
        /// Initializes a new instance of the SoapClient class
        /// </summary>
        /// <param name="uri">The SOAP service endpoint URI</param>
        public SoapClient(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentNullException(nameof(uri));

            URI = uri;

            var handler = new HttpClientHandler();
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(100000) // 100 seconds default
            };
        }

        /// <summary>
        /// Initializes a new instance of the SoapClient class with credentials
        /// </summary>
        /// <param name="uri">The SOAP service endpoint URI</param>
        /// <param name="credentials">The credentials for authentication</param>
        public SoapClient(string uri, ICredentials credentials) : this(uri)
        {
            Credentials = credentials;
        }

        /// <summary>
        /// Sends a SOAP request and returns the response as a string
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <returns>The response XML as a string</returns>
        public string SendRequest(string action, string xml, ICredentials? credentials = null)
        {
            return SendRequestAsync(action, xml, credentials).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Sends a SOAP request asynchronously and returns the response as a string
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response XML as a string</returns>
        public async Task<string> SendRequestAsync(string action, string xml, ICredentials? credentials = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentNullException(nameof(action));

            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentNullException(nameof(xml));

            using var request = CreateRequest(action, xml, credentials);

            OnRequest?.Invoke(this, new SoapRequestEventArgs(action, xml));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseXml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            OnResponse?.Invoke(this, new SoapResponseEventArgs(responseXml, response.StatusCode));

            return responseXml;
        }

        /// <summary>
        /// Sends a SOAP request and parses the response into an XmlDocument
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <returns>The response as an XmlDocument</returns>
        public XmlDocument SendRequestXml(string action, string xml, ICredentials? credentials = null)
        {
            var responseXml = SendRequest(action, xml, credentials);
            var doc = new XmlDocument();
            doc.LoadXml(responseXml);
            return doc;
        }

        /// <summary>
        /// Sends a SOAP request asynchronously and parses the response into an XmlDocument
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response as an XmlDocument</returns>
        public async Task<XmlDocument> SendRequestXmlAsync(string action, string xml, ICredentials? credentials = null, CancellationToken cancellationToken = default)
        {
            var responseXml = await SendRequestAsync(action, xml, credentials, cancellationToken).ConfigureAwait(false);
            var doc = new XmlDocument();
            doc.LoadXml(responseXml);
            return doc;
        }

        /// <summary>
        /// Creates an HttpRequestMessage for the SOAP action
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <returns>A configured HttpRequestMessage</returns>
        protected virtual HttpRequestMessage CreateRequest(string action, string xml, ICredentials? credentials = null)
        {
            var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, URI);
            request.Content = new StringContent(xml, Encoding.UTF8, "text/xml");
            request.Headers.Add("SOAPAction", action);

            return request;
        }

        /// <summary>
        /// Clears all cached SOAP clients
        /// </summary>
        public static void ClearCache()
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
        }

        /// <summary>
        /// Removes a specific cached SOAP client
        /// </summary>
        /// <param name="uri">The URI of the client to remove</param>
        /// <returns>True if the client was removed; otherwise, false</returns>
        public static bool RemoveClient(string uri)
        {
            if (_clients.TryGetValue(uri, out var client))
            {
                client.Dispose();
                return _clients.Remove(uri);
            }
            return false;
        }

        /// <summary>
        /// Disposes the underlying HttpClient
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for SOAP requests
    /// </summary>
    public class SoapRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the SOAP action
        /// </summary>
        public string Action { get; }

        /// <summary>
        /// Gets the XML payload
        /// </summary>
        public string Xml { get; }

        /// <summary>
        /// Gets the timestamp of the request
        /// </summary>
        public DateTime Timestamp { get; }

        public SoapRequestEventArgs(string action, string xml)
        {
            Action = action;
            Xml = xml;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for SOAP responses
    /// </summary>
    public class SoapResponseEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the response XML
        /// </summary>
        public string Xml { get; }

        /// <summary>
        /// Gets the HTTP status code
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Gets the timestamp of the response
        /// </summary>
        public DateTime Timestamp { get; }

        public SoapResponseEventArgs(string xml, HttpStatusCode statusCode)
        {
            Xml = xml;
            StatusCode = statusCode;
            Timestamp = DateTime.UtcNow;
        }
    }
}
