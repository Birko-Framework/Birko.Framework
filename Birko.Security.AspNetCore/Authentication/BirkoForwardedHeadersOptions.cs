using System.Collections.Generic;
using Birko.Security.Authentication;

namespace Birko.Security.AspNetCore;

/// <summary>
/// The configuration of <see cref="ForwardedHeadersExtensions.AddBirkoForwardedHeaders(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{BirkoForwardedHeadersOptions})"/>
/// (TASK-829), bound from <see cref="SectionName"/>. The proxy list is the inherited
/// <see cref="TrustedProxySettings.TrustedProxies"/>: the one list the host believes, registered as
/// <see cref="TrustedProxySettings"/> for everything else that needs it.
/// </summary>
public sealed class BirkoForwardedHeadersOptions : TrustedProxySettings
{
    /// <summary>The configuration section <c>AddBirkoForwardedHeaders(configuration)</c> reads.</summary>
    public const string SectionName = "Birko:ForwardedHeaders";

    /// <summary>
    /// How many proxies deep <c>X-Forwarded-For</c> is read. 1: one proxy in front of the host. Two trusted proxies in
    /// a row (Cloudflare, then Nginx Proxy Manager) need 2. Must be at least 1.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Public host names a trusted proxy may forward in <c>X-Forwarded-Host</c> (<c>app.example.com</c>,
    /// <c>*.example.com</c>). Empty: the header is ignored and <c>Request.Host</c> stays what the proxy sent as
    /// <c>Host</c>. Listing any needs a non-empty proxy list.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = new();

    /// <summary>
    /// Take the scheme from <c>X-Forwarded-Proto</c>, so a proxy that terminates TLS gives the app <c>https</c>.
    /// </summary>
    public bool ForwardProto { get; set; } = true;
}
