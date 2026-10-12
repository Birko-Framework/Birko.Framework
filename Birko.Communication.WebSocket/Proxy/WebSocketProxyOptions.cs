using Microsoft.AspNetCore.Http;
using System;
using System.Net.WebSockets;

namespace Birko.Communication.WebSocket.Proxy
{
    /// <summary>How <see cref="WebSocketProxyOptions.Credential"/> reaches the upstream.</summary>
    public enum WebSocketProxyCredentialTransport
    {
        /// <summary>A request header on the upstream handshake (default) — never written to an access log as part of a URL.</summary>
        Header,

        /// <summary>A URL-encoded query parameter, for upstreams that only read one (Birko's static-token gate reads <c>?token=</c>).</summary>
        QueryString
    }

    /// <summary>Configuration of one <see cref="WebSocketProxyEndpointExtensions.MapWebSocketProxy"/> endpoint.</summary>
    public sealed class WebSocketProxyOptions
    {
        /// <param name="upstream">Absolute <c>ws</c>/<c>wss</c> URI; <c>http</c>/<c>https</c> are taken as <c>ws</c>/<c>wss</c>.</param>
        public WebSocketProxyOptions(Uri upstream)
        {
            if (upstream == null) throw new ArgumentNullException(nameof(upstream));
            if (!upstream.IsAbsoluteUri) throw new ArgumentException("The upstream must be an absolute URI.", nameof(upstream));

            var scheme = upstream.Scheme.ToLowerInvariant() switch
            {
                "ws" or "http" => "ws",
                "wss" or "https" => "wss",
                _ => throw new ArgumentException($"The upstream scheme '{upstream.Scheme}' is not ws, wss, http or https.", nameof(upstream))
            };

            Upstream = scheme == upstream.Scheme.ToLowerInvariant()
                ? upstream
                : new UriBuilder(upstream) { Scheme = scheme, Port = upstream.IsDefaultPort ? -1 : upstream.Port }.Uri;
        }

        /// <summary>The upstream WebSocket URI, with its scheme normalised to <c>ws</c>/<c>wss</c>.</summary>
        public Uri Upstream { get; }

        /// <summary>
        /// The credential to present upstream for this request, or null / empty for none. Default: none. Note what a
        /// constant here means — every client allowed through this endpoint acts upstream with that credential, so
        /// gate the endpoint itself (<c>.RequireAuthorization()</c>) accordingly.
        /// </summary>
        public Func<HttpContext, string?>? Credential { get; set; }

        public WebSocketProxyCredentialTransport CredentialTransport { get; set; } = WebSocketProxyCredentialTransport.Header;

        /// <summary>Header carrying the credential under <see cref="WebSocketProxyCredentialTransport.Header"/>.</summary>
        public string CredentialHeaderName { get; set; } = "Authorization";

        /// <summary>Prefix written before the credential in the header (<c>Bearer</c>); null or empty sends the bare value.</summary>
        public string? CredentialHeaderScheme { get; set; } = "Bearer";

        /// <summary>Query parameter carrying the credential under <see cref="WebSocketProxyCredentialTransport.QueryString"/>.</summary>
        public string CredentialQueryParameter { get; set; } = "token";

        /// <summary>
        /// UNSAFE: accept any upstream TLS certificate — expired, self-signed, or issued for another host — which leaves the
        /// upstream connection open to interception. Off by default; meant for a local development certificate only.
        /// </summary>
        public bool DangerousAcceptAnyServerCertificate { get; set; }

        /// <summary>Further configuration of the upstream client (sub-protocols, keep-alive, proxy); runs last.</summary>
        public Action<ClientWebSocketOptions>? ConfigureClient { get; set; }

        /// <summary>How long a relayed close waits for the other side's acknowledgement before both sockets are aborted.</summary>
        public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Receive buffer per direction. Frames are relayed as they arrive, so this bounds memory per connection.</summary>
        public int BufferSize { get; set; } = 16 * 1024;

        internal void Validate()
        {
            if (BufferSize <= 0) throw new ArgumentOutOfRangeException(nameof(BufferSize), BufferSize, "Must be positive.");
            if (CloseTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(CloseTimeout), CloseTimeout, "Must be positive.");
            if (CredentialTransport == WebSocketProxyCredentialTransport.Header && string.IsNullOrWhiteSpace(CredentialHeaderName))
                throw new ArgumentException("CredentialHeaderName is required for the Header transport.", nameof(CredentialHeaderName));
            if (CredentialTransport == WebSocketProxyCredentialTransport.QueryString && string.IsNullOrWhiteSpace(CredentialQueryParameter))
                throw new ArgumentException("CredentialQueryParameter is required for the QueryString transport.", nameof(CredentialQueryParameter));
        }

        /// <summary>The URI to connect to for a request presenting <paramref name="credential"/>.</summary>
        internal Uri BuildUpstreamUri(string? credential)
        {
            if (string.IsNullOrEmpty(credential) || CredentialTransport != WebSocketProxyCredentialTransport.QueryString)
            {
                return Upstream;
            }

            var builder = new UriBuilder(Upstream) { Port = Upstream.IsDefaultPort ? -1 : Upstream.Port };
            var parameter = Uri.EscapeDataString(CredentialQueryParameter) + "=" + Uri.EscapeDataString(credential);
            var existing = builder.Query.TrimStart('?');
            builder.Query = existing.Length == 0 ? parameter : existing + "&" + parameter;
            return builder.Uri;
        }

        /// <summary>Applies the certificate policy, the header credential and <see cref="ConfigureClient"/> to an upstream client.</summary>
        internal void ConfigureUpstream(ClientWebSocketOptions options, string? credential)
        {
            if (DangerousAcceptAnyServerCertificate)
            {
                options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            if (!string.IsNullOrEmpty(credential) && CredentialTransport == WebSocketProxyCredentialTransport.Header)
            {
                options.SetRequestHeader(CredentialHeaderName,
                    string.IsNullOrEmpty(CredentialHeaderScheme) ? credential : CredentialHeaderScheme + " " + credential);
            }

            ConfigureClient?.Invoke(options);
        }
    }
}
