using System;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// Thrown by the tenant store wrappers under <see cref="Models.TenantIsolationMode.Strict"/> when an
/// operation needs a tenant and none is in scope.
///
/// <para>
/// <b>Why a dedicated type.</b> The wrappers used to throw a bare <see cref="InvalidOperationException"/>,
/// which a host has no way to tell apart from any other invalid-state bug — so every such refusal surfaced
/// as an unhandled <c>500 "An unexpected error occurred."</c>. That is the wrong answer to a well-formed
/// request that is merely missing tenant context: the correct answer is a <c>400</c> naming the cause, so a
/// caller can tell "you forgot the tenant header" from "the server is broken". Measured on Symbio TASK-271:
/// <b>115 of 170</b> parameterless GET routes returned 500 when called without an <c>X-Tenant-Id</c> header
/// — the two that were originally reported were not the population, and no per-endpoint guard could have
/// been the fix.
/// </para>
/// <para>
/// Derives from <see cref="InvalidOperationException"/> on purpose, mirroring how
/// <see cref="TenantMismatchException"/> derives from <see cref="UnauthorizedAccessException"/>: every
/// existing <c>catch (InvalidOperationException)</c> keeps working unchanged, while a host that wants to
/// report the missing-tenant case distinctly catches this type first.
/// </para>
/// <para>
/// <b>Distinct from <see cref="TenantMismatchException"/></b>, which means "a tenant IS in scope and the row
/// belongs to a different one" (an authorization-shaped refusal → 403). This one means "no tenant is in
/// scope at all" (a request-shaped problem → 400). Conflating them would tell a caller missing a header to
/// go hunting for a permission.
/// </para>
/// </summary>
public class TenantScopeRequiredException : InvalidOperationException
{
    /// <summary>
    /// The operation that was refused — <c>read</c> for the read/count/filter-write seam, <c>create</c> when
    /// refusing to stamp <see cref="Guid.Empty"/> onto a new item.
    /// </summary>
    public string Operation { get; }

    /// <summary>Name of the entity type the operation targeted, or <c>null</c> when not applicable.</summary>
    public string? EntityType { get; }

    /// <summary>
    /// Creates the exception. The message deliberately keeps the wording the wrappers have always used, so
    /// logs and any message-matching tests stay stable.
    /// </summary>
    public TenantScopeRequiredException(string operation, string? entityType, string message)
        : base(message)
    {
        Operation = operation;
        EntityType = entityType;
    }
}
