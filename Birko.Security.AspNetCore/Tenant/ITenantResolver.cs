using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Resolves the current tenant from an HTTP request.
/// </summary>
public interface ITenantResolver
{
    Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default);
}

/// <summary>
/// Resolved tenant information.
/// </summary>
public sealed record TenantInfo(
    Guid TenantId,
    string? TenantName = null,
    Dictionary<string, string>? Metadata = null);
