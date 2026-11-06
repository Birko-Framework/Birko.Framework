namespace Birko.Security.AspNetCore.Authorization;

/// <summary>
/// An <see cref="IUserPermissionResolver"/> that applies <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/> to
/// the host data it reads itself, so <see cref="PermissionResolutionMiddleware"/> keeps its answer as it is: a
/// <c>"*"</c> in it was granted by Birko (TASK-823: the grants layer's admin grant), not supplied by the host.
/// </summary>
public interface IAppliesWildcardOption : IUserPermissionResolver
{
}
