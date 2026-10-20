using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using Birko.Data.Stores;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.RavenDB;

/// <summary>
/// Makes a localized write one RavenDB transaction when the entity store and the translation store share one database
/// (TASK-700), as <c>SqlLocalizedWriteBoundary</c> does on SQL. Build it with <see cref="For"/> and pass it to the
/// localized wrapper or <c>EntityTranslations</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>How:</b> both stores write into one session, and nothing reaches the server until it is saved,
/// as one cluster-wide transaction (the mode <c>RavenDbUnitOfWork</c> uses). A write that throws is discarded with the
/// session.</description></item>
/// <item><description><b>"One database"</b> means both document stores have the same URLs and database
/// (<see cref="RavenAmbientKey"/>). A session of one document store carries the other store's writes, since the stores
/// write with explicit ids.</description></item>
/// <item><description><b>A caller's boundary is joined.</b> Inside a caller's <c>RavenDbUnitOfWork</c> for the same
/// database, the write goes into its session. A failure marks it rollback-only, so the caller's commit is refused
/// (<c>TransactionRollbackOnlyException</c>) and cannot save half a write. The sync wrapper takes an
/// <see cref="IDocumentSession"/> and does not join an async unit of work.</description></item>
/// <item><description><b>Otherwise the write gets its own session</b>, saved when the write returns and discarded when it
/// throws. The exception is <see cref="EntityNotFoundException"/> from a collection update (TASK-555): the stored part is
/// saved, then it is rethrown.</description></item>
/// <item><description><b>A race is retried once.</b> A key another writer saved after this write checked it surfaces when
/// the session is saved, as a <see cref="ConcurrencyException"/>. The whole write re-runs once in a new session, so
/// everything it does runs twice, as on SQL. A refusal during the write itself (<c>StoreUniqueViolationException</c>) is
/// already retried in place by the translation upsert, and a re-run cannot change a refused entity key, so it is not
/// retried here. Inside a caller's boundary there is no re-run.</description></item>
/// <item><description><b>A store given its own session</b> (<c>SetTransactionContext</c>) makes the write run in it,
/// published so the other store joins it too. The caller owns and saves it, so there is no retry and nothing to mark.
/// </description></item>
/// <item><description><b>Reads inside the session</b> that are queries answer from indexes and miss the session's unsaved
/// writes (RavenDB's property). The localized wrappers read translation rows before they write them and re-read a refused
/// key by id, so this does not change their result.</description></item>
/// <item><description><b>Build it after the stores' settings are set:</b> the database is read when <see cref="For"/>
/// runs.</description></item>
/// </list>
/// </remarks>
public sealed class RavenLocalizedWriteBoundary : ILocalizedWriteBoundary
{
    private readonly IDocumentStore _documentStore;
    private readonly string _key;
    private readonly IRavenConnectedStore _entities;
    private readonly IRavenConnectedStore _translations;

    private RavenLocalizedWriteBoundary(IDocumentStore documentStore, string key, IRavenConnectedStore entities, IRavenConnectedStore translations)
    {
        _documentStore = documentStore;
        _key = key;
        _entities = entities;
        _translations = translations;
    }

    public bool IsAtomic => true;

    /// <summary>
    /// The boundary for this pair of stores: atomic when both reach a RavenDB store on one database (through any
    /// decorators), best effort otherwise.
    /// </summary>
    /// <param name="entityStore">The entity store the localized wrapper wraps, or its decorator chain.</param>
    /// <param name="translationStore">The translation store.</param>
    /// <param name="requireAtomic">Refuse a pairing that cannot be atomic instead of returning best effort.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="requireAtomic"/> is set and the stores are not RavenDB stores on one database. A store whose
    /// settings are not set yet has no document store and counts as not RavenDB.
    /// </exception>
    public static ILocalizedWriteBoundary For(object entityStore, object translationStore, bool requireAtomic = false)
    {
        ArgumentNullException.ThrowIfNull(entityStore);
        ArgumentNullException.ThrowIfNull(translationStore);
        var entities = RavenStoreOf(entityStore);
        var translations = RavenStoreOf(translationStore);
        var key = entities?.AmbientKey;
        if (entities != null && translations != null && key != null && key == translations.AmbientKey)
        {
            return new RavenLocalizedWriteBoundary(entities.DocumentStore!, key, entities, translations);
        }
        if (requireAtomic)
        {
            throw new InvalidOperationException(
                "The entity store and the translation store are not RavenDB stores on one database "
                + $"(entity store: {Describe(entities)}; translation store: {Describe(translations)}), so their writes cannot "
                + "be one transaction.");
        }
        return BestEffortWriteBoundary.Instance;
    }

    private static IRavenConnectedStore? RavenStoreOf(object store)
        => StoreChain.Walk(store).OfType<IRavenConnectedStore>().FirstOrDefault(s => s.DocumentStore != null);

    private static string Describe(IRavenConnectedStore? store)
        => store == null ? "not a RavenDB store, or its settings are not set" : $"database {store.AmbientKey}";

    /// <summary>The session of this kind a caller gave one of the stores directly, if any.</summary>
    private TSession? GivenSession<TSession>() where TSession : class
        => _entities.GivenSession as TSession ?? _translations.GivenSession as TSession;

    public void Run(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        // A cell of this flow's own, so nothing below is published into a cell other flows share.
        using var cell = AmbientSession<IDocumentSession>.InstallCell();
        if (GivenSession<IDocumentSession>() is IDocumentSession given)
        {
            using (AmbientSession<IDocumentSession>.Enter(_key, given))
            {
                write();
            }
            return;
        }
        if (AmbientSession<IDocumentSession>.Find(_key) != null)
        {
            RunJoined<IDocumentSession>(write);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            using var session = _documentStore.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide });
            var scope = AmbientSession<IDocumentSession>.Enter(_key, session);
            try
            {
                try
                {
                    write();
                }
                catch (EntityNotFoundException)
                {
                    scope.Dispose();
                    session.SaveChanges();
                    throw;
                }
                scope.Dispose();
                session.SaveChanges();
                return;
            }
            catch (ConcurrencyException) when (attempt == 1)
            {
                // Discarded with the session; the next attempt opens a new one.
            }
            finally
            {
                scope.Dispose();
            }
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (GivenSession<IAsyncDocumentSession>() is IAsyncDocumentSession given)
        {
            using (AmbientSession<IAsyncDocumentSession>.InstallCell())
            using (AmbientSession<IAsyncDocumentSession>.Enter(_key, given))
            {
                await write(ct);
            }
            return;
        }
        if (AmbientSession<IAsyncDocumentSession>.Find(_key) != null)
        {
            await RunJoinedAsync(write, ct);
            return;
        }
        for (var attempt = 1; ; attempt++)
        {
            // Built here, in the flow that runs the write: its constructor installs the ambient cell its BeginAsync
            // publishes into.
            await using var unit = new RavenDbUnitOfWork(_documentStore);
            await unit.BeginAsync(ct);
            try
            {
                try
                {
                    await write(ct);
                }
                catch (EntityNotFoundException)
                {
                    await unit.CommitAsync(ct);
                    throw;
                }
                await unit.CommitAsync(ct);
                return;
            }
            catch (ConcurrencyException) when (attempt == 1)
            {
                // The failed commit ended the unit of work; the next attempt begins a new one.
            }
            // Any other failure: disposing the unit of work discards the session's unsaved writes.
        }
    }

    private void RunJoined<TSession>(Action write) where TSession : class
    {
        try
        {
            write();
        }
        catch (EntityNotFoundException)
        {
            throw;
        }
        catch
        {
            // The owner must not save half a write.
            AmbientSession<TSession>.MarkRollbackOnly(_key);
            throw;
        }
    }

    private async Task RunJoinedAsync(Func<CancellationToken, Task> write, CancellationToken ct)
    {
        try
        {
            await write(ct);
        }
        catch (EntityNotFoundException)
        {
            throw;
        }
        catch
        {
            AmbientSession<IAsyncDocumentSession>.MarkRollbackOnly(_key);
            throw;
        }
    }
}
