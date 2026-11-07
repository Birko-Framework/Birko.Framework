using Birko.Security.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
                EnsureProxyListsAgree(services);
                return;
            }

            throw new InvalidOperationException($"{MissingServiceMessage} Endpoint: '{pattern}'.");
        }

        /// <summary>
        /// TASK-829: the service refuses a trusted-proxy list that differs from the host's forwarded headers, but it is
        /// built on first use. Checking at map time makes that refusal a startup failure rather than a first-request one.
        /// Both are singletons, so reading them from the root provider is safe.
        /// </summary>
        private static void EnsureProxyListsAgree(IServiceProvider services)
        {
            var hostProxies = services.GetService<TrustedProxySettings>();
            var config = services.GetService<IOptions<Services.WebSocketAuthenticationConfiguration>>()?.Value;
            if (hostProxies == null || config == null)
            {
                return;
            }
            Services.WebSocketAuthenticationService.EnsureProxyListsAgree(config, hostProxies);
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
