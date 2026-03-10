using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Birko.Communication.SOAP
{
    /// <summary>
    /// SOAP client for consuming web services
    /// </summary>
    public class SoapClient
    {
        private static readonly Dictionary<string, SoapClient> _clients = new Dictionary<string, SoapClient>();

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
        public int Timeout { get; set; } = 100000; // 100 seconds default

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
            var request = CreateRequest(action, xml, credentials);
            OnRequest?.Invoke(this, new SoapRequestEventArgs(action, xml));

            using var response = (HttpWebResponse)request.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream ?? throw new InvalidOperationException("No response stream"));

            var responseXml = reader.ReadToEnd();
            OnResponse?.Invoke(this, new SoapResponseEventArgs(responseXml, response.StatusCode));

            return responseXml;
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
            var request = CreateRequest(action, xml, credentials);
            OnRequest?.Invoke(this, new SoapRequestEventArgs(action, xml));

            using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream ?? throw new InvalidOperationException("No response stream"));

            var responseXml = await reader.ReadToEndAsync().ConfigureAwait(false);
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
        /// Creates a web request for the SOAP action
        /// </summary>
        /// <param name="action">The SOAP action to perform</param>
        /// <param name="xml">The XML payload</param>
        /// <param name="credentials">Optional credentials for authentication</param>
        /// <returns>A configured WebRequest</returns>
        protected virtual WebRequest CreateRequest(string action, string xml, ICredentials? credentials = null)
        {
            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentNullException(nameof(action));

            if (string.IsNullOrWhiteSpace(xml))
                throw new ArgumentNullException(nameof(xml));

            var wr = (HttpWebRequest)WebRequest.Create(URI);
            wr.ContentType = "text/xml;charset=utf-8";
            wr.Method = "POST";
            wr.Timeout = Timeout;
            wr.Headers.Add("SOAPAction", action);
            wr.Credentials = credentials ?? Credentials;

            var bytes = Encoding.UTF8.GetBytes(xml);
            wr.ContentLength = bytes.Length;

            using var stream = wr.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);

            return wr;
        }

        /// <summary>
        /// Clears all cached SOAP clients
        /// </summary>
        public static void ClearCache()
        {
            _clients.Clear();
        }

        /// <summary>
        /// Removes a specific cached SOAP client
        /// </summary>
        /// <param name="uri">The URI of the client to remove</param>
        /// <returns>True if the client was removed; otherwise, false</returns>
        public static bool RemoveClient(string uri)
        {
            return _clients.Remove(uri);
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
