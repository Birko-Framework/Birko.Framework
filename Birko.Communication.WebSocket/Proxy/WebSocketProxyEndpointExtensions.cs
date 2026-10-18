using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace Birko.Communication.WebSocket.Proxy
{
    /// <summary>Maps an endpoint that relays WebSocket connections to an upstream WebSocket server.</summary>
    public static class WebSocketProxyEndpointExtensions
    {
        /// <summary>
        /// Maps <paramref name="pattern"/> as a WebSocket reverse proxy to <paramref name="upstream"/>.
        /// </summary>
        /// <remarks>
        /// The upstream is connected <b>before</b> the client's upgrade is accepted: an upstream that refuses or cannot be
        /// reached answers the client 502 without upgrading. A request that is not an upgrade gets 400. The client's requested
        /// sub-protocols are offered upstream in its order, and the client is accepted with the one the upstream selected
        /// (none when it selected none). The endpoint has no
        /// authentication of its own — chain <c>.RequireAuthorization()</c> on the returned builder. TLS certificates are
        /// validated unless <see cref="WebSocketProxyOptions.DangerousAcceptAnyServerCertificate"/> is set.
        /// </remarks>
        public static IEndpointConventionBuilder MapWebSocketProxy(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Uri upstream,
            Action<WebSocketProxyOptions>? configure = null)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            var options = new WebSocketProxyOptions(upstream);
            configure?.Invoke(options);
            options.Validate();

            return endpoints.Map(pattern, context => ProxyAsync(context, options))
                .WithDisplayName($"WebSocket proxy: {pattern} -> {options.Upstream.GetLeftPart(UriPartial.Path)}");
        }

        private static async Task ProxyAsync(HttpContext context, WebSocketProxyOptions options)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(WebSocketProxyEndpointExtensions).FullName!);
            var credential = options.Credential?.Invoke(context);
            var upstreamUri = options.BuildUpstreamUri(credential);

            using var upstream = new ClientWebSocket();
            options.ConfigureUpstream(upstream.Options, credential);
            // TASK-569: offer the client's sub-protocols upstream, in the client's order, and accept the client with the
            // one the upstream selected. Without this a client that asked for one (graphql-transport-ws, mqtt, ...) was
            // accepted with none and refused the handshake.
            foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
            {
                upstream.Options.AddSubProtocol(protocol);
            }

            try
            {
                await upstream.ConnectAsync(upstreamUri, context.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is HttpRequestException)
            {
                // The URI is not logged: under the QueryString transport it carries the credential.
                logger?.LogWarning(ex, "WebSocket proxy {Path}: upstream {Upstream} refused or unreachable",
                    context.Request.Path, options.Upstream.GetLeftPart(UriPartial.Path));
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            using var client = await context.WebSockets.AcceptWebSocketAsync(upstream.SubProtocol).ConfigureAwait(false);
            await WebSocketRelay.RunAsync(client, upstream, options.BufferSize, options.CloseTimeout, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
