namespace Birko.Data.Patterns.Models;

/// <summary>
/// Opts an <see cref="Birko.Data.Models.IDefault"/> model into storage-enforced "at most one default".
/// </summary>
/// <remarks>
/// TASK-632. The default decorators read the defaults, unset them and then write their own, so two callers promoting two
/// rows at once could both store a default (SH-M328): a read-then-write narrows that window and cannot close it.
/// <para>
/// <see cref="DefaultMarker"/> is <c>true</c> on the default row and <c>null</c> on every other, never <c>false</c>; the
/// default decorators keep it in step with <c>IsDefault</c>. Declare a UNIQUE index over it (with the tenant column
/// first, where the model is tenant-scoped) and the store refuses a second default. On SQL declare it with
/// <c>WhereNotNull = DefaultMarker</c>: SQL Server treats NULLs as equal for uniqueness, and MySQL, which has no
/// partial index, drops that term because NULLs are distinct there (rule 58). On MongoDB a unique index with
/// <c>partialFilterExpression: { DefaultMarker: true }</c>, created directly: a sparse one refuses the second
/// non-default, because the null is stored (measured, code 11000), and Birko's index manager declares no partial filter.
/// </para>
/// <para>
/// The decorator turns the refusal into a retry: it unsets the other default and writes again, so the last writer wins.
/// A store that cannot enforce a unique index (ElasticSearch, RavenDB, Cosmos DB, JSON, XML, InMemory) leaves the race
/// narrowed, not closed.
/// </para>
/// </remarks>
public interface IDefaultMarker : Birko.Data.Models.IDefault
{
    /// <summary><c>true</c> on the default row, <c>null</c> on every other. Never <c>false</c>.</summary>
    bool? DefaultMarker { get; set; }
}
