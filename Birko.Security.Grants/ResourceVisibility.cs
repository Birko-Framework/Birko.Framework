namespace Birko.Security.Grants;

/// <summary>
/// Who may learn that a resource of a given type exists. Read by the HTTP layer to choose the denial code
/// (STORY-062 decision 2b): a caller who may read the resource gets 403 on a denied action; a caller who may
/// not read it gets 404, the same answer as for a resource that does not exist.
/// </summary>
/// <remarks>
/// Visibility never changes what <see cref="Services.IGrantService.CanAsync"/> returns. <see cref="Public"/>
/// does not make a "read" check true — public reads are anonymous endpoints that never ask.
/// </remarks>
public enum ResourceVisibility
{
    /// <summary>Default. Only callers holding a grant that allows reading may learn the resource exists.</summary>
    Granted = 0,

    /// <summary>Anyone may read the resource, so its existence is no secret.</summary>
    Public = 1,
}
