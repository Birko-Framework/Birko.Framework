using System;
using System.Linq.Expressions;

namespace Birko.Data.Expressions;

/// <summary>
/// The single producer for "refuse a destructive write whose filter covers every row".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists (SH-H002 / TASK-329).</b> The rule was implemented twice — once in
/// <c>AbstractBulkStore</c> and once in <c>AbstractAsyncBulkStore</c>, differing only in which
/// all-rows door the refusal names — and the SQL bulk stores, which do <b>not</b> derive from either
/// (they implement <c>IAsyncBulkStore&lt;T&gt;</c> / <c>IBulkStore&lt;T&gt;</c> directly), had neither.
/// Measured on SQLite before this was extracted: <c>Update(x =&gt; !empty.Contains(x.Name), …)</c>
/// rewrote <b>3 of 3</b> rows with <c>thrown=NONE</c>, while the same store's
/// <c>Delete(filter)</c> and <c>Update(filter, PropertyUpdate)</c> refused it — a store whose delete
/// guards beside an update that does not.
/// </para>
/// <para>
/// Adding a third and fourth copy in the SQL layer was the obvious fix and is the shape
/// § Conventions keeps recording as the cause: <i>a rule with one statement and several
/// implementations is one that will be got wrong again</i>. So the rule lives here once and each caller
/// supplies only the thing that genuinely differs — the name of the door it offers instead.
/// </para>
/// <para>
/// It lives in <c>Birko.Data.Core</c> beside <see cref="PredicateScope"/> (which it consults) and
/// <c>WholeTableWriteException</c> (which it throws), so every backend and every store hierarchy can
/// reach it without a new dependency.
/// </para>
/// </remarks>
public static class BoundedFilterGuard
{
    /// <summary>
    /// Throws <c>WholeTableWriteException</c> when <paramref name="filter"/> is not null and
    /// <b>reduces</b> to every row, unless it is the explicit all-rows constant.
    /// </summary>
    /// <param name="filter">
    /// The caller's filter. <b>Null is ignored here</b> — a missing filter is the caller's own
    /// <c>RequireFilter</c>-style check, and duplicating it would produce two different messages for
    /// one mistake.
    /// </param>
    /// <param name="operation">
    /// <c>"update"</c> or <c>"delete"</c> — used in the message and to pick nothing else; the door name
    /// is passed explicitly rather than derived, because only the caller knows which API it has.
    /// </param>
    /// <param name="entityName">The entity type's name, for the message.</param>
    /// <param name="allRowsDoor">
    /// The all-rows API <b>this caller actually offers</b>, e.g. <c>"DeleteAllAsync()"</c> or
    /// <c>"UpdateAll(updates)"</c>. ⚠ Per § SH-H037 / TASK-215 a refusal must name a door that
    /// exists for the caller it is refusing: an async store has no <c>DeleteAll()</c>, and pointing at
    /// one would be an opt-out that does not compile.
    /// </param>
    public static void Require(LambdaExpression? filter, string operation, string entityName, string allRowsDoor)
    {
        if (filter == null)
        {
            return;
        }

        // x => true is the documented all-rows synonym, not a reduction to it — see PredicateScope.
        if (PredicateScope.IsExplicitAllRows(filter))
        {
            return;
        }

        if (!PredicateScope.ReducesToAllRows(filter))
        {
            return;
        }

        throw new Data.Exceptions.WholeTableWriteException(
            operation, entityName, "every stored entity of that type", allRowsDoor);
    }
}
