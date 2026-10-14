using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Grants.Services;

/// <summary>
/// Answers "is this user an admin everywhere?" for <see cref="IGrantService"/>. An admin passes every check and every
/// grant rule. The core default is <see cref="BootstrapAdminGrantSource"/>; a host can compose others (an ASP.NET host
/// adds the <c>"*"</c> permission from its permission resolver).
/// </summary>
public interface IGrantAdminSource
{
    Task<bool> IsAdminAsync(Guid userGuid, Guid? tenantGuid, CancellationToken ct = default);
}
