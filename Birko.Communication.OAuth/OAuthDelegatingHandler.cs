using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth;

/// <summary>
/// A <see cref="DelegatingHandler"/> that automatically attaches OAuth2 Bearer tokens to outgoing HTTP requests.
/// Tokens are obtained and refreshed via the provided <see cref="IOAuthClient"/>.
/// <para>
/// Usage: <c>new HttpClient(new OAuthDelegatingHandler(oauthClient) { InnerHandler = new HttpClientHandler() })</c>
/// </para>
/// </summary>
public class OAuthDelegatingHandler : DelegatingHandler
{
    private readonly IOAuthClient _oauthClient;

    /// <summary>
    /// Creates a new handler that attaches OAuth2 Bearer tokens from the specified client.
    /// </summary>
    public OAuthDelegatingHandler(IOAuthClient oauthClient)
    {
        _oauthClient = oauthClient ?? throw new System.ArgumentNullException(nameof(oauthClient));
    }

    /// <summary>
    /// Creates a new handler with an inner handler.
    /// </summary>
    public OAuthDelegatingHandler(IOAuthClient oauthClient, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _oauthClient = oauthClient ?? throw new System.ArgumentNullException(nameof(oauthClient));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _oauthClient.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);

        // Buffer any body up front so the request can be cloned and resent on a 401 (the real
        // handlers mark an HttpRequestMessage as sent and dispose its content stream, so the same
        // instance can't be sent twice — CR-H030). No-op for bodyless requests.
        if (request.Content != null)
        {
            await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry once with a fresh token on 401 — on a CLONE, never the already-sent instance.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            token = await _oauthClient.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            var retry = await CloneRequestAsync(request).ConfigureAwait(false);
            retry.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            response.Dispose();
            response = await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// Produces a fresh, sendable copy of a request (method, URI, version, headers, options, and a
    /// buffered copy of the body). Required because an HttpRequestMessage may only be sent once.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        if (request.Content != null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = content;
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            ((System.Collections.Generic.IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        return clone;
    }
}
