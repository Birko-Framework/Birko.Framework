using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.SOAP
{
    /// <summary>
    /// SOAP server using HttpListener for hosting SOAP web services
    /// </summary>
    public class SoapServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, SoapService> _services = new();
        private readonly ILogger<SoapServer>? _logger;

        public event EventHandler<SoapRequestContext>? OnRequest;

        public bool IsListening => _listener != null && _listener.IsListening;

        /// <summary>
        /// Initializes a new instance of the SoapServer class
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics</param>
        public SoapServer(ILogger<SoapServer>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts the SOAP server
        /// </summary>
        /// <param name="uriPrefix">The URI prefix to listen on (e.g., http://localhost:8080/soap/)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public async Task StartAsync(string uriPrefix, CancellationToken cancellationToken = default)
        {
            if (_listener != null && _listener.IsListening)
                throw new InvalidOperationException("Server is already running");

            _listener = new HttpListener();
            _listener.Prefixes.Add(uriPrefix);
            _listener.Start();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger?.LogInformation("SOAP Server started at {UriPrefix}", uriPrefix);

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => ProcessRequestAsync(context, _cts.Token), _cts.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
            }
            catch (HttpListenerException)
            {
                // Listener stopped
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Server error");
                throw;
            }
        }

        /// <summary>
        /// Stops the SOAP server
        /// </summary>
        public async Task StopAsync()
        {
            if (_listener == null || !_listener.IsListening)
                return;

            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();

            _cts?.Dispose();
            _cts = null;

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Registers a SOAP service with the server
        /// </summary>
        /// <param name="path">The service path (e.g., "service.asmx")</param>
        /// <param name="service">The SOAP service implementation</param>
        public void RegisterService(string path, SoapService service)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            _services.TryAdd(path.TrimStart('/'), service);
            _logger?.LogInformation("Registered SOAP service: {Path}", path);
        }

        /// <summary>
        /// Unregisters a SOAP service from the server
        /// </summary>
        /// <param name="path">The service path</param>
        /// <returns>True if the service was removed; otherwise, false</returns>
        public bool UnregisterService(string path)
        {
            var removed = _services.TryRemove(path.TrimStart('/'), out _);
            if (removed)
            {
                _logger?.LogInformation("Unregistered SOAP service: {Path}", path);
            }
            return removed;
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Extract the service path from the URL
                var path = GetServicePath(request.Url?.LocalPath ?? string.Empty);

                if (string.IsNullOrEmpty(path) || !_services.TryGetValue(path, out var service))
                {
                    await SendNotFoundAsync(response, "Service not found").ConfigureAwait(false);
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    await SendMethodNotAllowedAsync(response).ConfigureAwait(false);
                    return;
                }

                // Read SOAP request
                using var reader = new StreamReader(request.InputStream);
                var soapEnvelope = await reader.ReadToEndAsync().ConfigureAwait(false);

                _logger?.LogDebug("Received SOAP request for {Path}", path);

                // Invoke service
                var soapResponse = await service.ProcessRequestAsync(soapEnvelope, cancellationToken).ConfigureAwait(false);

                // Raise event for monitoring
                OnRequest?.Invoke(this, new SoapRequestContext(path, soapEnvelope, soapResponse));

                // Send SOAP response
                await SendSoapResponseAsync(response, soapResponse).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing SOAP request");
                await SendServerErrorAsync(response, ex.Message).ConfigureAwait(false);
            }
        }

        private static string GetServicePath(string localPath)
        {
            // Remove leading slash and extract service name
            var path = localPath.TrimStart('/');
            var queryIndex = path.IndexOf('?');
            if (queryIndex > 0)
            {
                path = path.Substring(0, queryIndex);
            }
            return path;
        }

        private static async Task SendSoapResponseAsync(HttpListenerResponse response, string soapResponse)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/xml; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;

            var bytes = Encoding.UTF8.GetBytes(soapResponse);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static async Task SendNotFoundAsync(HttpListenerResponse response, string message)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.ContentType = "text/plain";

            var bytes = Encoding.UTF8.GetBytes(message);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static async Task SendMethodNotAllowedAsync(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.ContentType = "text/plain";

            var message = "Only POST method is allowed";
            var bytes = Encoding.UTF8.GetBytes(message);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static async Task SendServerErrorAsync(HttpListenerResponse response, string errorMessage)
        {
            var faultXml = CreateFaultEnvelope("Server", errorMessage);
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response.ContentType = "text/xml; charset=utf-8";

            var bytes = Encoding.UTF8.GetBytes(faultXml);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static string CreateFaultEnvelope(string code, string message)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <soap:Fault>
      <faultcode>soap:{code}</faultcode>
      <faultstring>{EscapeXml(message)}</faultstring>
    </soap:Fault>
  </soap:Body>
</soap:Envelope>";
        }

        private static string EscapeXml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Base class for SOAP service implementations
    /// </summary>
    public abstract class SoapService
    {
        /// <summary>
        /// Processes a SOAP request and returns the response
        /// </summary>
        /// <param name="soapEnvelope">The SOAP envelope XML</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The SOAP response envelope</returns>
        public abstract Task<string> ProcessRequestAsync(string soapEnvelope, CancellationToken cancellationToken = default);

        /// <summary>
        /// Extracts the SOAP action from the request
        /// </summary>
        /// <param name="soapEnvelope">The SOAP envelope XML</param>
        /// <returns>The SOAP action or null if not found</returns>
        protected static string? ExtractSoapAction(string soapEnvelope)
        {
            try
            {
                var doc = XDocument.Parse(soapEnvelope);
                var ns = XNamespace.Get("http://schemas.xmlsoap.org/soap/envelope/");
                var body = doc.Descendants(ns + "Body").FirstOrDefault();
                return body?.Elements().FirstOrDefault()?.Name.LocalName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a SOAP response envelope
        /// </summary>
        /// <param name="bodyContent">The body content XML</param>
        /// <returns>The complete SOAP envelope</returns>
        protected static string CreateResponseEnvelope(string bodyContent)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    {bodyContent}
  </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Creates a SOAP fault response
        /// </summary>
        /// <param name="code">The fault code</param>
        /// <param name="message">The fault message</param>
        /// <returns>The SOAP fault envelope</returns>
        protected static string CreateFaultResponse(string code, string message)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <soap:Fault>
      <faultcode>soap:{code}</faultcode>
      <faultstring>{EscapeXml(message)}</faultstring>
    </soap:Fault>
  </soap:Body>
</soap:Envelope>";
        }

        private static string EscapeXml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }

    /// <summary>
    /// Context information for SOAP requests
    /// </summary>
    public class SoapRequestContext
    {
        /// <summary>
        /// Gets the service path
        /// </summary>
        public string ServicePath { get; }

        /// <summary>
        /// Gets the request SOAP envelope
        /// </summary>
        public string RequestEnvelope { get; }

        /// <summary>
        /// Gets the response SOAP envelope
        /// </summary>
        public string ResponseEnvelope { get; }

        /// <summary>
        /// Gets the timestamp of the request
        /// </summary>
        public DateTime Timestamp { get; }

        public SoapRequestContext(string servicePath, string requestEnvelope, string responseEnvelope)
        {
            ServicePath = servicePath;
            RequestEnvelope = requestEnvelope;
            ResponseEnvelope = responseEnvelope;
            Timestamp = DateTime.UtcNow;
        }
    }
}
