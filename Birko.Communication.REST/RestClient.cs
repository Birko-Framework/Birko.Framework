using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.REST
{
    /// <summary>
    /// REST client for consuming RESTful APIs
    /// </summary>
    public class RestClient
    {
        private static readonly Dictionary<string, RestClient> _clients = new Dictionary<string, RestClient>();

        /// <summary>
        /// Gets the base URI endpoint for this REST client
        /// </summary>
        public string BaseURI { get; private set; }

        /// <summary>
        /// Gets or sets the default credentials for authentication
        /// </summary>
        public ICredentials? Credentials { get; set; }

        /// <summary>
        /// Gets or sets the timeout for requests in milliseconds
        /// </summary>
        public int Timeout { get; set; } = 100000; // 100 seconds default

        /// <summary>
        /// Gets or sets the default content type for requests
        /// </summary>
        public string DefaultContentType { get; set; } = "application/json";

        /// <summary>
        /// Gets or sets the default headers to include in all requests
        /// </summary>
        public Dictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Event raised when a REST request is sent
        /// </summary>
        public event EventHandler<RestRequestEventArgs>? OnRequest;

        /// <summary>
        /// Event raised when a REST response is received
        /// </summary>
        public event EventHandler<RestResponseEventArgs>? OnResponse;

        /// <summary>
        /// Gets a cached REST client instance for the specified base URI
        /// </summary>
        /// <param name="baseUri">The base URI of the REST API</param>
        /// <returns>A cached or new RestClient instance</returns>
        public static RestClient GetClient(string baseUri)
        {
            if (!_clients.ContainsKey(baseUri))
            {
                _clients.Add(baseUri, new RestClient(baseUri));
            }
            return _clients[baseUri];
        }

        /// <summary>
        /// Initializes a new instance of the RestClient class
        /// </summary>
        /// <param name="baseUri">The base URI of the REST API</param>
        public RestClient(string baseUri)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
                throw new ArgumentNullException(nameof(baseUri));

            BaseURI = baseUri.TrimEnd('/');
        }

        /// <summary>
        /// Sends a GET request to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path (e.g., "/users/1")</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>The response content as a string</returns>
        public string Get(string endpoint, string? queryString = null, Dictionary<string, string>? headers = null)
        {
            return SendRequestAsync(HttpMethod.GET, endpoint, queryString, null, null, headers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a GET request asynchronously to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        public async Task<string> GetAsync(string endpoint, string? queryString = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync(HttpMethod.GET, endpoint, queryString, null, null, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a POST request to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type (defaults to application/json)</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>The response content as a string</returns>
        public string Post(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null)
        {
            return SendRequestAsync(HttpMethod.POST, endpoint, null, body, contentType, headers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a POST request asynchronously to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        public async Task<string> PostAsync(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync(HttpMethod.POST, endpoint, null, body, contentType, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a PUT request to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>The response content as a string</returns>
        public string Put(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null)
        {
            return SendRequestAsync(HttpMethod.PUT, endpoint, null, body, contentType, headers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a PUT request asynchronously to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        public async Task<string> PutAsync(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync(HttpMethod.PUT, endpoint, null, body, contentType, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a DELETE request to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>The response content as a string</returns>
        public string Delete(string endpoint, string? queryString = null, Dictionary<string, string>? headers = null)
        {
            return SendRequestAsync(HttpMethod.DELETE, endpoint, queryString, null, null, headers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a DELETE request asynchronously to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        public async Task<string> DeleteAsync(string endpoint, string? queryString = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync(HttpMethod.DELETE, endpoint, queryString, null, null, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a PATCH request to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>The response content as a string</returns>
        public string Patch(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null)
        {
            return SendRequestAsync(HttpMethod.PATCH, endpoint, null, body, contentType, headers)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Sends a PATCH request asynchronously to the specified endpoint
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        public async Task<string> PatchAsync(string endpoint, string body, string? contentType = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync(HttpMethod.PATCH, endpoint, null, body, contentType, headers, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a REST request and returns the response as a string
        /// </summary>
        /// <param name="method">The HTTP method to use</param>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The response content as a string</returns>
        protected async Task<string> SendRequestAsync(HttpMethod method, string endpoint, string? queryString, string? body, string? contentType, Dictionary<string, string>? headers, CancellationToken cancellationToken = default)
        {
            var uri = BuildUri(endpoint, queryString);
            var request = CreateRequest(method, uri, body, contentType, headers);

            OnRequest?.Invoke(this, new RestRequestEventArgs(method.ToString(), uri, body));

            using var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream ?? throw new InvalidOperationException("No response stream"));

            var responseContent = await reader.ReadToEndAsync().ConfigureAwait(false);
            OnResponse?.Invoke(this, new RestResponseEventArgs(method.ToString(), uri, responseContent, response.StatusCode));

            return responseContent;
        }

        /// <summary>
        /// Creates a web request for the REST API call
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <param name="uri">The full URI</param>
        /// <param name="body">The request body content</param>
        /// <param name="contentType">The content type</param>
        /// <param name="headers">Optional headers</param>
        /// <returns>A configured HttpWebRequest</returns>
        protected virtual HttpWebRequest CreateRequest(HttpMethod method, string uri, string? body, string? contentType, Dictionary<string, string>? headers)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method.ToString();
            request.Timeout = Timeout;
            request.Credentials = Credentials;

            // Set content type for methods that typically have a body
            if (method == HttpMethod.POST || method == HttpMethod.PUT || method == HttpMethod.PATCH)
            {
                request.ContentType = contentType ?? DefaultContentType;
            }

            // Add default headers
            foreach (var header in DefaultHeaders)
            {
                if (!request.Headers[header.Key]?.Equals(header.Value) ?? true)
                {
                    try
                    {
                        request.Headers[header.Key] = header.Value;
                    }
                    catch (Exception)
                    {
                        // Some headers are restricted and cannot be set
                    }
                }
            }

            // Add custom headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    try
                    {
                        request.Headers[header.Key] = header.Value;
                    }
                    catch (Exception)
                    {
                        // Some headers are restricted and cannot be set
                    }
                }
            }

            // Write body if present
            if (!string.IsNullOrEmpty(body) && (method == HttpMethod.POST || method == HttpMethod.PUT || method == HttpMethod.PATCH))
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                request.ContentLength = bytes.Length;

                using var stream = request.GetRequestStream();
                stream.Write(bytes, 0, bytes.Length);
            }

            return request;
        }

        /// <summary>
        /// Builds the full URI including query string
        /// </summary>
        /// <param name="endpoint">The endpoint path</param>
        /// <param name="queryString">Optional query string parameters</param>
        /// <returns>The full URI</returns>
        protected string BuildUri(string endpoint, string? queryString)
        {
            var uri = $"{BaseURI}/{endpoint.TrimStart('/')}";

            if (!string.IsNullOrEmpty(queryString))
            {
                uri += queryString.StartsWith('?') ? queryString : $"?{queryString}";
            }

            return uri;
        }

        /// <summary>
        /// Clears all cached REST clients
        /// </summary>
        public static void ClearCache()
        {
            _clients.Clear();
        }

        /// <summary>
        /// Removes a specific cached REST client
        /// </summary>
        /// <param name="baseUri">The base URI of the client to remove</param>
        /// <returns>True if the client was removed; otherwise, false</returns>
        public static bool RemoveClient(string baseUri)
        {
            return _clients.Remove(baseUri);
        }
    }

    /// <summary>
    /// HTTP methods enumeration
    /// </summary>
    public enum HttpMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH,
        HEAD,
        OPTIONS
    }

    /// <summary>
    /// Event arguments for REST requests
    /// </summary>
    public class RestRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the HTTP method
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets the full request URI
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// Gets the request body
        /// </summary>
        public string? Body { get; }

        /// <summary>
        /// Gets the timestamp of the request
        /// </summary>
        public DateTime Timestamp { get; }

        public RestRequestEventArgs(string method, string uri, string? body)
        {
            Method = method;
            Uri = uri;
            Body = body;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for REST responses
    /// </summary>
    public class RestResponseEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the HTTP method
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Gets the request URI
        /// </summary>
        public string Uri { get; }

        /// <summary>
        /// Gets the response content
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// Gets the HTTP status code
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// Gets the timestamp of the response
        /// </summary>
        public DateTime Timestamp { get; }

        public RestResponseEventArgs(string method, string uri, string content, HttpStatusCode statusCode)
        {
            Method = method;
            Uri = uri;
            Content = content;
            StatusCode = statusCode;
            Timestamp = DateTime.UtcNow;
        }
    }
}
