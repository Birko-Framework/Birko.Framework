using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Endpoints.Token;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.OAuth.Server.AspNetCore;

/// <summary>
/// Takes over one grant type of the <c>/token</c> endpoint. It receives the request as parsed from the form and HTTP
/// Basic credentials, and returns the token response; throw <see cref="OAuthServerException"/> for a protocol error.
/// The framework authenticates nothing on this path: the handler owns client authentication for its grant.
/// </summary>
public delegate Task<TokenResponse> OAuthGrantHandler(TokenRequest request, HttpContext http, CancellationToken cancellationToken);

/// <summary>How <c>/authorize</c> answers once it has a code (or an error that can be sent back to the client).</summary>
public enum OAuthAuthorizeResponseMode
{
    /// <summary>
    /// A JSON body (<c>requires_consent</c>, <c>code</c>, <c>redirect_uri</c>, <c>state</c>, …) for a single-page app
    /// that drives the redirect itself. The default, as DraCode's original.
    /// </summary>
    Json,

    /// <summary>
    /// RFC 6749 §4.1.2: a 302 to the registered <c>redirect_uri</c> with <c>code</c> and <c>state</c>. A request that
    /// still needs consent is answered as JSON, since the consent UI is the app's.
    /// </summary>
    Redirect,
}

/// <summary>Routes and policies of <see cref="OAuthServerEndpointExtensions.MapOAuthServer"/>.</summary>
public sealed class OAuthServerEndpointOptions
{
    public string TokenPath { get; set; } = "/token";
    public string DeviceAuthorizationPath { get; set; } = "/device_authorization";
    public string DeviceApprovalPath { get; set; } = "/device/approve";
    public string AuthorizePath { get; set; } = "/authorize";
    public string RegistrationPath { get; set; } = "/register";

    /// <summary>
    /// Maps RFC 7591 dynamic client registration (<c>POST {RegistrationPath}</c>) and
    /// <c>POST {RegistrationPath}/{clientId}/disable</c>. Default false: an open registration endpoint lets anyone
    /// create clients, so it must be switched on deliberately, and should be gated by <see cref="ConfigureRegistration"/>.
    /// </summary>
    public bool AllowDynamicRegistration { get; set; }

    /// <summary>
    /// Applied to the registration endpoints, e.g. <c>b =&gt; b.RequireAuthorization("admin")</c>. They always require an
    /// authenticated caller; this adds the app's own policy.
    /// </summary>
    public Action<RouteHandlerBuilder>? ConfigureRegistration { get; set; }

    /// <summary>
    /// Applied to <c>/authorize</c> and the device approval, which act for the signed-in user and always require
    /// authorization; this adds the app's own policy.
    /// </summary>
    public Action<RouteHandlerBuilder>? ConfigureUserEndpoints { get; set; }

    /// <summary>
    /// Grant types the app handles itself (keyed by <c>grant_type</c>). A key may also name a grant the server does not
    /// know (an extension grant).
    /// </summary>
    public IDictionary<string, OAuthGrantHandler> GrantHandlers { get; } = new Dictionary<string, OAuthGrantHandler>(StringComparer.Ordinal);

    /// <summary>How <c>/authorize</c> answers. Default <see cref="OAuthAuthorizeResponseMode.Json"/>.</summary>
    public OAuthAuthorizeResponseMode AuthorizeResponseMode { get; set; } = OAuthAuthorizeResponseMode.Json;

    /// <summary>
    /// Reads the signed-in user's id from the request principal. Default: the <c>sub</c> claim, else
    /// <see cref="ClaimTypes.NameIdentifier"/>. Never a request field: a body or query value is spoofable.
    /// </summary>
    public Func<ClaimsPrincipal, string?> ResolveUserId { get; set; } = DefaultUserId;

    internal static string? DefaultUserId(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
