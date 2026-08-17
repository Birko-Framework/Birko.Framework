using System;
using Raven.Client.Documents;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// The single producer of a document's RavenDB id.
/// </summary>
/// <remarks>
/// TASK-241. Both stores used to have <b>two</b> answers for what a document's id is: the write path
/// called <c>StoreAsync(data)</c> with no id, so Raven's default convention generated one from the
/// collection (<c>TxDocs/1-A</c>), while every read, update and delete addressed
/// <c>data.Guid.ToString()</c>. Those never match, so — measured against RavenDB 7.2, with no transaction
/// anywhere in the picture — <c>ReadAsync(Guid)</c> returned null for a document that exists,
/// <c>DeleteAsync(entity)</c> deleted nothing <i>and reported success</i>, and <c>UpdateAsync(entity)</c>
/// created a duplicate instead of replacing. Same family as TASK-219, where MongoDB had two answers for
/// what <c>_id</c> is.
/// <para>
/// Every id-addressing site in both stores now routes through here, so the reader and the writer agree
/// <b>by construction</b> and a site added later is correct without being told.
/// </para>
/// <para>
/// <b>Why not a Raven id convention.</b> <c>Conventions.RegisterAsyncIdConvention&lt;T&gt;</c> would be one
/// registration at a funnel, which is normally the better shape. It cannot be used here: conventions
/// freeze on <c>DocumentStore.Initialize()</c> and Raven <i>throws</i> on any later change (measured —
/// "Conventions has frozen after 'DocumentStore.Initialize()'"), and both stores accept an
/// externally-supplied, already-initialised <c>IDocumentStore</c>. A convention-based fix would therefore
/// have had to be skipped for exactly the constructor a consumer with its own <c>DocumentStore</c> uses,
/// which is the silent half-fix this defect is made of.
/// </para>
/// <para>
/// <b>Why the collection prefix.</b> The id is <c>{collection}/{guid}</c>, not the bare guid. Two entity
/// types deliberately sharing one identity — a <c>User</c> and its <c>UserProfile</c> keyed by the same
/// <c>Guid</c> — are an ordinary modelling pattern, and a bare-guid id would make the second silently
/// overwrite the first: the same class of silent data loss this task exists to remove. The prefix costs
/// nothing and is also what Raven's own conventions produce, so ids stay navigable in the Studio.
/// </para>
/// </remarks>
public static class RavenDocumentId
{
    /// <summary>
    /// The document id for an entity of <paramref name="entityType"/> with the given canonical
    /// <paramref name="guid"/>.
    /// </summary>
    /// <remarks>
    /// The collection name comes from the store's own conventions, so the id agrees with the document's
    /// <c>@collection</c> metadata even when a consumer has customised collection naming. Correctness
    /// only requires that writes and reads agree, which one producer guarantees; matching the metadata is
    /// what keeps it legible.
    /// </remarks>
    public static string For(IDocumentStore store, Type entityType, Guid guid)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (entityType == null) throw new ArgumentNullException(nameof(entityType));

        return store.Conventions.GetCollectionName(entityType) + "/" + guid;
    }
}
