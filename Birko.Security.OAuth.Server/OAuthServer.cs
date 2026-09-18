using System;
using Birko.Security.OAuth.Server.Endpoints.Authorize;
using Birko.Security.OAuth.Server.Endpoints.ClientRegistration;
using Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;
using Birko.Security.OAuth.Server.Endpoints.Token;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server;

/// <summary>
/// Composition root for the OAuth2 authorization server. Owns one handler per endpoint;
/// the host (ASP.NET Core, Birko.Communication.REST.Server, etc.) routes incoming
/// HTTP requests to the matching handler.
/// <para>
/// Register a single instance (typically as a singleton) and resolve it from your
/// controllers / minimal-API endpoints.
/// </para>
/// </summary>
public class OAuthServer
{
    public OAuthServerSettings Settings { get; }
    public TokenEndpointHandler Token { get; }
    public AuthorizationEndpointHandler Authorize { get; }
    public DeviceAuthorizationHandler DeviceAuthorization { get; }
    public ClientRegistrationHandler ClientRegistration { get; }

    public OAuthServer(
        OAuthServerSettings settings,
        ITokenProvider tokens,
        TokenOptions tokenOptions,
        IOAuthClientStore clientStore,
        IAuthorizationCodeStore codeStore,
        IRefreshTokenStore refreshStore,
        IDeviceCodeStore deviceStore,
        IConsentStore consentStore,
        string deviceVerificationUri,
        IDateTimeProvider? clock = null)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (deviceVerificationUri == null) throw new ArgumentNullException(nameof(deviceVerificationUri));

        Settings = settings;
        Token = new TokenEndpointHandler(settings, tokens, tokenOptions, clientStore, codeStore, refreshStore, deviceStore, clock);
        Authorize = new AuthorizationEndpointHandler(settings, clientStore, codeStore, consentStore, clock);
        DeviceAuthorization = new DeviceAuthorizationHandler(settings, clientStore, deviceStore, deviceVerificationUri, clock);
        ClientRegistration = new ClientRegistrationHandler(clientStore, clock);
    }
}
