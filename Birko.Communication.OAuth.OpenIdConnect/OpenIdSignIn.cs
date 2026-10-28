using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>Where to send the browser to sign in, and the <c>state</c> the callback will carry back.</summary>
public sealed record OpenIdSignInStart(string AuthorizationUrl, string State);

/// <summary>The query of the provider's redirect to the app's callback.</summary>
/// <param name="State">The <c>state</c> parameter.</param>
/// <param name="Code">The <c>code</c> parameter, on success.</param>
/// <param name="Error">The <c>error</c> parameter, when the provider refused (e.g. the user cancelled: <c>access_denied</c>).</param>
/// <param name="ErrorDescription">The <c>error_description</c> parameter.</param>
public sealed record OpenIdCallback(string? State, string? Code, string? Error = null, string? ErrorDescription = null);

/// <summary>How a sign-in callback ended.</summary>
public enum OpenIdSignInStatus
{
    /// <summary>The <c>id_token</c> is verified; <see cref="OpenIdSignInResult.Identity"/> is set.</summary>
    Succeeded,
    /// <summary>The state is unknown, expired, already used, or was started by another browser. Start over.</summary>
    InvalidState,
    /// <summary>The provider redirected back with an <c>error</c> (the user cancelled, consent was refused).</summary>
    ProviderRefused,
    /// <summary>The token endpoint refused the code (expired, reused, a wrong redirect URI or client secret).</summary>
    CodeRejected,
    /// <summary>The token response had no valid <c>id_token</c>: a forged, expired or foreign token, a wrong nonce.</summary>
    InvalidIdToken,
    /// <summary>The provider could not be reached, or answered with a server error. Try again later.</summary>
    ProviderUnavailable,
}

/// <summary>The outcome of <see cref="IOpenIdSignIn.CompleteAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Identity">The verified identity when <see cref="OpenIdSignInStatus.Succeeded"/>, else null.</param>
/// <param name="Data">The value passed to <see cref="IOpenIdSignIn.BeginAsync"/> (e.g. a return URL), whenever the state was
/// valid; null when it was not.</param>
/// <param name="Error">The provider's error code, or this library's reason, for logs. Never show it to the user.</param>
/// <param name="ErrorDescription">The provider's error description, or null.</param>
public sealed record OpenIdSignInResult(
    OpenIdSignInStatus Status, OpenIdIdentity? Identity, string? Data, string? Error = null, string? ErrorDescription = null)
{
    /// <summary>The sign-in succeeded and <see cref="Identity"/> is set.</summary>
    public bool Succeeded => Status == OpenIdSignInStatus.Succeeded;
}

/// <summary>
/// An OpenID Connect web sign-in with one provider: authorization code, PKCE, <c>state</c> bound to the browser,
/// <c>nonce</c>, and a verified <c>id_token</c> (TASK-778). What happens next — who may sign in, creating the user, the
/// app's cookie and session — is the app's.
/// </summary>
public interface IOpenIdSignIn
{
    /// <summary>The provider's name (<see cref="OpenIdProviderOptions.Name"/>).</summary>
    string Provider { get; }

    /// <summary>
    /// Starts a sign-in: stores a fresh state (with the nonce, the PKCE verifier and <paramref name="data"/>) bound to
    /// <paramref name="binding"/>, and returns the URL to redirect the browser to.
    /// </summary>
    /// <param name="binding">A value only this browser holds: a random, HttpOnly, SameSite=Lax cookie the app sets now and
    /// reads on the callback. It stops login CSRF (another browser's callback URL signing this one in); required.</param>
    /// <param name="data">A value handed back by <see cref="CompleteAsync"/>, e.g. a local return URL. Not secret from the
    /// app's own store, but never sent to the provider.</param>
    /// <exception cref="OpenIdProviderUnavailableException">The provider's discovery document could not be read.</exception>
    Task<OpenIdSignInStart> BeginAsync(string binding, string? data = null, CancellationToken ct = default);

    /// <summary>
    /// Completes a sign-in from the provider's redirect: consumes the state (once, and only with the binding it was
    /// created with), exchanges the code with the PKCE verifier and the client secret, and verifies the <c>id_token</c>.
    /// </summary>
    Task<OpenIdSignInResult> CompleteAsync(OpenIdCallback callback, string binding, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IOpenIdSignIn"/> over an <see cref="IOAuthStateStore"/> and the provider's discovery document. Register one
/// per provider as a singleton (it caches the provider's keys), with a shared state store.
/// </summary>
public sealed class OpenIdSignIn : IOpenIdSignIn
{
    private readonly IdTokenValidator _validator;
    private readonly IOAuthStateStore _states;
    private readonly HttpClient _http;

    /// <param name="options">The provider (<see cref="GoogleOpenIdProvider"/>, <see cref="MicrosoftOpenIdProvider"/>).</param>
    /// <param name="states">The state store; a shared one on a multi-node host.</param>
    /// <param name="http">The HttpClient every request to the provider goes through; not disposed by this class.</param>
    /// <param name="clock">The clock the key cache and token lifetime follow. Default <see cref="TimeProvider.System"/>.</param>
    public OpenIdSignIn(OpenIdProviderOptions options, IOAuthStateStore states, HttpClient http, TimeProvider? clock = null)
        : this(new IdTokenValidator(options, http, clock), states, http)
    {
    }

    /// <param name="validator">The provider's validator, when the app also uses it on its own.</param>
    /// <param name="states">The state store; a shared one on a multi-node host.</param>
    /// <param name="http">The HttpClient the code exchange goes through; not disposed by this class.</param>
    public OpenIdSignIn(IdTokenValidator validator, IOAuthStateStore states, HttpClient http)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public string Provider => _validator.Options.Name;

    /// <inheritdoc />
    public async Task<OpenIdSignInStart> BeginAsync(string binding, string? data = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(binding))
            throw new ArgumentException("A browser binding is required: an unbound state does not stop login CSRF.", nameof(binding));

        var metadata = await _validator.Metadata.GetAsync(ct).ConfigureAwait(false);
        var options = _validator.Options;
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var pkce = PkceChallenge.Generate();
        var state = await _states.CreateAsync(binding, JsonSerializer.Serialize(new Pending(nonce, pkce.CodeVerifier, data)), ct)
            .ConfigureAwait(false);

        // Built here, not with OAuthClient.BuildAuthorizationUrl: its ExtraParameters also go into the token request,
        // and the nonce belongs only in the authorization request.
        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", options.ClientId),
            new("redirect_uri", options.RedirectUri),
            new("scope", options.Scope),
            new("state", state),
            new("nonce", nonce),
            new("code_challenge", pkce.CodeChallenge),
            new("code_challenge_method", pkce.CodeChallengeMethod),
        };
        foreach (var extra in options.AuthorizationParameters) query.Add(extra);

        var endpoint = metadata.AuthorizationEndpoint;
        var separator = endpoint.Contains('?') ? '&' : '?';
        var url = endpoint + separator + string.Join('&', query.ConvertAll(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return new OpenIdSignInStart(url, state);
    }

    /// <inheritdoc />
    public async Task<OpenIdSignInResult> CompleteAsync(OpenIdCallback callback, string binding, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        // Without the binding cookie this is not the browser that started the sign-in; the state is left for it.
        if (string.IsNullOrEmpty(binding) || string.IsNullOrEmpty(callback.State))
            return new OpenIdSignInResult(OpenIdSignInStatus.InvalidState, null, null, "no state or binding");

        var entry = await _states.TryConsumeAsync(callback.State, binding, ct).ConfigureAwait(false);
        var pending = Read(entry?.Data);
        if (pending == null) return new OpenIdSignInResult(OpenIdSignInStatus.InvalidState, null, null, "unknown, used or foreign state");

        if (!string.IsNullOrEmpty(callback.Error))
            return new OpenIdSignInResult(OpenIdSignInStatus.ProviderRefused, null, pending.Data, callback.Error, callback.ErrorDescription);
        if (string.IsNullOrEmpty(callback.Code))
            return new OpenIdSignInResult(OpenIdSignInStatus.CodeRejected, null, pending.Data, "no code");

        OAuthToken token;
        try
        {
            var metadata = await _validator.Metadata.GetAsync(ct).ConfigureAwait(false);
            var options = _validator.Options;
            var settings = new OAuthSettings
            {
                GrantType = OAuthGrantType.AuthorizationCodePkce,
                TokenEndpoint = metadata.TokenEndpoint,
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret,
                RedirectUri = options.RedirectUri,
                TimeoutSeconds = (int)Math.Ceiling(options.RequestTimeout.TotalSeconds),
            };
            using var client = new OAuthClient(settings, _http);
            token = await client.ExchangeCodeAsync(callback.Code, pending.Verifier, ct).ConfigureAwait(false);
        }
        catch (OpenIdProviderUnavailableException ex)
        {
            return new OpenIdSignInResult(OpenIdSignInStatus.ProviderUnavailable, null, pending.Data, ex.Message);
        }
        catch (OAuthException ex) when (ex.ErrorCode == null && ex.StatusCode is >= 500)
        {
            return new OpenIdSignInResult(OpenIdSignInStatus.ProviderUnavailable, null, pending.Data, ex.Message);
        }
        catch (OAuthException ex)
        {
            return new OpenIdSignInResult(OpenIdSignInStatus.CodeRejected, null, pending.Data, ex.ErrorCode ?? ex.Message, ex.ErrorDescription);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new OpenIdSignInResult(OpenIdSignInStatus.ProviderUnavailable, null, pending.Data, ex.Message);
        }

        if (string.IsNullOrEmpty(token.IdToken))
            return new OpenIdSignInResult(OpenIdSignInStatus.InvalidIdToken, null, pending.Data, "no id_token in the token response");

        var verified = await _validator.ValidateAsync(token.IdToken, pending.Nonce, ct).ConfigureAwait(false);
        if (verified.ProviderUnavailable)
            return new OpenIdSignInResult(OpenIdSignInStatus.ProviderUnavailable, null, pending.Data, verified.Error);
        return verified.IsValid
            ? new OpenIdSignInResult(OpenIdSignInStatus.Succeeded, verified.Identity, pending.Data)
            : new OpenIdSignInResult(OpenIdSignInStatus.InvalidIdToken, null, pending.Data, verified.Error);
    }

    private sealed record Pending(string Nonce, string Verifier, string? Data);

    private static Pending? Read(string? json)
    {
        if (json == null) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<Pending>(json);
            return pending is { Nonce.Length: > 0, Verifier.Length: > 0 } ? pending : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
