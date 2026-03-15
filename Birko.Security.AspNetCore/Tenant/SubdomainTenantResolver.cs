using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Resolves tenant from the request hostname subdomain.
/// Example: "tenant1.example.com" → TenantName = "tenant1".
/// Requires a lookup function to map tenant name → TenantId.
/// </summary>
public sealed class SubdomainTenantResolver : ITenantResolver
{
    private readonly Func<string, CancellationToken, Task<TenantInfo?>> _lookupAsync;
    private readonly string? _baseDomain;

    /// <param name="lookupAsync">Maps a subdomain string to TenantInfo (e.g. database lookup).</param>
    /// <param name="baseDomain">
    /// Optional base domain to strip (e.g. "example.com").
    /// If null, assumes the first segment of the hostname is the tenant.
    /// </param>
    public SubdomainTenantResolver(
        Func<string, CancellationToken, Task<TenantInfo?>> lookupAsync,
        string? baseDomain = null)
    {
        _lookupAsync = lookupAsync;
        _baseDomain = baseDomain?.TrimStart('.');
    }

    public async Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default)
    {
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        string? subdomain;

        if (_baseDomain is not null)
        {
            // "tenant1.example.com" with baseDomain "example.com" → "tenant1"
            if (!host.EndsWith("." + _baseDomain, StringComparison.OrdinalIgnoreCase))
                return null;

            subdomain = host[..^(_baseDomain.Length + 1)];
        }
        else
        {
            // Take first segment: "tenant1.example.com" → "tenant1"
            var dotIndex = host.IndexOf('.');
            if (dotIndex <= 0)
                return null;

            subdomain = host[..dotIndex];
        }

        if (string.IsNullOrEmpty(subdomain))
            return null;

        return await _lookupAsync(subdomain, ct);
    }
}
