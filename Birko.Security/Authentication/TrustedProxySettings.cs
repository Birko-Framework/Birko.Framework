using System.Collections.Generic;

namespace Birko.Security.Authentication
{
    /// <summary>
    /// The one list of reverse proxies an ASP.NET host believes <c>X-Forwarded-*</c> from (TASK-829), as registered by
    /// <c>AddBirkoForwardedHeaders</c> (Birko.Security.AspNetCore). That registration configures ASP.NET's middleware and
    /// the loopback check from the same entries; this type is how a component outside Birko.Security.AspNetCore (the
    /// WebSocket token binding) sees them.
    /// </summary>
    /// <remarks>
    /// The entry format is <see cref="AuthenticationConfiguration.TrustedProxies"/>'s, parsed by the same
    /// <see cref="TrustedProxyList"/>: an address or a CIDR network, <c>${ENV_VAR}</c> supported. The transport
    /// configurations keep their own list for the <c>HttpListener</c> hosts that no ASP.NET middleware runs in.
    /// </remarks>
    public class TrustedProxySettings
    {
        /// <summary>
        /// The <c>HttpContext.Items</c> key set on every request once the forwarded-headers middleware has run with this
        /// list (whether or not the peer was trusted), so a later reader takes <c>Connection.RemoteIpAddress</c> as the
        /// client rather than walking the headers again. Items are server-side; a client cannot set it.
        /// </summary>
        public const string ResolvedItemKey = "Birko.ForwardedHeaders.Resolved";

        /// <summary>
        /// The proxies whose forwarding headers are believed: an address (<c>10.0.0.5</c>) or a CIDR network
        /// (<c>172.18.0.0/16</c>). Empty trusts nobody.
        /// </summary>
        public List<string> TrustedProxies { get; set; } = new();
    }
}
