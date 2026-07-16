using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Security.OAuth.Server.Internal;

/// <summary>
/// Shared scope arithmetic for the OAuth endpoint handlers.
/// <para>
/// CR-L348: <see cref="NarrowScope"/> was previously copy-pasted verbatim across
/// TokenEndpointHandler, AuthorizationEndpointHandler and DeviceAuthorizationHandler, and
/// <see cref="CoversAllScopes"/> / <see cref="MergeScopes"/> were duplicated in the authorize
/// handler; centralizing them keeps a single scope policy.
/// </para>
/// </summary>
internal static class ScopeUtil
{
    private static readonly char[] Separator = { ' ' };

    private static HashSet<string> ToSet(string? value) =>
        new(value?.Split(Separator, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(), StringComparer.Ordinal);

    /// <summary>
    /// Narrows the requested scope to what the client is allowed. When the server declares
    /// <paramref name="supportedScopes"/> (CR-L349: <see cref="OAuthServerSettings.SupportedScopes"/>,
    /// previously dead configuration) the allowed set is first intersected with it — an empty
    /// supported set imposes no constraint, matching the documented "if empty, any requested scope is
    /// allowed" behavior. An empty/whitespace request returns the full effective allowed set; a request
    /// none of whose scopes survive throws <c>invalid_scope</c>.
    /// </summary>
    public static string NarrowScope(string? requested, IEnumerable<string> allowed, IReadOnlyCollection<string>? supportedScopes = null)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (supportedScopes is { Count: > 0 })
        {
            allowedSet.IntersectWith(supportedScopes);
        }

        if (string.IsNullOrWhiteSpace(requested))
        {
            return string.Join(' ', allowedSet);
        }

        var granted = requested!.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(allowedSet.Contains)
            .ToArray();
        if (granted.Length == 0 && allowedSet.Count > 0)
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidScope, "None of the requested scopes are allowed for this client.");
        }
        return string.Join(' ', granted);
    }

    /// <summary>Returns true when every scope in <paramref name="requested"/> is present in <paramref name="granted"/>.</summary>
    public static bool CoversAllScopes(string granted, string requested)
    {
        var grantedSet = ToSet(granted);
        return ToSet(requested).All(grantedSet.Contains);
    }

    /// <summary>Returns the union of <paramref name="existing"/> and <paramref name="toAdd"/> as a space-delimited scope string.</summary>
    public static string MergeScopes(string existing, string toAdd)
    {
        var set = ToSet(existing);
        set.UnionWith(ToSet(toAdd));
        return string.Join(' ', set);
    }
}
