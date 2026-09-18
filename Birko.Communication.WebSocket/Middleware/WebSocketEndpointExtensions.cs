using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.WebSockets;
using System.Threading.Tasks;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Middleware
{
    /// <summary>
    /// Delegate for handling WebSocket connections
    /// </summary>
    /// <param name="webSocket">The WebSocket instance</param>
    /// <param name="context">The HTTP context</param>
    /// <returns>Task representing the async operation</returns>
    public delegate Task WebSocketConnectionHandler(SysWebSocket webSocket, HttpContext context);

    /// <summary>
    /// Extension methods for registering WebSocket endpoints
    /// </summary>
    public static class WebSocketEndpointExtensions
    {
        /// <summary>
        /// Maps a WebSocket endpoint with optional authentication
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <param name="pattern">The route pattern</param>
        /// <param name="handler">The WebSocket connection handler</param>
        /// <param name="requireAuthentication">Whether to require authentication (default: true)</param>
        /// <returns>The route handler builder</returns>
        public static IEndpointConventionBuilder MapWebSocketEndpoint(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            WebSocketConnectionHandler handler,
            bool requireAuthentication = true)
        {
            var pipeline = endpoints.CreateApplicationBuilder()
                .UseMiddleware<WebSocketAuthenticationMiddleware>(requireAuthentication)
                .UseMiddleware<WebSocketMiddleware>(handler)
                .Build();

            return endpoints.Map(pattern, pipeline).WithDisplayName($"WebSocket: {pattern}");
        }

        /// <summary>
        /// Maps a WebSocket endpoint without authentication
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <param name="pattern">The route pattern</param>
        /// <param name="handler">The WebSocket connection handler</param>
        /// <returns>The route handler builder</returns>
        public static IEndpointConventionBuilder MapWebSocketEndpointNoAuth(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            WebSocketConnectionHandler handler)
        {
            return endpoints.MapWebSocketEndpoint(pattern, handler, requireAuthentication: false);
        }

        /// <summary>
        /// Legacy extension method for direct app.UseWebSocket() style
        /// </summary>
        public static void MapWebSocket(
            this IApplicationBuilder app,
            string pattern,
            WebSocketConnectionHandler handler,
            bool requireAuthentication = true)
        {
            app.Map(pattern, appBuilder =>
            {
                appBuilder.Use(async (HttpContext context, RequestDelegate next) =>
                {
                    if (requireAuthentication)
                    {
                        var authService = context.RequestServices.GetService<Services.WebSocketAuthenticationService>();
                        if (authService != null)
                        {
                            var token = authService.ExtractTokenFromQuery(context);
                            var clientIp = authService.GetClientIpAddress(context);

                            if (!authService.ValidateToken(token, clientIp))
                            {
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                await context.Response.WriteAsync("Unauthorized: Invalid or missing authentication token, or IP address not allowed");
                                return;
                            }
                        }
                    }

                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        SysWebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await handler(webSocket, context);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }
                });
            });
        }
    }
}
