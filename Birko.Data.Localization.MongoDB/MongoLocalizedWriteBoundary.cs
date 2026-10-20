using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
using Birko.Data.MongoDB;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using Birko.Data.Stores;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.MongoDB;

/// <summary>
/// Makes a localized write one MongoDB transaction when the entity store and the translation store share one database
/// (TASK-700), as <c>SqlLocalizedWriteBoundary</c> does on SQL. Build it with <see cref="For"/> and pass it to the
/// localized wrapper or <c>EntityTranslations</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>Needs a replica set</b> (or a sharded cluster): a standalone <c>mongod</c> refuses the first
/// write in a transaction, so every write through this boundary fails there. Use best effort on a standalone server.
/// </description></item>
/// <item><description><b>"One database"</b> means both stores' clients have the same <c>Settings.GetId()</c>, the key
/// <see cref="AmbientSession{TSession}"/> is looked up by. Settings with equal values but a different id count as two
/// databases, which is safe but best effort. Each store builds its own client; a session of one client carries the
/// other's writes when both point at one deployment (measured).</description></item>
/// <item><description><b>A caller's boundary is joined.</b> Inside a caller's <c>MongoDbUnitOfWork</c> for the same
/// database, the write takes part in it. A failure marks it rollback-only, so the caller's commit is refused
/// (<c>TransactionRollbackOnlyException</c>) and cannot keep half a write.</description></item>
/// <item><description><b>Otherwise the write gets its own transaction.</b> It commits when the write returns and rolls back
/// when it throws. The exception is <see cref="EntityNotFoundException"/> from a collection update (TASK-555): the stored
/// part is committed, then it is rethrown.</description></item>
/// <item><description><b>A race is retried once.</b> Inside a transaction a racing writer is a write conflict, never a
/// duplicate key: the transaction reads a snapshot, so a key another writer took after it began, committed or not,
/// conflicts (measured). MongoDB labels it <c>TransientTransactionError</c>; the transaction is aborted and the whole write
/// re-runs once in a new one, which reads the other writer's row and updates it. Everything the write does runs twice, as
/// on SQL. A <see cref="StoreUniqueViolationException"/> is not retried: inside a transaction it means a key held before
/// it began that the write could not see, which a re-run cannot change. Inside a caller's boundary there is no re-run.
/// </description></item>
/// <item><description><b>A store given its own session</b> (<c>SetTransactionContext</c>) makes the write run in it,
/// published so the other store joins it too. The caller owns it, so there is no retry and nothing to mark.</description></item>
/// <item><description><b>Build it after <c>SetSettings</c>:</b> the database is read when <see cref="For"/> runs.</description></item>
/// </list>
/// </remarks>
public sealed class MongoLocalizedWriteBoundary : ILocalizedWriteBoundary
{
    private const string TransientLabel = "TransientTransactionError";

    private readonly MongoDBClient _client;
    private readonly string _key;
    private readonly IMongoConnectedStore _entities;
    private readonly IMongoConnectedStore _translations;

    private MongoLocalizedWriteBoundary(MongoDBClient client, string key, IMongoConnectedStore entities, IMongoConnectedStore translations)
    {
        _client = client;
        _key = key;
        _entities = entities;
        _translations = translations;
    }

    public bool IsAtomic => true;

    /// <summary>
    /// The boundary for this pair of stores: atomic when both reach a MongoDB store on one database (through any
    /// decorators), best effort otherwise.
    /// </summary>
    /// <param name="entityStore">The entity store the localized wrapper wraps, or its decorator chain.</param>
    /// <param name="translationStore">The translation store.</param>
    /// <param name="requireAtomic">Refuse a pairing that cannot be atomic instead of returning best effort.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="requireAtomic"/> is set and the stores are not MongoDB stores on one database. A store whose
    /// settings are not set yet has no client and counts as not MongoDB.
    /// </exception>
    public static ILocalizedWriteBoundary For(object entityStore, object translationStore, bool requireAtomic = false)
    {
        ArgumentNullException.ThrowIfNull(entityStore);
        ArgumentNullException.ThrowIfNull(translationStore);
        var entities = MongoStoreOf(entityStore);
        var translations = MongoStoreOf(translationStore);
        var key = entities?.AmbientKey;
        if (entities != null && translations != null && key != null && key == translations.AmbientKey)
        {
            return new MongoLocalizedWriteBoundary(entities.Client!, key, entities, translations);
        }
        if (requireAtomic)
        {
            throw new InvalidOperationException(
                "The entity store and the translation store are not MongoDB stores on one database "
                + $"(entity store: {Describe(entities)}; translation store: {Describe(translations)}), so their writes cannot "
                + "be one transaction.");
        }
        return BestEffortWriteBoundary.Instance;
    }

    private static IMongoConnectedStore? MongoStoreOf(object store)
        => StoreChain.Walk(store).OfType<IMongoConnectedStore>().FirstOrDefault(s => s.Client != null);

    private static string Describe(IMongoConnectedStore? store)
        => store == null ? "not a MongoDB store, or its settings are not set" : $"database {store.AmbientKey}";

    /// <summary>The session a caller gave one of the stores directly, if any.</summary>
    private IClientSessionHandle? ExplicitSession => _entities.TransactionContext ?? _translations.TransactionContext;

    public void Run(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        // A cell of this flow's own, so nothing below is published into a cell other flows share.
        using var cell = AmbientSession<IClientSessionHandle>.InstallCell();
        if (ExplicitSession is IClientSessionHandle given)
        {
            using (AmbientSession<IClientSessionHandle>.Enter(_key, given))
            {
                write();
            }
            return;
        }
        if (AmbientSession<IClientSessionHandle>.Find(_key) != null)
        {
            RunJoined(write);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            using var session = _client.Client.StartSession();
            session.StartTransaction();
            var scope = AmbientSession<IClientSessionHandle>.Enter(_key, session);
            try
            {
                write();
            }
            catch (EntityNotFoundException)
            {
                scope.Dispose();
                session.CommitTransaction();
                throw;
            }
            catch (Exception error) when (attempt == 1 && IsRace(error))
            {
                scope.Dispose();
                Abort(session);
                continue;
            }
            catch
            {
                scope.Dispose();
                Abort(session);
                throw;
            }
            scope.Dispose();
            session.CommitTransaction();
            return;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (ExplicitSession is IClientSessionHandle given)
        {
            using (AmbientSession<IClientSessionHandle>.InstallCell())
            using (AmbientSession<IClientSessionHandle>.Enter(_key, given))
            {
                await write(ct);
            }
            return;
        }
        if (AmbientSession<IClientSessionHandle>.Find(_key) != null)
        {
            await RunJoinedAsync(write, ct);
            return;
        }
        for (var attempt = 1; ; attempt++)
        {
            // Built here, in the flow that runs the write: its constructor installs the ambient cell its BeginAsync
            // publishes into.
            await using var unit = new MongoDbUnitOfWork(_client);
            await unit.BeginAsync(ct);
            try
            {
                await write(ct);
            }
            catch (EntityNotFoundException)
            {
                await unit.CommitAsync(ct);
                throw;
            }
            catch (Exception error) when (attempt == 1 && IsRace(error))
            {
                await RollBackAsync(unit);
                continue;
            }
            catch
            {
                await RollBackAsync(unit);
                throw;
            }
            await unit.CommitAsync(ct);
            return;
        }
    }

    private void RunJoined(Action write)
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
            // The owner must not commit half a write.
            AmbientSession<IClientSessionHandle>.MarkRollbackOnly(_key);
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
            AmbientSession<IClientSessionHandle>.MarkRollbackOnly(_key);
            throw;
        }
    }

    /// <summary>
    /// A write conflict with another writer, which MongoDB labels transient. The store does not translate it, so the
    /// driver's label is read on the chain.
    /// </summary>
    private static bool IsRace(Exception error)
    {
        for (Exception? e = error; e != null; e = e.InnerException)
        {
            if (e is MongoException mongo && mongo.HasErrorLabel(TransientLabel))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Aborts on a path that is already throwing; a failed abort must not replace that exception.</summary>
    private static void Abort(IClientSessionHandle session)
    {
        try
        {
            if (session.IsInTransaction)
            {
                session.AbortTransaction();
            }
        }
        catch (Exception)
        {
            // Disposing the session aborts the transaction; the caller sees the write's exception.
        }
    }

    /// <inheritdoc cref="Abort"/>
    private static async Task RollBackAsync(MongoDbUnitOfWork unit)
    {
        if (!unit.IsActive)
        {
            return;
        }
        try
        {
            await unit.RollbackAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Disposing the unit of work releases the session; the caller sees the write's exception.
        }
    }
}
