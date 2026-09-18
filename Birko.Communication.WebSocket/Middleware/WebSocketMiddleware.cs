using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Middleware
{
    /// <summary>
    /// Middleware for handling WebSocket connections
    /// </summary>
    public class WebSocketMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly WebSocketConnectionHandler _handler;
        private readonly ILogger<WebSocketMiddleware> _logger;

        public WebSocketMiddleware(
            RequestDelegate next,
            WebSocketConnectionHandler handler,
            ILogger<WebSocketMiddleware> logger)
        {
            _next = next;
            _handler = handler;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                SysWebSocket? webSocket = null;
                try
                {
                    webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    _logger.LogInformation("WebSocket connection accepted from {RemoteEndPoint}",
                        context.Connection.RemoteIpAddress);

                    await _handler(webSocket, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling WebSocket connection");

                    // Try to close the WebSocket if it was successfully accepted
                    if (webSocket != null && webSocket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await webSocket.CloseAsync(
                                WebSocketCloseStatus.InternalServerError,
                                "Error",
                                CancellationToken.None);
                        }
                        catch (Exception closeEx)
                        {
                            _logger.LogWarning(closeEx, "Failed to close WebSocket after error");
                        }
                    }

                    throw; // Re-throw to let error handling middleware deal with it
                }
            }
            else
            {
                await _next(context);
            }
        }
    }

    /// <summary>
    /// Middleware for WebSocket authentication
    /// </summary>
    public class WebSocketAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly bool _requireAuthentication;
        private readonly ILogger<WebSocketAuthenticationMiddleware> _logger;

        public WebSocketAuthenticationMiddleware(
            RequestDelegate next,
            bool requireAuthentication,
            ILogger<WebSocketAuthenticationMiddleware> logger)
        {
            _next = next;
            _requireAuthentication = requireAuthentication;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_requireAuthentication || !context.WebSockets.IsWebSocketRequest)
            {
                await _next(context);
                return;
            }

            var authService = context.RequestServices.GetService<Services.WebSocketAuthenticationService>();
            if (authService != null)
            {
                var token = authService.ExtractTokenFromQuery(context);
                var clientIp = authService.GetClientIpAddress(context);

                if (!authService.ValidateToken(token, clientIp))
                {
                    _logger.LogWarning("WebSocket authentication failed from IP: {ClientIp}", clientIp ?? "unknown");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized: Invalid or missing authentication token, or IP address not allowed");
                    return;
                }

                _logger.LogDebug("WebSocket authenticated from IP: {ClientIp}", clientIp ?? "unknown");
            }

            await _next(context);
        }
    }
}
