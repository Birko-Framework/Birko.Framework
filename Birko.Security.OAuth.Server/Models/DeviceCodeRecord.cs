using System;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// State of an RFC 8628 Device Authorization Grant request between
/// /device_authorization and the eventual /token poll.
/// </summary>
public class DeviceCodeRecord : AbstractModel
{
    /// <summary>Long opaque code returned to the device. Used as the polling key at /token.</summary>
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>Short user-visible code typed into the verification URI.</summary>
    public string UserCode { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Approval state — see <see cref="DeviceCodeStatus"/>.</summary>
    public DeviceCodeStatus Status { get; set; } = DeviceCodeStatus.Pending;

    /// <summary>Set once a user authorizes the device — becomes the <c>sub</c> claim.</summary>
    public string? UserId { get; set; }

    /// <summary>UTC time of the last successful poll — used to enforce <c>slow_down</c>.</summary>
    public DateTime? LastPolledAt { get; set; }
}

/// <summary>Current state of a device authorization request.</summary>
public enum DeviceCodeStatus
{
    /// <summary>User has not yet visited the verification URI.</summary>
    Pending = 0,

    /// <summary>User has authorized — next /token poll will return a token.</summary>
    Authorized = 1,

    /// <summary>User has explicitly denied — /token returns <c>access_denied</c>.</summary>
    Denied = 2
}
