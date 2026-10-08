using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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
        /// Maps a WebSocket endpoint, by default behind the static-token check.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder</param>
        /// <param name="pattern">The route pattern</param>
        /// <param name="handler">The WebSocket connection handler</param>
        /// <param name="requireAuthentication">
        /// Require a token accepted by <see cref="Services.WebSocketAuthenticationService"/> (static / M2M tokens
        /// from <see cref="Services.WebSocketAuthenticationConfiguration"/>, passed as <c>?token=</c>). This is
        /// <b>not</b> per-user authentication. When true and the service is not registered, mapping throws
        /// <see cref="InvalidOperationException"/>, and a request that still gets through is refused with 401.
        /// For per-user (JWT / cookie) auth pass <c>false</c> and chain <c>.RequireAuthorization()</c>.
        /// </param>
        /// <returns>The endpoint convention builder</returns>
        public static IEndpointConventionBuilder MapWebSocketEndpoint(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            WebSocketConnectionHandler handler,
            bool requireAuthentication = true)
        {
            if (requireAuthentication)
            {
                WebSocketAuthenticationGate.EnsureRegistered(endpoints.ServiceProvider, pattern);
            }

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
        /// Legacy extension method for direct app.UseWebSocket() style. <paramref name="requireAuthentication"/>
        /// means exactly what it does on <see cref="MapWebSocketEndpoint"/>, including failing closed.
        /// </summary>
        public static void MapWebSocket(
            this IApplicationBuilder app,
            string pattern,
            WebSocketConnectionHandler handler,
            bool requireAuthentication = true)
        {
            if (requireAuthentication)
            {
                WebSocketAuthenticationGate.EnsureRegistered(app.ApplicationServices, pattern);
            }

            app.Map(pattern, appBuilder =>
            {
                appBuilder.Use(async (HttpContext context, RequestDelegate next) =>
                {
                    if (requireAuthentication && !await WebSocketAuthenticationGate.AuthorizeAsync(context, null))
                    {
                        return;
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
