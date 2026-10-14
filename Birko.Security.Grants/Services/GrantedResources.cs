using System;
using System.Collections.Generic;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Services;

/// <summary>
/// What a user may reach of one resource type. <see cref="AllOfType"/> is true for an admin or a holder of a type-wide
/// grant, and then <see cref="Ids"/> lists only the exact grants — never read "no ids" as "nothing".
/// </summary>
public sealed record GrantedResources(bool AllOfType, IReadOnlyList<Guid> Ids);

/// <summary>One grant applying to a resource; <see cref="IsTypeWide"/> when it covers every resource of the type.</summary>
public sealed record GrantEntry(ResourceGrant Grant, bool IsTypeWide);
