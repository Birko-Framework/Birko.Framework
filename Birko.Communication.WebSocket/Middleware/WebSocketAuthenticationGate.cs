using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Birko.Communication.WebSocket.Middleware
{
    /// <summary>
    /// The one place that decides whether a WebSocket upgrade passes the static-token check
    /// (<see cref="Services.WebSocketAuthenticationService"/>). Both mapping paths call it.
    /// </summary>
    /// <remarks>
    /// TASK-536: both paths used to skip the check when the service was not registered, so the
    /// default <c>requireAuthentication: true</c> accepted anonymous upgrades. A missing service now
    /// fails closed: refused at map time when the container can say so, and 401 per request otherwise.
    /// </remarks>
    internal static class WebSocketAuthenticationGate
    {
        internal const string MissingServiceMessage =
            "WebSocket endpoint requires authentication, but no WebSocketAuthenticationService is registered. " +
            "Register it (with WebSocketAuthenticationConfiguration) for static-token auth, or pass " +
            "requireAuthentication: false and protect the endpoint with .RequireAuthorization() for per-user auth.";

        /// <summary>
        /// Throws at map time when the container can tell the service is not registered.
        /// A container that cannot answer leaves the decision to <see cref="AuthorizeAsync"/>.
        /// </summary>
        internal static void EnsureRegistered(IServiceProvider services, string pattern)
        {
            var isService = services.GetService<IServiceProviderIsService>();
            if (isService == null || isService.IsService(typeof(Services.WebSocketAuthenticationService)))
            {
                return;
            }

            throw new InvalidOperationException($"{MissingServiceMessage} Endpoint: '{pattern}'.");
        }

        /// <summary>
        /// Returns true when the request may proceed; otherwise writes 401 and returns false.
        /// </summary>
        internal static async Task<bool> AuthorizeAsync(HttpContext context, ILogger? logger)
        {
            var authService = context.RequestServices.GetService<Services.WebSocketAuthenticationService>();
            if (authService == null)
            {
                logger?.LogError(MissingServiceMessage);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: WebSocket authentication is not configured");
                return false;
            }

            var token = authService.ExtractTokenFromQuery(context);
            var clientIp = authService.GetClientIpAddress(context);

            if (!authService.ValidateToken(token, clientIp))
            {
                logger?.LogWarning("WebSocket authentication failed from IP: {ClientIp}", clientIp ?? "unknown");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized: Invalid or missing authentication token, or IP address not allowed");
                return false;
            }

            logger?.LogDebug("WebSocket authenticated from IP: {ClientIp}", clientIp ?? "unknown");
            return true;
        }
    }
}
