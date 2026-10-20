using Raven.Client.Documents;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// A RavenDB store's document store and database key without its model type (TASK-700): how composing code tells two
/// stores share one database.
/// </summary>
public interface IRavenConnectedStore
{
    /// <summary>The store's document store; null until settings are set.</summary>
    IDocumentStore? DocumentStore { get; }

    /// <summary>
    /// The key a session for this store's database is published under (<see cref="RavenAmbientKey"/>); null until settings
    /// are set.
    /// </summary>
    string? AmbientKey { get; }

    /// <summary>
    /// The session a caller gave this store (<c>SetTransactionContext</c>), if any: an <c>IAsyncDocumentSession</c> on the
    /// async store, an <c>IDocumentSession</c> on the sync one.
    /// </summary>
    object? GivenSession { get; }
}
