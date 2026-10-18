using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.AspNetCore;

/// <summary>
/// The decision behind <see cref="BirkoSecurityOptions.LoopbackBypass"/> (TASK-522, from DraCode's <c>DaemonLoopback</c>):
/// a server that only listens on loopback can only be reached from this machine, so a local tool may call it without a
/// token.
/// </summary>
public static class LoopbackBypass
{
    /// <summary>The authentication type of the principal a bypassed request runs as.</summary>
    public const string AuthenticationType = "LoopbackBypass";

    /// <summary>
    /// True only when <paramref name="enabled"/>, at least one address is known, and every address is loopback
    /// (<c>localhost</c>, <c>127.0.0.0/8</c>, <c>::1</c>). Fails safe: no addresses, an unparsable one, or a wildcard
    /// bind (<c>0.0.0.0</c>, <c>[::]</c>, <c>*</c>, <c>+</c>), which accepts remote connections, all enforce.
    /// </summary>
    public static bool ShouldBypass(bool enabled, IEnumerable<string>? addresses)
    {
        if (!enabled) return false;
        var list = addresses?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? new List<string>();
        return list.Count > 0 && list.All(IsLoopback);
    }

    private static bool IsLoopback(string address)
    {
        var trimmed = address.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("http://" + trimmed, UriKind.Absolute, out uri))
        {
            return false;
        }
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip);
    }
}

/// <summary>
/// Runs an unauthenticated request as a principal with the wildcard permission when
/// <see cref="BirkoSecurityOptions.LoopbackBypass"/> is on and the server is bound to loopback only. A request that
/// authenticated keeps its own principal.
/// </summary>
/// <remarks>
/// The addresses are the ones the server <b>actually</b> bound (<see cref="IServerAddressesFeature"/>), read per request.
/// DraCode decided once at startup from <c>app.Urls</c> / the <c>urls</c> setting, which a code-level Kestrel
/// <c>Listen</c> overrides: measured with <c>--urls http://127.0.0.1:0</c> and <c>ListenAnyIP</c>, it bypassed while
/// bound to <c>[::]</c>, i.e. unauthenticated access from any interface. Unknown addresses (a server that reports none,
/// e.g. a test host) enforce.
/// <para>
/// ⚠ A reverse proxy on the same machine forwards remote callers over loopback. The bypass cannot tell them apart; never
/// enable it behind one.
/// </para>
/// </remarks>
public sealed class LoopbackBypassMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BirkoSecurityOptions _options;

    public LoopbackBypassMiddleware(RequestDelegate next, BirkoSecurityOptions options)
    {
        _next = next;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_options.LoopbackBypass && context.User?.Identity?.IsAuthenticated != true)
        {
            var addresses = context.RequestServices.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (LoopbackBypass.ShouldBypass(true, addresses))
            {
                var claims = _options.Jwt.Claims;
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[]
                    {
                        new Claim(claims.PermissionClaim, "*"),
                        new Claim(claims.UserIdClaim, Guid.Empty.ToString()),
                    },
                    LoopbackBypass.AuthenticationType));
            }
        }
        return _next(context);
    }
}
