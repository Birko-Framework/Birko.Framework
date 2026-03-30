using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Birko.Communication.REST
{
    /// <summary>
    /// REST server using HttpListener for hosting RESTful APIs
    /// </summary>
    public class RestServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, RouteRegistration> _routes = new();
        private readonly List<RestMiddleware> _middlewares = new();
        private readonly ILogger<RestServer>? _logger;

        public event EventHandler<RestRequestContext>? OnRequest;

        public bool IsListening => _listener != null && _listener.IsListening;

        /// <summary>
        /// Initializes a new instance of the RestServer class
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics</param>
        public RestServer(ILogger<RestServer>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts the REST server
        /// </summary>
        /// <param name="uriPrefix">The URI prefix to listen on (e.g., http://localhost:8080/api/)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public async Task StartAsync(string uriPrefix, CancellationToken cancellationToken = default)
        {
            if (_listener != null && _listener.IsListening)
                throw new InvalidOperationException("Server is already running");

            _listener = new HttpListener();
            _listener.Prefixes.Add(uriPrefix);
            _listener.Start();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger?.LogInformation("REST Server started at {UriPrefix}", uriPrefix);

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
        /// Stops the REST server
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
        /// Registers a route handler for the specified path and HTTP method
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <param name="path">The route path (supports parameters like {id})</param>
        /// <param name="handler">The route handler function</param>
        public void RegisterRoute(HttpMethod method, string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var routeKey = GetRouteKey(method, path);
            var registration = new RouteRegistration(path, handler);
            _routes.TryAdd(routeKey, registration);

            _logger?.LogInformation("Registered route: {Method} {Path}", method, path);
        }

        /// <summary>
        /// Registers a GET route handler
        /// </summary>
        /// <param name="path">The route path</param>
        /// <param name="handler">The handler function</param>
        public void Get(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            RegisterRoute(HttpMethod.GET, path, handler);
        }

        /// <summary>
        /// Registers a POST route handler
        /// </summary>
        /// <param name="path">The route path</param>
        /// <param name="handler">The handler function</param>
        public void Post(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            RegisterRoute(HttpMethod.POST, path, handler);
        }

        /// <summary>
        /// Registers a PUT route handler
        /// </summary>
        /// <param name="path">The route path</param>
        /// <param name="handler">The handler function</param>
        public void Put(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            RegisterRoute(HttpMethod.PUT, path, handler);
        }

        /// <summary>
        /// Registers a DELETE route handler
        /// </summary>
        /// <param name="path">The route path</param>
        /// <param name="handler">The handler function</param>
        public void Delete(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            RegisterRoute(HttpMethod.DELETE, path, handler);
        }

        /// <summary>
        /// Registers a PATCH route handler
        /// </summary>
        /// <param name="path">The route path</param>
        /// <param name="handler">The handler function</param>
        public void Patch(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            RegisterRoute(HttpMethod.PATCH, path, handler);
        }

        /// <summary>
        /// Unregisters a route
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <param name="path">The route path</param>
        /// <returns>True if the route was removed; otherwise, false</returns>
        public bool UnregisterRoute(HttpMethod method, string path)
        {
            var routeKey = GetRouteKey(method, path);
            var removed = _routes.TryRemove(routeKey, out _);
            if (removed)
            {
                _logger?.LogInformation("Unregistered route: {Method} {Path}", method, path);
            }
            return removed;
        }

        /// <summary>
        /// Adds a middleware to the request pipeline
        /// </summary>
        /// <param name="middleware">The middleware function</param>
        public void UseMiddleware(Func<RestRequest, Func<Task<RestResponse>>, Task<RestResponse>> middleware)
        {
            _middlewares.Add(new RestMiddleware(middleware));
            _logger?.LogInformation("Added middleware to pipeline");
        }

        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var restRequest = CreateRestRequest(request);
                var restResponse = await ExecutePipelineAsync(restRequest, cancellationToken).ConfigureAwait(false);

                // Raise event for monitoring
                OnRequest?.Invoke(this, new RestRequestContext(restRequest, restResponse));

                await SendResponseAsync(response, restResponse).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing REST request");
                await SendServerErrorAsync(response, ex.Message).ConfigureAwait(false);
            }
        }

        private async Task<RestResponse> ExecutePipelineAsync(RestRequest request, CancellationToken cancellationToken)
        {
            // Build middleware pipeline
            Func<Task<RestResponse>> pipeline = async () =>
            {
                var routeKey = GetRouteKey(request.Method, request.Path);

                // Try exact match first
                if (_routes.TryGetValue(routeKey, out var registration))
                {
                    return await registration.Handler(request).ConfigureAwait(false);
                }

                // Try pattern match for routes with parameters
                var matchedRoute = _routes.FirstOrDefault(r =>
                    IsRouteMatch(r.Value.Path, request.Path, out var pathParameters));

                if (matchedRoute.Value != null)
                {
                    // Extract path parameters
                    if (IsRouteMatch(matchedRoute.Value.Path, request.Path, out var parameters))
                    {
                        request.PathParameters = parameters;
                    }
                    return await matchedRoute.Value.Handler(request).ConfigureAwait(false);
                }

                return RestResponse.NotFound("Route not found");
            };

            // Execute middlewares in reverse order (last added = first to execute)
            for (int i = _middlewares.Count - 1; i >= 0; i--)
            {
                var middleware = _middlewares[i];
                var currentPipeline = pipeline;
                pipeline = () => middleware.Execute(request, currentPipeline);
            }

            return await pipeline().ConfigureAwait(false);
        }

        private bool IsRouteMatch(string routePattern, string requestPath, out Dictionary<string, string> parameters)
        {
            parameters = new Dictionary<string, string>();

            var routeSegments = routePattern.Trim('/').Split('/');
            var pathSegments = requestPath.Trim('/').Split('/');

            if (routeSegments.Length != pathSegments.Length)
                return false;

            for (int i = 0; i < routeSegments.Length; i++)
            {
                var routeSegment = routeSegments[i];
                var pathSegment = pathSegments[i];

                if (routeSegment.StartsWith("{") && routeSegment.EndsWith("}"))
                {
                    var paramName = routeSegment.Trim('{', '}');
                    parameters[paramName] = pathSegment;
                }
                else if (!string.Equals(routeSegment, pathSegment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private RestRequest CreateRestRequest(HttpListenerRequest request)
        {
            var restRequest = new RestRequest
            {
                Method = ParseHttpMethod(request.HttpMethod),
                Path = request.Url?.LocalPath ?? string.Empty,
                QueryString = request.Url?.Query ?? string.Empty,
                Headers = new Dictionary<string, string>(),
                QueryParameters = new Dictionary<string, string>()
            };

            // Copy headers
            foreach (string key in request.Headers.Keys)
            {
                restRequest.Headers[key] = request.Headers[key] ?? string.Empty;
            }

            // Parse query parameters
            var query = request.Url?.Query;
            if (!string.IsNullOrEmpty(query))
            {
                var parameters = query.TrimStart('?').Split('&');
                foreach (var param in parameters)
                {
                    var parts = param.Split('=');
                    if (parts.Length == 2)
                    {
                        restRequest.QueryParameters[Uri.UnescapeDataString(parts[0])] =
                            Uri.UnescapeDataString(parts[1]);
                    }
                }
            }

            // Read body
            if (request.InputStream != null && request.ContentLength64 > 0)
            {
                using var reader = new StreamReader(request.InputStream);
                restRequest.Body = reader.ReadToEnd();
            }

            restRequest.ContentType = request.ContentType;
            restRequest.UserHostAddress = request.UserHostAddress;

            return restRequest;
        }

        private HttpMethod ParseHttpMethod(string method)
        {
            return Enum.TryParse<HttpMethod>(method, true, out var httpMethod)
                ? httpMethod
                : HttpMethod.GET;
        }

        private static string GetRouteKey(HttpMethod method, string path)
        {
            return $"{method}:{path.TrimStart('/')}";
        }

        private static async Task SendResponseAsync(HttpListenerResponse response, RestResponse restResponse)
        {
            response.StatusCode = (int)restResponse.StatusCode;

            if (!string.IsNullOrEmpty(restResponse.ContentType))
            {
                response.ContentType = restResponse.ContentType;
            }

            // Add custom headers
            foreach (var header in restResponse.Headers)
            {
                try
                {
                    response.Headers[header.Key] = header.Value;
                }
                catch (Exception)
                {
                    // Some headers are restricted
                }
            }

            if (!string.IsNullOrEmpty(restResponse.Content))
            {
                var bytes = Encoding.UTF8.GetBytes(restResponse.Content);
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }

            response.OutputStream.Close();
        }

        private static async Task SendServerErrorAsync(HttpListenerResponse response, string errorMessage)
        {
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response.ContentType = "application/json";

            var errorJson = $"{{\"error\":\"{EscapeJson(errorMessage)}\"}}";
            var bytes = Encoding.UTF8.GetBytes(errorJson);
            response.ContentLength64 = bytes.Length;

            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static string EscapeJson(string text)
        {
            return text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        public void Dispose()
        {
            StopAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Internal route registration
    /// </summary>
    internal class RouteRegistration
    {
        public string Path { get; }
        public Func<RestRequest, Task<RestResponse>> Handler { get; }

        public RouteRegistration(string path, Func<RestRequest, Task<RestResponse>> handler)
        {
            Path = path;
            Handler = handler;
        }
    }

    /// <summary>
    /// Internal middleware wrapper
    /// </summary>
    internal class RestMiddleware
    {
        private readonly Func<RestRequest, Func<Task<RestResponse>>, Task<RestResponse>> _middleware;

        public RestMiddleware(Func<RestRequest, Func<Task<RestResponse>>, Task<RestResponse>> middleware)
        {
            _middleware = middleware;
        }

        public Task<RestResponse> Execute(RestRequest request, Func<Task<RestResponse>> next)
        {
            return _middleware(request, next);
        }
    }

    /// <summary>
    /// REST request object
    /// </summary>
    public class RestRequest
    {
        /// <summary>
        /// Gets or sets the HTTP method
        /// </summary>
        public HttpMethod Method { get; set; }

        /// <summary>
        /// Gets or sets the request path
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the query string
        /// </summary>
        public string QueryString { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the request body
        /// </summary>
        public string? Body { get; set; }

        /// <summary>
        /// Gets or sets the content type
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// Gets or sets the request headers
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Gets or sets the query parameters
        /// </summary>
        public Dictionary<string, string> QueryParameters { get; set; } = new();

        /// <summary>
        /// Gets or sets the path parameters (extracted from route patterns)
        /// </summary>
        public Dictionary<string, string> PathParameters { get; set; } = new();

        /// <summary>
        /// Gets or sets the client's IP address
        /// </summary>
        public string? UserHostAddress { get; set; }

        /// <summary>
        /// Gets a header value by key
        /// </summary>
        public string? GetHeader(string key)
        {
            return Headers.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Gets a query parameter value by key
        /// </summary>
        public string? GetQueryParameter(string key)
        {
            return QueryParameters.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Gets a path parameter value by key
        /// </summary>
        public string? GetPathParameter(string key)
        {
            return PathParameters.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// REST response object
    /// </summary>
    public class RestResponse
    {
        /// <summary>
        /// Gets or sets the HTTP status code
        /// </summary>
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        /// <summary>
        /// Gets or sets the response content
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Gets or sets the content type
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// Gets or sets the response headers
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>
        /// Creates a 200 OK response with content
        /// </summary>
        public static RestResponse Ok(string? content = null, string contentType = "application/json")
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.OK,
                Content = content,
                ContentType = contentType
            };
        }

        /// <summary>
        /// Creates a 201 Created response with content
        /// </summary>
        public static RestResponse Created(string? content = null, string contentType = "application/json")
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.Created,
                Content = content,
                ContentType = contentType
            };
        }

        /// <summary>
        /// Creates a 204 No Content response
        /// </summary>
        public static RestResponse NoContent()
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.NoContent
            };
        }

        /// <summary>
        /// Creates a 400 Bad Request response
        /// </summary>
        public static RestResponse BadRequest(string message)
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = $"{{\"error\":\"{message}\"}}",
                ContentType = "application/json"
            };
        }

        /// <summary>
        /// Creates a 401 Unauthorized response
        /// </summary>
        public static RestResponse Unauthorized(string message = "Unauthorized")
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = $"{{\"error\":\"{message}\"}}",
                ContentType = "application/json"
            };
        }

        /// <summary>
        /// Creates a 403 Forbidden response
        /// </summary>
        public static RestResponse Forbidden(string message = "Forbidden")
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.Forbidden,
                Content = $"{{\"error\":\"{message}\"}}",
                ContentType = "application/json"
            };
        }

        /// <summary>
        /// Creates a 404 Not Found response
        /// </summary>
        public static RestResponse NotFound(string message = "Not found")
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.NotFound,
                Content = $"{{\"error\":\"{message}\"}}",
                ContentType = "application/json"
            };
        }

        /// <summary>
        /// Creates a 500 Internal Server Error response
        /// </summary>
        public static RestResponse InternalServerError(string message)
        {
            return new RestResponse
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = $"{{\"error\":\"{message}\"}}",
                ContentType = "application/json"
            };
        }
    }

    /// <summary>
    /// Context information for REST requests
    /// </summary>
    public class RestRequestContext
    {
        /// <summary>
        /// Gets the request
        /// </summary>
        public RestRequest Request { get; }

        /// <summary>
        /// Gets the response
        /// </summary>
        public RestResponse Response { get; }

        /// <summary>
        /// Gets the timestamp of the request
        /// </summary>
        public DateTime Timestamp { get; }

        public RestRequestContext(RestRequest request, RestResponse response)
        {
            Request = request;
            Response = response;
            Timestamp = DateTime.UtcNow;
        }
    }
}
