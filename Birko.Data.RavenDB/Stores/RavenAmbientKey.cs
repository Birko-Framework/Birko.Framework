using Raven.Client.Documents;
using System;
using System.Linq;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// The one producer of the key a RavenDB session is published under for a database (TASK-700, rule 16): the stores read
/// it, the unit of work and the localized write boundary publish under it.
/// </summary>
/// <remarks>
/// It is built from the document store, not from settings, because a <c>RavenDbUnitOfWork</c> and a store built from a
/// connection string have no settings. Two document stores on the same URLs and database share the key: a session of one
/// carries the other's writes, since the stores write through the session with explicit ids.
/// </remarks>
public static class RavenAmbientKey
{
    /// <summary>The key for <paramref name="documentStore"/>'s database; null for no document store.</summary>
    public static string? Of(IDocumentStore? documentStore)
    {
        if (documentStore == null)
        {
            return null;
        }
        var urls = string.Join(",", (documentStore.Urls ?? Array.Empty<string>()).Select(u => u.TrimEnd('/').ToLowerInvariant()).OrderBy(u => u, StringComparer.Ordinal));
        return $"raven|{urls}|{documentStore.Database}";
    }
}
