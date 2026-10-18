using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Endpoints.Authorize;
using Birko.Security.OAuth.Server.Endpoints.ClientRegistration;
using Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;
using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Stores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Birko.Security.OAuth.Server.AspNetCore;

/// <summary>
/// Maps the <see cref="OAuthServer"/> handlers onto ASP.NET Core minimal-API routes: form and HTTP Basic parsing,
/// RFC 6749 error bodies and status codes, the signed-in user for <c>/authorize</c> and device approval, and gated
/// dynamic registration. The handlers stay ASP.NET-free; this is the HTTP shell around them.
/// </summary>
public static class OAuthServerEndpointExtensions
{
    // The RFC wire format is snake_case: payloads are built with snake_case names, nulls omitted, no naming policy.
    private static readonly JsonSerializerOptions WireJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
    };

    /// <summary>
    /// Maps <c>/token</c>, <c>/device_authorization</c>, <c>/device/approve</c>, <c>/authorize</c> and (when
    /// <see cref="OAuthServerEndpointOptions.AllowDynamicRegistration"/>) <c>/register</c>. The <see cref="OAuthServer"/>
    /// is resolved from DI per request; the disable endpoint also resolves <see cref="IOAuthClientStore"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthServer(this IEndpointRouteBuilder app, Action<OAuthServerEndpointOptions>? configure = null)
    {
        if (app == null) throw new ArgumentNullException(nameof(app));
        var options = new OAuthServerEndpointOptions();
        configure?.Invoke(options);

        // /token and /device_authorization authenticate the client in the request itself, not with a bearer token.
        app.MapPost(options.TokenPath, Handler(http => HandleTokenAsync(http, options)));
        app.MapPost(options.DeviceAuthorizationPath, Handler(HandleDeviceAuthorizationAsync));

        // These act for the signed-in user, whose id comes from the principal, never from a request field.
        var approve = app.MapPost(options.DeviceApprovalPath, Handler(http => HandleDeviceApproveAsync(http, options)))
            .RequireAuthorization();
        options.ConfigureUserEndpoints?.Invoke(approve);
        var authorize = app.MapGet(options.AuthorizePath, Handler(http => HandleAuthorizeAsync(http, options)))
            .RequireAuthorization();
        options.ConfigureUserEndpoints?.Invoke(authorize);

        if (options.AllowDynamicRegistration)
        {
            var register = app.MapPost(options.RegistrationPath, Handler(HandleRegisterAsync))
                .RequireAuthorization();
            options.ConfigureRegistration?.Invoke(register);
            var disable = app.MapPost(options.RegistrationPath.TrimEnd('/') + "/{clientId}/disable",
                    (string clientId, HttpContext http) => HandleDisableClientAsync(clientId, http))
                .RequireAuthorization();
            options.ConfigureRegistration?.Invoke(disable);
        }

        return app;
    }

    private static async Task<IResult> HandleTokenAsync(HttpContext http, OAuthServerEndpointOptions options)
    {
        NoStore(http);
        var req = http.Request;
        if (!req.HasFormContentType)
            return Error("invalid_request", "Expected application/x-www-form-urlencoded.");

        var form = await req.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var basic = ParseBasic(req);
        if (basic.Malformed)
            return Error(OAuthErrorCodes.InvalidClient, "Malformed HTTP Basic credentials.", usedBasic: true);

        var bodyId = Val(form["client_id"]);
        var bodySecret = Val(form["client_secret"]);
        // RFC 6749 §2.3: a client uses one authentication method. A body secret beside Basic, or a body client_id that
        // names another client, is refused rather than resolved by picking one.
        if (basic.ClientId != null && bodySecret != null)
            return Error("invalid_request", "Use either HTTP Basic or client_secret in the body, not both.");
        if (basic.ClientId != null && bodyId != null && !string.Equals(basic.ClientId, bodyId, StringComparison.Ordinal))
            return Error("invalid_request", "client_id in the body does not match the HTTP Basic credentials.");

        var request = new TokenRequest
        {
            GrantType = Val(form["grant_type"]) ?? string.Empty,
            ClientId = basic.ClientId ?? bodyId ?? string.Empty,
            ClientSecret = basic.ClientSecret ?? bodySecret,
            Code = Val(form["code"]),
            RedirectUri = Val(form["redirect_uri"]),
            CodeVerifier = Val(form["code_verifier"]),
            RefreshToken = Val(form["refresh_token"]),
            DeviceCode = Val(form["device_code"]),
            Scope = Val(form["scope"]),
        };

        try
        {
            var response = options.GrantHandlers.TryGetValue(request.GrantType, out var handler)
                ? await handler(request, http, http.RequestAborted).ConfigureAwait(false)
                : await Server(http).Token.HandleAsync(request, http.RequestAborted).ConfigureAwait(false);
            return Results.Json(new
            {
                access_token = response.AccessToken,
                token_type = response.TokenType,
                expires_in = response.ExpiresIn,
                refresh_token = response.RefreshToken,
                scope = response.Scope,
            }, WireJson);
        }
        catch (OAuthServerException ex)
        {
            return Error(ex, usedBasic: basic.ClientId != null);
        }
    }

    private static async Task<IResult> HandleDeviceAuthorizationAsync(HttpContext http)
    {
        NoStore(http);
        if (!http.Request.HasFormContentType)
            return Error("invalid_request", "Expected application/x-www-form-urlencoded.");

        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var request = new DeviceAuthorizationRequest
        {
            ClientId = Val(form["client_id"]) ?? string.Empty,
            Scope = Val(form["scope"]),
        };

        try
        {
            var r = await Server(http).DeviceAuthorization.HandleAsync(request, http.RequestAborted).ConfigureAwait(false);
            return Results.Json(new
            {
                device_code = r.DeviceCode,
                user_code = r.UserCode,
                verification_uri = r.VerificationUri,
                verification_uri_complete = r.VerificationUriComplete,
                expires_in = r.ExpiresIn,
                interval = r.Interval,
            }, WireJson);
        }
        catch (OAuthServerException ex)
        {
            return Error(ex);
        }
    }

    private sealed record DeviceApproval(
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("approved")] bool Approved);

    private static async Task<IResult> HandleDeviceApproveAsync(HttpContext http, OAuthServerEndpointOptions options)
    {
        var userId = options.ResolveUserId(http.User);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        DeviceApproval? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<DeviceApproval>(http.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Error("invalid_request", "Expected a JSON body { user_code, approved }.");
        }
        if (body == null || string.IsNullOrEmpty(body.UserCode))
            return Error("invalid_request", "user_code is required.");

        try
        {
            await Server(http).DeviceAuthorization.ApproveAsync(body.UserCode, userId, body.Approved, http.RequestAborted).ConfigureAwait(false);
            return Results.Json(new { status = "ok" }, WireJson);
        }
        catch (OAuthServerException ex)
        {
            return Error(ex);
        }
    }

    private static async Task<IResult> HandleAuthorizeAsync(HttpContext http, OAuthServerEndpointOptions options)
    {
        var userId = options.ResolveUserId(http.User);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var q = http.Request.Query;
        var request = new AuthorizeRequest
        {
            ResponseType = Val(q["response_type"]) ?? "code",
            ClientId = Val(q["client_id"]) ?? string.Empty,
            RedirectUri = Val(q["redirect_uri"]) ?? string.Empty,
            Scope = Val(q["scope"]),
            State = Val(q["state"]),
            CodeChallenge = Val(q["code_challenge"]),
            CodeChallengeMethod = Val(q["code_challenge_method"]),
        };

        AuthorizeResponse r;
        try
        {
            r = await Server(http).Authorize.HandleAuthorizeAsync(request, userId, http.RequestAborted).ConfigureAwait(false);
        }
        catch (OAuthServerException ex)
        {
            // Never redirected: the error may be the redirect_uri itself, and sending a user to an unvalidated URI is the
            // open redirect RFC 6749 §4.1.2.1 forbids.
            return Error(ex);
        }

        if (options.AuthorizeResponseMode == OAuthAuthorizeResponseMode.Redirect && !r.RequiresConsent && r.Code != null)
        {
            var query = new Dictionary<string, string?> { ["code"] = r.Code };
            if (r.State != null) query["state"] = r.State;
            return Results.Redirect(QueryHelpers.AddQueryString(r.RedirectUri, query));
        }

        return Results.Json(new
        {
            requires_consent = r.RequiresConsent,
            requested_scope = r.RequestedScope,
            code = r.Code,
            redirect_uri = r.RedirectUri,
            state = r.State,
        }, WireJson);
    }

    // camelCase as minimal-API binding reads it, plus string enums: the response names the client type as a string
    // ("Confidential"), and plain binding refused that same string back with a 400 — only the number was accepted.
    private static readonly JsonSerializerOptions RegistrationJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static async Task<IResult> HandleRegisterAsync(HttpContext http)
    {
        ClientRegistrationRequest? body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<ClientRegistrationRequest>(RegistrationJson, http.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Error("invalid_request", "Expected a JSON client registration body.");
        }
        if (body == null)
            return Error("invalid_request", "Expected a JSON client registration body.");

        try
        {
            var r = await Server(http).ClientRegistration.RegisterAsync(body, http.RequestAborted).ConfigureAwait(false);
            NoStore(http); // the response carries the client secret
            return Results.Json(new
            {
                client_id = r.ClientId,
                client_secret = r.ClientSecret,
                client_type = r.ClientType.ToString(),
                name = r.Name,
                redirect_uris = r.RedirectUris,
                allowed_grant_types = r.AllowedGrantTypes,
                allowed_scopes = r.AllowedScopes,
                created_at = r.CreatedAt,
            }, WireJson, statusCode: StatusCodes.Status201Created);
        }
        catch (OAuthServerException ex)
        {
            return Error(ex);
        }
    }

    private static async Task<IResult> HandleDisableClientAsync(string clientId, HttpContext http)
    {
        var clients = http.RequestServices.GetRequiredService<IOAuthClientStore>();
        var client = await clients.GetByClientIdAsync(clientId, http.RequestAborted).ConfigureAwait(false);
        if (client == null)
            return Results.Json(new { error = "unknown_client" }, WireJson, statusCode: StatusCodes.Status404NotFound);

        if (client.IsEnabled)
        {
            client.IsEnabled = false;
            await clients.UpdateAsync(client, ct: http.RequestAborted).ConfigureAwait(false);
        }
        return Results.Json(new { status = "disabled", client_id = clientId }, WireJson);
    }

    // Typed as a Delegate so MapPost/MapGet bind the RouteHandlerBuilder overload: an HttpContext => Task lambda would
    // otherwise bind RequestDelegate, which writes nothing for the returned IResult.
    private static Delegate Handler(Func<HttpContext, Task<IResult>> handler) => handler;

    private static OAuthServer Server(HttpContext http) => http.RequestServices.GetRequiredService<OAuthServer>();

    /// <summary>RFC 6749 §5.1: token-endpoint responses carry credentials and must not be cached.</summary>
    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
    }

    private static IResult Error(OAuthServerException ex, bool usedBasic = false) =>
        Error(ex.ErrorCode, ex.ErrorDescription, usedBasic, ex.ErrorUri);

    /// <summary>
    /// RFC 6749 §5.2: 400, except <c>invalid_client</c> after HTTP Basic, which is 401 with a <c>WWW-Authenticate</c>
    /// challenge; <c>server_error</c> is 500 and <c>temporarily_unavailable</c> 503 (§4.1.2.1's meanings).
    /// </summary>
    private static IResult Error(string code, string? description, bool usedBasic = false, string? uri = null)
    {
        var status = code switch
        {
            OAuthErrorCodes.InvalidClient when usedBasic => StatusCodes.Status401Unauthorized,
            OAuthErrorCodes.ServerError => StatusCodes.Status500InternalServerError,
            OAuthErrorCodes.TemporarilyUnavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        var body = Results.Json(new { error = code, error_description = description, error_uri = uri }, WireJson, statusCode: status);
        return status == StatusCodes.Status401Unauthorized ? new WithChallenge(body) : body;
    }

    private sealed class WithChallenge(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"oauth\"";
            return inner.ExecuteAsync(http);
        }
    }

    private static string? Val(StringValues value) => StringValues.IsNullOrEmpty(value) ? null : value.ToString();

    private readonly record struct BasicCredentials(string? ClientId, string? ClientSecret, bool Malformed);

    /// <summary>
    /// RFC 6749 §2.3.1: the id and secret are form-urlencoded before Base64, so they are form-decoded ('+' is a
    /// space). A Basic header that does not decode is reported, not ignored: ignoring it fell back to the body.
    /// </summary>
    private static BasicCredentials ParseBasic(HttpRequest req)
    {
        var header = req.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return default;
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
            var sep = decoded.IndexOf(':');
            if (sep <= 0) return new BasicCredentials(null, null, true);
            return new BasicCredentials(WebUtility.UrlDecode(decoded[..sep]), WebUtility.UrlDecode(decoded[(sep + 1)..]), false);
        }
        catch (FormatException)
        {
            return new BasicCredentials(null, null, true);
        }
    }
}
