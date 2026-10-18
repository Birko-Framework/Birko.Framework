using System;

namespace Birko.Communication.AspNetCore
{
    /// <summary>
    /// The one owner-or-admin access rule (TASK-541): a caller who sees everything may access any resource; otherwise the
    /// caller and the owner must both be present and equal. A denied caller is answered 404, never 403 — owned data is
    /// private, so a non-owner may not read it, and a denial must not reveal that it exists (rulebook rule 65).
    /// <see cref="OwnedCrudResults"/> decides through it, as should any hand-written owner check.
    /// </summary>
    /// <remarks>
    /// Identities are passed as values, not as an <c>ICurrentUser</c>, so this project keeps no dependency on
    /// Birko.Security.AspNetCore: pass <c>user.UserGuid</c> and, for <paramref name="seesEverything"/>, the permission check
    /// the app uses (e.g. <c>user.Permissions.Contains("*")</c>).
    /// </remarks>
    public static class OwnerAccess
    {
        /// <summary>
        /// Whether <paramref name="caller"/> may access a resource owned by <paramref name="owner"/>. A null caller or a
        /// null owner never matches, except under <paramref name="seesEverything"/>.
        /// </summary>
        public static bool CanAccess(Guid? caller, Guid? owner, bool seesEverything = false)
            => seesEverything || (caller is { } c && owner is { } o && c == o);

        /// <summary>
        /// String-owner form (e.g. <c>github:42</c>). A null or empty caller or owner never matches, except under
        /// <paramref name="seesEverything"/>; the comparison is ordinal.
        /// </summary>
        public static bool CanAccess(string? caller, string? owner, bool seesEverything = false)
            => seesEverything || (!string.IsNullOrEmpty(caller) && !string.IsNullOrEmpty(owner) && string.Equals(caller, owner, StringComparison.Ordinal));
    }
}
