using System;

namespace Birko.Data.Tenant.Stores;

/// <summary>
/// Thrown by the tenant store wrappers when a write targets an entity that belongs to a different tenant
/// than the one in scope.
///
/// <para>
/// <b>Why a dedicated type.</b> The wrappers used to throw a bare
/// <see cref="UnauthorizedAccessException"/>, which a host has no way to tell apart from an authorization
/// failure — so the refusal surfaced to callers as a generic "you are not authorized" 403 and read as a
/// missing permission. It is not: the caller may hold every permission the operation needs and still be
/// refused because the row is another tenant's. Conflating the two costs real diagnosis time (measured on
/// Symbio TASK-290, where an hour went into a permission hunt for a tenant-scope refusal).
/// </para>
/// <para>
/// Derives from <see cref="UnauthorizedAccessException"/> on purpose: every existing
/// <c>catch (UnauthorizedAccessException)</c> and every host that maps that type to 403 keeps working
/// unchanged, while a host that wants to report the tenant case distinctly can catch this type first.
/// </para>
/// <para>
/// The <see cref="ExpectedTenantGuid"/>/<see cref="ActualTenantGuid"/> values are for the server's log.
/// Do not echo them to the caller — which tenant owns a row is exactly what a caller probing ids wants
/// to learn.
/// </para>
/// </summary>
public class TenantMismatchException : UnauthorizedAccessException
{
    /// <summary>The operation that was refused, e.g. <c>update</c> or <c>delete</c>.</summary>
    public string Operation { get; }

    /// <summary>Name of the entity type the operation targeted.</summary>
    public string EntityType { get; }

    /// <summary>The tenant in scope when the write was attempted (<c>null</c> when none was set).</summary>
    public Guid? ExpectedTenantGuid { get; }

    /// <summary>The tenant the target entity actually belongs to (<c>null</c> when unknown).</summary>
    public Guid? ActualTenantGuid { get; }

    /// <summary>
    /// Creates the exception. The message deliberately keeps the wording the wrappers have always used, so
    /// logs and any message-matching tests stay stable; the tenant ids travel in the properties instead.
    /// </summary>
    public TenantMismatchException(
        string operation,
        string entityType,
        Guid? expectedTenantGuid = null,
        Guid? actualTenantGuid = null)
        : base($"Cannot {operation} item: it does not belong to the current tenant")
    {
        Operation = operation;
        EntityType = entityType;
        ExpectedTenantGuid = expectedTenantGuid;
        ActualTenantGuid = actualTenantGuid;
    }
}
