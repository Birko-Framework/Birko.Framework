using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.Services;

/// <summary>
/// An admin is a user holding the reserved grant <c>{ResourceType = "*", ResourceId = null, Role = "*"}</c> in tenant scope.
/// </summary>
public sealed class BootstrapAdminGrantSource : IGrantAdminSource
{
    private readonly IResourceGrantStore _store;

    public BootstrapAdminGrantSource(IResourceGrantStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<bool> IsAdminAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default)
        => _store.HasAdminGrantAsync(userGuid, tenantGuid, ct);
}
