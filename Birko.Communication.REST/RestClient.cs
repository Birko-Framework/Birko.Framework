using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.REST
{
    /// <summary>
    /// REST client for consuming RESTful APIs.
    /// </summary>
    /// <remarks>
    /// The synchronous overloads (Get/Post/Put/Delete/Patch) are convenience shims that block on the
    /// async methods via <c>ConfigureAwait(false)</c>. Prefer the <c>*Async</c> overloads — they are
    /// the only ones that accept a <see cref="CancellationToken"/> and do not block a thread on
    /// network I/O (CR-M060).
    /// </remarks>
    public class RestClient : IDisposable
    {
        // Process-wide shared cache reached concurrently from many threads — must be thread-safe
        // (CR-H031: a plain Dictionary mutated without locking can corrupt / throw).
        private static readonly ConcurrentDictionary<string, RestClient> _clients = new();

        private readonly HttpClientHandler _handler;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// Gets the base URI endpoint for this REST client
        /// </summary>
        public string BaseURI { get; private set; }

        /// <summary>
        /// Gets or sets the default credentials for authentication. Applied to the underlying
        /// HttpClientHandler so it actually authenticates requests (CR-M059). Set it before the first
        /// request — HttpClientHandler rejects credential changes once a request has been sent.
        /// </summary>
        public ICredentials? Credentials
        {
            get => _handler.Credentials;
            set => _handler.Credentials = value;
        }

        /// <summary>
        /// Gets or sets the timeout for requests in milliseconds
        /// </summary>
        public int Timeout
        {
            get => (int)_httpClient.Timeout.TotalMilliseconds;
            set => _httpClient.Timeout = TimeSpan.FromMilliseconds(value);
        }

        /// <summary>
        /// Gets or sets the default content type for requests
        /// </summary>
        public string DefaultContentType { get; set; } = "application/json";

        /// <summary>
        /// Gets or sets the default headers to include in all requests
        /// </summary>
        public Dictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Event raised when a REST request is sent. <b>Handlers must not throw</b> — they run inline on
        /// the request path (OnRequest before the HTTP call, OnResponse after), so a throwing subscriber
        /// aborts the request or masks the response (CR-L079).
        /// </summary>
        public event EventHandler<RestRequestEventArgs>? OnRequest;

        /// <summary>
        /// Event raised when a REST response is received. Handlers must not throw — see <see cref="OnRequest"/>.
        /// </summary>
        public event EventHandler<RestResponseEventArgs>? OnResponse;

        /// <summary>
        /// Gets a cached REST client instance for the specified base URI
        /// </summary>
        /// <param name="baseUri">The base URI of the REST API</param>
        /// <returns>A cached or new RestClient instance</returns>
        public static RestClient GetClient(string baseUri)
        {
            return _clients.GetOrAdd(baseUri, uri => new RestClient(uri));
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
            // Own an HttpClientHandler so the Credentials property can actually flow to requests
            // (CR-M059). HttpClient owns the handler (disposeHandler defaults true), so Dispose covers it.
            _handler = new HttpClientHandler();
            _httpClient = new HttpClient(_handler)
            {
                Timeout = TimeSpan.FromMilliseconds(100000) // 100 seconds default
            };
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
        /// Maps the custom HttpMethod enum to System.Net.Http.HttpMethod
        /// </summary>
        private static System.Net.Http.HttpMethod ToHttpMethod(HttpMethod method)
        {
            return method switch
            {
                HttpMethod.GET => System.Net.Http.HttpMethod.Get,
                HttpMethod.POST => System.Net.Http.HttpMethod.Post,
                HttpMethod.PUT => System.Net.Http.HttpMethod.Put,
                HttpMethod.DELETE => System.Net.Http.HttpMethod.Delete,
                HttpMethod.PATCH => System.Net.Http.HttpMethod.Patch,
                HttpMethod.HEAD => System.Net.Http.HttpMethod.Head,
                HttpMethod.OPTIONS => System.Net.Http.HttpMethod.Options,
                _ => new System.Net.Http.HttpMethod(method.ToString())
            };
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

            using var request = new HttpRequestMessage(ToHttpMethod(method), uri);

            // Add default headers
            foreach (var header in DefaultHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add custom headers
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Write body if present
            if (!string.IsNullOrEmpty(body) && (method == HttpMethod.POST || method == HttpMethod.PUT || method == HttpMethod.PATCH))
            {
                var mediaType = contentType ?? DefaultContentType;
                request.Content = new StringContent(body, Encoding.UTF8, mediaType);
            }

            OnRequest?.Invoke(this, new RestRequestEventArgs(method.ToString(), uri, body));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            OnResponse?.Invoke(this, new RestResponseEventArgs(method.ToString(), uri, responseContent, response.StatusCode));

            return responseContent;
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
            // Snapshot then clear so a concurrent GetClient can't observe half-disposed state.
            var clients = _clients.Values.ToArray();
            _clients.Clear();
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// Removes a specific cached REST client
        /// </summary>
        /// <param name="baseUri">The base URI of the client to remove</param>
        /// <returns>True if the client was removed; otherwise, false</returns>
        public static bool RemoveClient(string baseUri)
        {
            if (_clients.TryRemove(baseUri, out var client))
            {
                client.Dispose();
                return true;
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
