using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.SQL;

/// <summary>
/// Makes a localized write one transaction when the entity store and the translation store share one SQL database
/// (TASK-683). Build it with <see cref="For"/> and pass it to the localized wrapper or <c>EntityTranslations</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>"One database"</b> means both stores' connectors have the same <c>Settings.GetId()</c>, the key
/// <see cref="AmbientSqlTransaction"/> is looked up by. Settings with equal values but a different id count as two
/// databases, which is safe but best effort.</description></item>
/// <item><description><b>A caller's boundary is joined.</b> Inside a caller's <c>SqlUnitOfWork</c> or a store's bulk
/// transaction for the same database, the write takes part in it. A failure marks it rollback-only, so the caller cannot
/// commit half a write. Unless the write is one a decorator above will retry on a refusal (TASK-697): then it runs in a
/// savepoint, a failure is rolled back to it, and the caller's transaction is left unmarked, so the retry that follows
/// can commit.</description></item>
/// <item><description><b>Otherwise the write gets its own transaction.</b> It commits when the write returns and rolls back
/// when the write throws. The exception is <see cref="EntityNotFoundException"/> from a collection update (TASK-555): the
/// stored part is committed, then it is rethrown, as a SQL store's own bulk transaction does.</description></item>
/// <item><description><b>A unique-key race is retried once</b> (decided by the user). When the write's own transaction
/// fails with <see cref="StoreUniqueViolationException"/>, the transaction is rolled back and the whole write re-runs once
/// in a new one. A filter update's action runs again too. The second pass reads the row the other writer stored and
/// updates it. Everything the write does runs twice: a <c>StoreDataDelegate</c>, a filter update's action, and changes
/// to the caller's objects (a create's assigned <c>Guid</c> stays set). Since TASK-694 the translation upsert retries a
/// race in place even on PostgreSQL (its create runs in a savepoint), so this re-run is what still covers a race on the
/// entity write. Inside a caller's boundary there is no re-run: the upsert's in-place retry is all there is, and on MySQL
/// the typed error reaches the caller.</description></item>
/// <item><description><b>A store given its own transaction</b> (<c>SetTransactionContext</c>) makes the write run in that
/// transaction, published so the other store joins it too. The caller owns it, so there is no retry and nothing to mark.
/// </description></item>
/// <item><description><b>Build it after <c>SetSettings</c>:</b> the database is read when <see cref="For"/> runs. A store
/// pointed at another database later needs a new boundary.</description></item>
/// <item><description><b>Rule 6:</b> a decorator in either chain that opens its own connection is outside the
/// transaction. The boundary is only as wide as that decorator.</description></item>
/// </list>
/// </remarks>
public sealed class SqlLocalizedWriteBoundary : ILocalizedWriteBoundary
{
    private readonly AbstractConnector _connector;
    private readonly ISqlConnectedStore _entities;
    private readonly ISqlConnectedStore _translations;

    private SqlLocalizedWriteBoundary(AbstractConnector connector, ISqlConnectedStore entities, ISqlConnectedStore translations)
    {
        _connector = connector;
        _entities = entities;
        _translations = translations;
    }

    public bool IsAtomic => true;

    /// <summary>
    /// The boundary for this pair of stores: atomic when both reach a SQL store on one database (through any decorators),
    /// best effort otherwise.
    /// </summary>
    /// <param name="entityStore">The entity store the localized wrapper wraps, or its decorator chain.</param>
    /// <param name="translationStore">The translation store.</param>
    /// <param name="requireAtomic">Refuse a pairing that cannot be atomic instead of returning best effort.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="requireAtomic"/> is set and the stores are not SQL stores on one database. A store whose settings
    /// are not set yet has no connector and counts as not SQL.
    /// </exception>
    public static ILocalizedWriteBoundary For(object entityStore, object translationStore, bool requireAtomic = false)
    {
        ArgumentNullException.ThrowIfNull(entityStore);
        ArgumentNullException.ThrowIfNull(translationStore);
        var entityLink = SqlStoreOf(entityStore);
        var translationLink = SqlStoreOf(translationStore);
        var entities = entityLink?.Connector;
        var translations = translationLink?.Connector;
        if (entities != null && translations != null && entities.Settings.GetId() == translations.Settings.GetId())
        {
            return new SqlLocalizedWriteBoundary(entities, entityLink!, translationLink!);
        }
        if (requireAtomic)
        {
            throw new InvalidOperationException(
                "The entity store and the translation store are not SQL stores on one database "
                + $"(entity store: {Describe(entities)}; translation store: {Describe(translations)}), so their writes cannot be "
                + "one transaction.");
        }
        return BestEffortWriteBoundary.Instance;
    }

    private static ISqlConnectedStore? SqlStoreOf(object store)
        => StoreChain.Walk(store).OfType<ISqlConnectedStore>().FirstOrDefault(s => s.Connector != null);

    /// <summary>The transaction a caller gave one of the stores directly, if any.</summary>
    private SqlTransactionContext? ExplicitContext => _entities.TransactionContext ?? _translations.TransactionContext;

    private static string Describe(AbstractConnector? connector)
        => connector == null ? "not a SQL store, or its settings are not set" : $"database {connector.Settings.GetId()}";

    public void Run(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var settingsId = _connector.Settings.GetId();
        if (ExplicitContext is SqlTransactionContext given)
        {
            using (AmbientSqlTransaction.Enter(settingsId, given.Connection, given.Transaction))
            {
                write();
            }
            return;
        }
        var enclosing = AmbientSqlTransaction.Find(settingsId);
        if (enclosing != null)
        {
            RunJoined(enclosing, write);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            // A cell of this flow's own, so the boundary below is not published into a cell other flows share.
            using var cell = AmbientSqlTransaction.InstallCell();
            using var connection = _connector.CreateConnection(_connector.Settings);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var scope = AmbientSqlTransaction.Enter(settingsId, connection, transaction);
            try
            {
                write();
            }
            catch (EntityNotFoundException)
            {
                scope.Dispose();
                Commit(transaction);
                throw;
            }
            catch (StoreUniqueViolationException) when (attempt == 1)
            {
                scope.Dispose();
                RollBack(transaction);
                continue;
            }
            catch
            {
                scope.Dispose();
                RollBack(transaction);
                throw;
            }
            scope.Dispose();
            Commit(transaction);
            return;
        }
    }

    public async Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (ExplicitContext is SqlTransactionContext given)
        {
            using (AmbientSqlTransaction.Enter(_connector.Settings.GetId(), given.Connection, given.Transaction))
            {
                await write(ct);
            }
            return;
        }
        var enclosing = AmbientSqlTransaction.Find(_connector.Settings.GetId());
        if (enclosing != null)
        {
            await RunJoinedAsync(enclosing, write, ct);
            return;
        }
        for (var attempt = 1; ; attempt++)
        {
            // Built here, in the flow that runs the write: its constructor installs the ambient cell (see SqlUnitOfWork).
            await using var unit = new SqlUnitOfWork(_connector, _connector.Settings);
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
            catch (StoreUniqueViolationException) when (attempt == 1)
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

    private static void RunJoined(AmbientSqlTransaction.Entry enclosing, Action write)
    {
        // TASK-697: a write a caller declared retried runs in a savepoint, so a failure is undone and leaves nothing to
        // poison. Under a retrying store's own savepoint (PostgreSQL) this one nests inside it.
        if (RetryableWrite.IsDeclared)
        {
            RunInSavepoint(enclosing, write);
            return;
        }
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
            // As a participating SqlUnitOfWork does: the owner must not commit half a write.
            enclosing.MarkRollbackOnly();
            throw;
        }
    }

    /// <inheritdoc cref="RunJoined"/>
    private static async Task RunJoinedAsync(AmbientSqlTransaction.Entry enclosing, Func<CancellationToken, Task> write, CancellationToken ct)
    {
        if (RetryableWrite.IsDeclared)
        {
            await RunInSavepointAsync(enclosing, write, ct);
            return;
        }
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
            // As a participating SqlUnitOfWork does: the owner must not commit half a write.
            enclosing.MarkRollbackOnly();
            throw;
        }
    }

    /// <summary>
    /// TASK-697 / TASK-705: a declared-retried joined write in a savepoint. A failure is rolled back to it and leaves the
    /// unit unmarked, unless the rollback itself failed: then the write may still be in the transaction, and the unit is
    /// marked. (An <see cref="EntityNotFoundException"/> keeps what it stored; <c>SqlSavepoint</c> releases on it.)
    /// </summary>
    private static void RunInSavepoint(AmbientSqlTransaction.Entry enclosing, Action write)
    {
        try
        {
            SqlSavepoint.Run(enclosing.Transaction, write);
        }
        catch (Exception error) when (error.Data.Contains(SqlSavepoint.RollbackFailureKey))
        {
            enclosing.MarkRollbackOnly();
            throw;
        }
    }

    /// <inheritdoc cref="RunInSavepoint"/>
    private static async Task RunInSavepointAsync(AmbientSqlTransaction.Entry enclosing, Func<CancellationToken, Task> write, CancellationToken ct)
    {
        try
        {
            await SqlSavepoint.RunAsync(enclosing.Transaction, () => write(ct), ct);
        }
        catch (Exception error) when (error.Data.Contains(SqlSavepoint.RollbackFailureKey))
        {
            enclosing.MarkRollbackOnly();
            throw;
        }
    }

    private void Commit(DbTransaction transaction)
    {
        try
        {
            transaction.Commit();
        }
        catch
        {
            AmbientSqlTransaction.DiscardCommitted(transaction);
            throw;
        }
        AmbientSqlTransaction.RunCommitted(transaction, error => _connector.RecordSubscriberFailure("AfterCommit", error));
    }

    /// <summary>Rolls back on a path that is already throwing; a failed rollback must not replace that exception.</summary>
    private static void RollBack(DbTransaction transaction)
    {
        AmbientSqlTransaction.DiscardCommitted(transaction);
        try
        {
            transaction.Rollback();
        }
        catch (Exception)
        {
            // Disposing the transaction rolls it back; the caller sees the write's exception.
        }
    }

    /// <inheritdoc cref="RollBack"/>
    private static async Task RollBackAsync(SqlUnitOfWork unit)
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
            // Disposing the unit of work releases the transaction; the caller sees the write's exception.
        }
    }
}
