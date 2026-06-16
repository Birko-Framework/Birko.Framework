using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;

/// <summary>
/// Handles the RFC 8628 /device_authorization endpoint plus the helper API used by
/// the consent UI to mark a user_code as authorized or denied.
/// </summary>
public class DeviceAuthorizationHandler
{
    private readonly OAuthServerSettings _settings;
    private readonly IOAuthClientStore _clients;
    private readonly IDeviceCodeStore _devices;
    private readonly IDateTimeProvider _clock;
    private readonly string _verificationUri;

    public DeviceAuthorizationHandler(
        OAuthServerSettings settings,
        IOAuthClientStore clients,
        IDeviceCodeStore devices,
        string verificationUri,
        IDateTimeProvider? clock = null)
    {
        _settings = settings;
        _clients = clients;
        _devices = devices;
        _verificationUri = verificationUri ?? throw new ArgumentNullException(nameof(verificationUri));
        _clock = clock ?? new SystemDateTimeProvider();
    }

    /// <summary>Issues a new device_code + user_code pair for the client.</summary>
    public async Task<DeviceAuthorizationResponse> HandleAsync(DeviceAuthorizationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(request.ClientId))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "client_id is required.");

        var client = await _clients.GetByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client == null || !client.IsEnabled)
            throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "Unknown or disabled client.");
        if (!client.AllowedGrantTypes.Contains(OAuthGrantTypes.DeviceCode))
            throw new OAuthServerException(OAuthErrorCodes.UnauthorizedClient, "Client is not authorized for the device-code grant.");

        var scope = NarrowScope(request.Scope, client.AllowedScopes);

        var deviceCode = RandomStringGenerator.Base64Url(32);
        var userCode = RandomStringGenerator.UserCode(8);

        await _devices.CreateAsync(new DeviceCodeRecord
        {
            DeviceCode = deviceCode,
            UserCode = userCode,
            ClientId = client.ClientId,
            Scope = scope,
            ExpiresAt = _clock.UtcNow.AddSeconds(_settings.DeviceCodeLifetimeSeconds),
            Status = DeviceCodeStatus.Pending
        }, ct: ct).ConfigureAwait(false);

        return new DeviceAuthorizationResponse
        {
            DeviceCode = deviceCode,
            UserCode = userCode,
            VerificationUri = _verificationUri,
            VerificationUriComplete = $"{_verificationUri}?user_code={Uri.EscapeDataString(userCode)}",
            ExpiresIn = _settings.DeviceCodeLifetimeSeconds,
            Interval = _settings.DeviceCodePollingIntervalSeconds
        };
    }

    /// <summary>Called by the consent UI once the user has entered a user_code and clicked Allow/Deny.</summary>
    public async Task ApproveAsync(string userCode, string userId, bool approved, CancellationToken ct = default)
    {
        var device = await _devices.GetByUserCodeAsync(userCode, ct).ConfigureAwait(false)
            ?? throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Unknown user_code.");

        if (device.ExpiresAt <= _clock.UtcNow)
            throw new OAuthServerException(OAuthErrorCodes.ExpiredToken, "Device code has expired.");

        device.Status = approved ? DeviceCodeStatus.Authorized : DeviceCodeStatus.Denied;
        device.UserId = approved ? userId : null;
        await _devices.UpdateAsync(device, ct: ct).ConfigureAwait(false);
    }

    private static string NarrowScope(string? requested, IEnumerable<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(requested))
        {
            return string.Join(' ', allowedSet);
        }
        var requestedScopes = requested!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var granted = requestedScopes.Where(allowedSet.Contains).ToArray();
        if (granted.Length == 0 && allowedSet.Count > 0)
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidScope, "None of the requested scopes are allowed for this client.");
        }
        return string.Join(' ', granted);
    }
}
