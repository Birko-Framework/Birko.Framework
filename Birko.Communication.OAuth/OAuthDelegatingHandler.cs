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

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Retry once with fresh token on 401
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            token = await _oauthClient.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}
