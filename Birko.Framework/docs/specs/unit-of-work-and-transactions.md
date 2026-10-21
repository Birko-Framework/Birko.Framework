---
area: unit-of-work-and-transactions
generated-at: 50828c68d6f35ca2096600381c2d0c4481132330
generated-on: 2026-10-21
sources:
  - ../Birko.Data.CosmosDB/UnitOfWork/CosmosDbUnitOfWork.cs
  - ../Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs
  - ../Birko.Data.InfluxDB/UnitOfWork/InfluxDbUnitOfWork.cs
  - ../Birko.Data.MongoDB/UnitOfWork/MongoDbUnitOfWork.cs
  - ../Birko.Data.Patterns/UnitOfWork/ITransactionCapabilities.cs
  - ../Birko.Data.Patterns/UnitOfWork/IUnitOfWork.cs
  - ../Birko.Data.Patterns/UnitOfWork/UnitOfWorkException.cs
  - ../Birko.Data.RavenDB/UnitOfWork/RavenDbUnitOfWork.cs
  - ../Birko.Data.SQL/SQL/Connectors/AmbientSqlTransaction.cs
  - ../Birko.Data.SQL/Stores/ISqlConnectedStore.cs
  - ../Birko.Data.SQL/Stores/SqlRetryableWrite.cs
  - ../Birko.Data.SQL/Stores/SqlSavepoint.cs
  - ../Birko.Data.SQL/Stores/SqlTransactionContext.cs
  - ../Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs
  - ../Birko.Data.Stores/AmbientSession.cs
  - ../Birko.Data.Stores/ITransactionalStore.cs
  - ../Birko.Data.Stores/RetryableWrite.cs
# source-commits: omitted — every source is inside this repo, so `generated-at` covers them (regen step 5c).
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-21 (TASK-696) over 376 feature-linked tasks: 117 resolved, 259 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 1 commit leads with the id of a task the state gate refuses. Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 259
---
# Unit of work and transactional store boundaries

## Purpose

This capability lets a caller group several store operations into one transactional boundary. A
`IUnitOfWork` owns the boundary (`BeginAsync` / `CommitAsync` / `RollbackAsync`) and, in its generic
form, hands out a backend-specific *transaction context* that individual stores enlist in via
`ITransactionalStore.SetTransactionContext`. Six backends implement the boundary, and they are not
equivalent: `SqlUnitOfWork` wraps a real ADO.NET `DbTransaction` on a dedicated connection, so it is
genuinely atomic; `ElasticSearchUnitOfWork` merely buffers operations in memory and flushes them as
one Elasticsearch Bulk call on commit, which is neither atomic nor isolated. `MongoDbUnitOfWork`,
`RavenDbUnitOfWork`, `CosmosDbUnitOfWork` and `InfluxDbUnitOfWork` sit between those two shapes, and every
unit of work states which shape it is through `IUnitOfWork.Capabilities`. Anything that needs
"these writes land together" depends on this capability, and needs to know which of those shapes
it is actually getting. The SQL, MongoDB and RavenDB units of work also publish their transaction for the
current async flow (`AmbientSqlTransaction`, `AmbientSession<TSession>`), so stores on the same database
join it without being handed the context, and a write a caller means to retry can run through
`RetryableWrite` so that its refusal leaves the caller's transaction usable.

## Requirements

### Requirement: Unit-of-work lifecycle surface

The system SHALL expose the transactional boundary as `IUnitOfWork`, which extends both
`IAsyncDisposable` and `IDisposable`, and SHALL expose the boundary state as a read-only `bool
IsActive` property and the boundary's guarantees as a read-only `ITransactionCapabilities Capabilities`
property, alongside the three lifecycle methods `BeginAsync(CancellationToken)`,
`CommitAsync(CancellationToken)` and `RollbackAsync(CancellationToken)`, each returning `Task` and
each defaulting its cancellation token.

#### Scenario: Caller can await disposal or dispose synchronously

- **Given** a variable of static type `IUnitOfWork`
- **When** the caller writes `await using` or a plain `using` around it
- **Then** both compile, because `IUnitOfWork` inherits `IAsyncDisposable` and `IDisposable`

#### Scenario: Lifecycle methods take an optional cancellation token

- **Given** an `IUnitOfWork`
- **When** the caller invokes `BeginAsync()`, `CommitAsync()` or `RollbackAsync()` with no arguments
- **Then** each call compiles and the token defaults to `default(CancellationToken)`

### Requirement: Typed transaction context exposure

The system SHALL provide `IUnitOfWork<out TContext> : IUnitOfWork` exposing a covariant
`TContext? Context` getter, whose value is the platform-specific transaction handle while a
transaction is active and `null` when none is.

#### Scenario: Context is null before the first begin

- **Given** a freshly constructed `SqlUnitOfWork` or `ElasticSearchUnitOfWork`
- **When** the caller reads `Context`
- **Then** it is `null`, and `IsActive` is `false`

#### Scenario: Context is populated by begin and cleared by commit

- **Given** a `SqlUnitOfWork` on which `BeginAsync()` has completed
- **When** the caller reads `Context`, then calls `CommitAsync()` and reads `Context` again
- **Then** the first read yields a non-null `SqlTransactionContext` and the second yields `null`

### Requirement: Begin rejects a second concurrent transaction

The system SHALL throw `TransactionAlreadyActiveException` from `BeginAsync` when `IsActive` is
already `true`, with the message `"A transaction is already active. Commit or rollback before
starting a new one."`, and SHALL NOT replace or nest the existing transaction.

#### Scenario: Second BeginAsync on an active SQL unit of work

- **Given** a `SqlUnitOfWork` whose `BeginAsync()` has completed so `_transaction` is non-null
- **When** `BeginAsync()` is called again
- **Then** `TransactionAlreadyActiveException` (a `UnitOfWorkException`) is thrown before any new
  connection is created, and the original `Context` is unchanged

#### Scenario: Second BeginAsync on an active Elasticsearch unit of work

- **Given** an `ElasticSearchUnitOfWork` whose `BeginAsync()` has completed so `_context` is non-null
- **When** `BeginAsync()` is called again
- **Then** `TransactionAlreadyActiveException` is thrown and the existing `BulkOperationContext` —
  including any already-buffered operations — is retained

### Requirement: Commit and rollback require an active transaction

The system SHALL throw `NoActiveTransactionException` from `CommitAsync` and from `RollbackAsync`
when `IsActive` is `false`, with the message `"No active transaction. Call BeginAsync() first."` —
except that on `SqlUnitOfWork` and `ElasticSearchUnitOfWork` the **first** `RollbackAsync` after a
`CommitAsync` that threw returns without effect, so a catch-and-roll-back keeps the commit's exception
(TASK-325).

#### Scenario: Commit without begin

- **Given** a newly constructed `SqlUnitOfWork`
- **When** `CommitAsync()` is called
- **Then** `NoActiveTransactionException` is thrown and no database work is attempted

#### Scenario: Rollback twice

- **Given** an `ElasticSearchUnitOfWork` on which `RollbackAsync()` has already completed, clearing
  `_context`
- **When** `RollbackAsync()` is called a second time
- **Then** `NoActiveTransactionException` is thrown

### Requirement: Disposed unit of work rejects all lifecycle calls

The system SHALL guard `BeginAsync`, `CommitAsync` and `RollbackAsync` on both implementations with
`ObjectDisposedException.ThrowIf(_disposed, this)`, so that any lifecycle call after `Dispose()` or
`DisposeAsync()` throws `ObjectDisposedException` — and the disposed check SHALL run before the
active/inactive check.

#### Scenario: Begin after dispose

- **Given** a `SqlUnitOfWork` on which `Dispose()` has been called
- **When** `BeginAsync()` is called
- **Then** `ObjectDisposedException` is thrown, not `TransactionAlreadyActiveException`

#### Scenario: Commit after dispose on an Elasticsearch unit of work

- **Given** an `ElasticSearchUnitOfWork` that has been disposed while inactive
- **When** `CommitAsync()` is called
- **Then** `ObjectDisposedException` is thrown rather than `NoActiveTransactionException`

### Requirement: A unit of work is reusable after commit or rollback

The system SHALL NOT set the disposed flag during commit, rollback or their cleanup, so a unit of
work that has been committed or rolled back returns to the inactive state and MAY begin a new
transaction.

#### Scenario: Begin, commit, begin again on SQL

- **Given** a `SqlUnitOfWork` that has completed `BeginAsync()` then `CommitAsync()` (whose
  `CleanupAsync` nulls `Context`, `_transaction` and `_connection` but not `_disposed`)
- **When** `BeginAsync()` is called again
- **Then** a fresh connection is created and opened, a new `DbTransaction` is begun, and a new
  `SqlTransactionContext` is published on `Context`

#### Scenario: Rollback then begin again on Elasticsearch

- **Given** an `ElasticSearchUnitOfWork` that has buffered two operations and then rolled back
- **When** `BeginAsync()` is called again and `Context` is inspected
- **Then** a brand-new empty `BulkOperationContext` is published, with no operations carried over

### Requirement: Unit-of-work exception hierarchy

The system SHALL root all unit-of-work failures at `UnitOfWorkException : Exception`, offering a
message-only and a message-plus-inner-exception constructor, and SHALL derive
`NoActiveTransactionException`, `TransactionAlreadyActiveException` and
`TransactionRollbackOnlyException` from it, each supplying its own fixed message via a parameterless
constructor.

#### Scenario: Catching the base type catches the state errors

- **Given** a `catch (UnitOfWorkException)` block around a `CommitAsync()` call
- **When** the unit of work has no active transaction
- **Then** the thrown `NoActiveTransactionException` is caught by that block

#### Scenario: State exceptions carry no caller-supplied message

- **Given** `new NoActiveTransactionException()` and `new TransactionAlreadyActiveException()`
- **When** their `Message` values are read
- **Then** they are exactly `"No active transaction. Call BeginAsync() first."` and `"A transaction
  is already active. Commit or rollback before starting a new one."` — neither type exposes a
  constructor overload that lets the caller customise the message

#### Scenario: Rollback-only exception names the remedy

- **Given** `new TransactionRollbackOnlyException()`
- **When** its `Message` is read
- **Then** it is exactly `"The transaction was marked rollback-only by a nested unit of work and cannot be
  committed. Call RollbackAsync() instead."`

### Requirement: Transactional store enlistment contract

The system SHALL define `ITransactionalStore<T, TContext> : IStore<T>` (and the mirror
`IAsyncTransactionalStore<T, TContext> : IAsyncStore<T>`), constrained to `T : Models.AbstractModel`,
each exposing a read-only `TContext? TransactionContext` and a `void SetTransactionContext(TContext?
context)` by which an external transaction context is attached; passing `null` SHALL be the documented
way to detach and return the store to per-operation transaction mode.

#### Scenario: Enlisting a SQL store in a unit of work

- **Given** a store implementing `IAsyncTransactionalStore<MyModel, SqlTransactionContext>` and a
  `SqlUnitOfWork` with an active transaction
- **When** the caller passes `unitOfWork.Context` into `store.SetTransactionContext(...)`
- **Then** the store's `TransactionContext` reports that same context, signalling that subsequent
  CRUD must run on the shared connection and transaction rather than opening its own

#### Scenario: Detaching by passing null

- **Given** a store whose `TransactionContext` is non-null
- **When** `SetTransactionContext(null)` is called
- **Then** `TransactionContext` becomes `null` and the store is back in per-operation transaction mode

#### Scenario: Bulk transactional stores are the intersection of the two contracts

- **Given** the marker interfaces `ITransactionalBulkStore<T, TContext>` and
  `IAsyncTransactionalBulkStore<T, TContext>`
- **When** a store implements either
- **Then** it must satisfy both the bulk contract (`IBulkStore<T>` / `IAsyncBulkStore<T>`) and the
  transactional contract, since the bulk interfaces add no members of their own

### Requirement: SQL transaction context is an unvalidated immutable pair

The system SHALL represent the SQL transaction handle as `sealed class SqlTransactionContext` in
namespace `Birko.Data.SQL.Stores`, exposing get-only `DbConnection Connection` and
`DbTransaction Transaction`, assigned directly from constructor arguments with no null validation.

#### Scenario: Context surfaces the exact connection and transaction it was given

- **Given** an open `DbConnection` and a `DbTransaction` begun on it
- **When** `new SqlTransactionContext(connection, transaction)` is constructed
- **Then** `Connection` and `Transaction` return those same instances, and no setter exists to
  replace either afterwards

#### Scenario: Null arguments are accepted

- **Given** a call `new SqlTransactionContext(null!, null!)`
- **When** the constructor runs
- **Then** it completes without throwing, producing a context whose `Connection` and `Transaction`
  are both null

### Requirement: SQL unit of work owns a dedicated connection per transaction

The system SHALL, on `SqlUnitOfWork.BeginAsync` when no enclosing boundary for the same settings id is
found, create a connection via `_connector.CreateConnection(_settings)`, open it with `OpenAsync(ct)`,
begin a transaction with `BeginTransactionAsync(ct)`, and publish both on a new `SqlTransactionContext`;
`IsActive` SHALL be derived from whether that `DbTransaction` field is non-null or the unit of work has
joined an enclosing boundary.

#### Scenario: Begin produces an open connection carrying a live transaction

- **Given** a `SqlUnitOfWork` constructed with a SQL connector and `PasswordSettings`
- **When** `BeginAsync()` completes
- **Then** `IsActive` is `true` and `Context.Connection` / `Context.Transaction` are the connection
  just opened and the transaction just begun on it

#### Scenario: The connector is used purely as a connection factory

- **Given** a `SqlUnitOfWork` built with any `AbstractConnectorBase` (PostgreSQL, MSSql, MySQL or
  SQLite)
- **When** `BeginAsync()` runs
- **Then** the only connector member invoked is `CreateConnection(settings)` (the settings id is read
  from the settings via `GetId()`); the transaction
  lifecycle itself goes through the ADO.NET `DbConnection` / `DbTransaction` API, so the behaviour is
  provider-independent

### Requirement: SQL commit and rollback release the connection

The system SHALL, on a successful `SqlUnitOfWork.CommitAsync` or `RollbackAsync`, call the
corresponding `DbTransaction` method and then run `CleanupAsync`, which nulls `Context`, disposes and
nulls the transaction, and closes, disposes and nulls the connection.

#### Scenario: Commit closes the connection

- **Given** an active `SqlUnitOfWork`
- **When** `CommitAsync()` completes
- **Then** `DbTransaction.CommitAsync` has been awaited, the transaction has been disposed, the
  connection has been closed and disposed, `IsActive` is `false` and `Context` is `null`

#### Scenario: Rollback closes the connection

- **Given** an active `SqlUnitOfWork`
- **When** `RollbackAsync()` completes
- **Then** `DbTransaction.RollbackAsync` has been awaited and exactly the same cleanup has run

### Requirement: A failed SQL commit or rollback releases the connection

The system SHALL, when `SqlUnitOfWork.CommitAsync` or `RollbackAsync` throws (deadlock victim,
connection drop, cancellation), release the transaction and the connection before rethrowing the
provider's exception unchanged; disposing the uncommitted `DbTransaction` rolls it back. A failure while
releasing is swallowed so it cannot replace that exception. After a failed commit the unit of work is
inactive, and the first `RollbackAsync` that follows is a no-op (SH-M428, fixed by TASK-325).

#### Scenario: Commit fails with a database error

- **Given** an active `SqlUnitOfWork` whose underlying transaction will fail to commit
- **When** `CommitAsync()` is called and the provider throws
- **Then** the exception propagates unwrapped, the transaction and the connection have been disposed,
  `IsActive` is `false` and `Context` is `null`

#### Scenario: Catch-and-roll-back after a failed commit

- **Given** the caller's pattern `try { await uow.CommitAsync(); } catch { await uow.RollbackAsync(); throw; }`
- **When** the commit throws
- **Then** `RollbackAsync()` returns without effect and the commit's exception — not a
  `NoActiveTransactionException` — reaches the caller; a second `RollbackAsync()` throws
  `NoActiveTransactionException` as usual

#### Scenario: Rollback fails

- **Given** an active `SqlUnitOfWork` whose rollback statement throws
- **When** `RollbackAsync()` is called
- **Then** the exception propagates and the connection has been released

### Requirement: A failed SQL begin releases the created connection

The system SHALL, when `OpenAsync` or `BeginTransactionAsync` throws during `SqlUnitOfWork.BeginAsync`,
release the created connection before rethrowing, leaving `IsActive` `false` and `Context` `null`
(SH-M427, fixed by TASK-325).

#### Scenario: Open fails with bad credentials

- **Given** a `SqlUnitOfWork` whose settings produce an unreachable or unauthorised connection string
- **When** `BeginAsync()` throws from `OpenAsync`
- **Then** the created connection has been disposed, `IsActive` is `false` and `Context` is `null`

#### Scenario: Retrying begin after a failed begin

- **Given** a `SqlUnitOfWork` whose previous `BeginAsync()` threw
- **When** `BeginAsync()` is called again
- **Then** a new connection is created, and every connection from a failed attempt has already been
  released — none is stranded by the field being overwritten

### Requirement: SQL disposal releases resources without an explicit rollback call

The system SHALL make `SqlUnitOfWork` disposal idempotent through a `_disposed` flag, with
`DisposeAsync` delegating to `CleanupAsync` and then releasing the ambient cell installed at
construction, and `Dispose` performing the synchronous equivalent (leaving the ambient scope, releasing
the ambient cell, clearing any joined boundary, `_transaction?.Dispose()`, `_connection?.Close()`,
`_connection?.Dispose()`, `Context = null`); neither path calls `RollbackAsync`, so an uncommitted
transaction is ended by disposing the `DbTransaction` rather than by an explicit rollback.

#### Scenario: Disposing an uncommitted unit of work

- **Given** a `SqlUnitOfWork` with an active transaction and pending writes
- **When** the surrounding `await using` scope exits and `DisposeAsync()` runs
- **Then** `CleanupAsync` disposes the transaction and closes the connection; `RollbackAsync` is
  never invoked and no `NoActiveTransactionException` can arise from disposal

#### Scenario: Double disposal is a no-op

- **Given** a `SqlUnitOfWork` already disposed
- **When** `Dispose()` and `DisposeAsync()` are each called again
- **Then** both return without touching the (already-null) fields, because the `_disposed` guard
  short-circuits them

### Requirement: SQL unit of work construction and store-derived factory

The system SHALL reject a null connector or null settings in the `SqlUnitOfWork` constructor with
`ArgumentNullException`, and SHALL provide
`static SqlUnitOfWork FromStore<DB, T>(AsyncDataBaseStore<DB, T> store)` which reads the store's
`Connector`, throws `InvalidOperationException("Store connector is not initialized. Call
SetSettings() first.")` when it is null, and otherwise constructs the unit of work from that
connector and the connector's own `Settings`.

#### Scenario: Null connector rejected at construction

- **Given** a call `new SqlUnitOfWork(null!, settings)`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown naming `connector`

#### Scenario: Factory on an unconfigured store

- **Given** an `AsyncDataBaseStore<DB, T>` on which `SetSettings()` has not been called, so
  `Connector` is null
- **When** `SqlUnitOfWork.FromStore(store)` is called
- **Then** `InvalidOperationException` is thrown with the message `"Store connector is not
  initialized. Call SetSettings() first."`

#### Scenario: Factory reuses the connector's settings

- **Given** a configured `AsyncDataBaseStore<DB, T>` whose `Connector.Settings` is a
  `PasswordSettings` descendant
- **When** `SqlUnitOfWork.FromStore(store)` is called
- **Then** the returned unit of work holds that same connector and those same settings, obtained
  through the connector's public `Settings` property rather than by reflection

#### Scenario: SqlUnitOfWork resolves without a namespace qualifier

- **Given** a consumer compiling against `Birko.Data.SQL`
- **When** it references the type name `SqlUnitOfWork`
- **Then** the type resolves from the global namespace, because its source file declares no
  `namespace` — unlike `SqlTransactionContext` (`Birko.Data.SQL.Stores`) and
  `ElasticSearchUnitOfWork` (`Birko.Data.ElasticSearch.UnitOfWork`)

### Requirement: SQL unit of work publishes its transaction as an ambient boundary

The system SHALL install an ambient `AmbientSqlTransaction` cell in the `SqlUnitOfWork` constructor —
synchronously, because an `AsyncLocal` set inside an awaited `BeginAsync` would not flow back to the
awaiting caller — and SHALL, after a successful owning `BeginAsync`, enter the new connection and
transaction into that ambient scope keyed by the settings' `GetId()`, so that stores in the same flow
participate without each being handed the context. The ambient entry SHALL be left before the
`DbTransaction` is committed or rolled back, and on cleanup and disposal; the cell SHALL be released on
disposal.

#### Scenario: A store in the same flow sees the boundary

- **Given** a `SqlUnitOfWork` constructed and begun in the caller's flow
- **When** code in that flow looks up `AmbientSqlTransaction.Find(settings.GetId())`
- **Then** it finds an entry carrying the unit of work's connection and transaction

#### Scenario: The ambient entry is withdrawn before commit

- **Given** an owning, active `SqlUnitOfWork`
- **When** `CommitAsync()` runs
- **Then** the ambient entry is exited before `DbTransaction.CommitAsync` is awaited, so a store used
  during the commit cannot enlist in the transaction being ended

### Requirement: SQL after-commit work runs once, after a successful commit

The system SHALL let code inside a SQL transaction register work to run after it commits,
`AmbientSqlTransaction.OnCommitted(transaction, key, callback)`, keyed by the `DbTransaction` (a second
registration with the same key on the same transaction is ignored). The boundary's owner — `SqlUnitOfWork.CommitAsync`,
and the SQL bulk stores' `Atomically` / `AtomicallyAsync` for the transaction they open — SHALL run the callbacks once
after a successful commit and drop them on rollback, a failed commit or disposal; a joined participant's commit SHALL
run nothing. A callback that throws SHALL NOT fail the commit or stop the next callback: its exception is recorded in
the connector's `SubscriberFailures` under channel `AfterCommit` (TASK-669). A `DbTransaction` committed by its caller
outside these owners runs no callbacks.

#### Scenario: Callbacks run after the commit

- **Given** a callback registered on a `SqlUnitOfWork`'s transaction
- **When** `CommitAsync` succeeds
- **Then** the callback has run exactly once

#### Scenario: A failing callback is recorded, not thrown

- **Given** two callbacks on one transaction, the first throwing
- **When** `CommitAsync` runs
- **Then** it completes, the second callback runs, and `SubscriberFailures` holds an `AfterCommit` entry for the exception

#### Scenario: A rollback drops the callbacks

- **Given** a callback registered on a `SqlUnitOfWork`'s transaction
- **When** `RollbackAsync` runs
- **Then** the callback never runs

#### Scenario: Null registration arguments are refused

- **Given** a call `AmbientSqlTransaction.OnCommitted(transaction, key, callback)` with any of the three arguments null
- **When** it runs
- **Then** `ArgumentNullException` naming that argument is thrown and nothing is registered

#### Scenario: A second registration under the same key is ignored

- **Given** a callback registered on a transaction under key `"k"`
- **When** a second callback is registered on the same transaction under `"k"` (ordinal comparison) and the owner commits
- **Then** only the first callback runs

### Requirement: The ambient SQL boundary is flow-private, skips ended entries and can be suppressed

The system SHALL keep the ambient SQL boundary chain in a mutable cell carried by an `AsyncLocal`:
`AmbientSqlTransaction.InstallCell()` SHALL install a fresh cell seeded with the inherited head and return a handle that
restores the previous cell; `Enter(settingsId, connection, transaction)` SHALL refuse a null or empty settings id and a
null connection or transaction with `ArgumentNullException`, push an `Entry` (creating a cell when the flow has none) and
return a handle whose disposal marks the entry ended and pops it only if it is still the head; `Find(settingsId)` SHALL
return the innermost entry that is not ended and whose `SettingsId` matches ordinally (null for a null or empty id), and
`Current` the innermost entry that is not ended for any database. `Suppress()` SHALL hide the whole chain behind a fresh
empty cell until its handle is disposed, ending, committing and rolling back nothing.

#### Scenario: Two flows forked from one ancestor do not see each other's boundaries

- **Given** two concurrent flows that each called `InstallCell()` after forking from a common ancestor
- **When** one of them enters a boundary
- **Then** `Find` in the other flow does not return it, while both still see the ancestor's boundary

#### Scenario: An ended entry is never resolved

- **Given** a flow holding a cell that still references an entry whose scope has been disposed
- **When** `Find` or `Current` is read in that flow
- **Then** the ended entry is skipped, so no lookup resolves a connection its owner has already disposed

#### Scenario: Out-of-order disposal removes only its own entry

- **Given** two entries entered in order A then B
- **When** A's handle is disposed first
- **Then** A is marked ended, B stays the head, and no exception is thrown

#### Scenario: Suppression hides boundaries for every database

- **Given** a flow inside boundaries against settings ids `X` and `Y`
- **When** code runs inside `using (AmbientSqlTransaction.Suppress())`
- **Then** `Find("X")`, `Find("Y")` and `Current` are all null, and after disposal both boundaries are found again

### Requirement: A SQL store exposes its connector without its generic arguments

The system SHALL define `ISqlConnectedStore` (namespace `Birko.Data.SQL.Stores`) exposing the store's
`AbstractConnector? Connector` (null until its settings are set) and the `SqlTransactionContext? TransactionContext`
given with `SetTransactionContext`, implemented by `DataBaseStore` and `AsyncDataBaseStore`, so code composing stores it
did not create can tell whether two stores share one database by comparing `Connector.Settings.GetId()` (TASK-683).

#### Scenario: Two stores on one database

- **Given** two SQL stores of different model types configured with settings of the same id
- **When** each is read as `ISqlConnectedStore` and their `Connector.Settings.GetId()` values are compared
- **Then** they are equal — the same key the ambient transaction is looked up by

### Requirement: A nested SQL unit of work joins the enclosing boundary

The system SHALL, when `SqlUnitOfWork.BeginAsync` finds a live enclosing ambient boundary for the same
settings id, join it instead of opening a second connection: the unit of work becomes a participant
(`IsParticipant == true`, `IsActive == true`) whose `Context` wraps the enclosing connection and
transaction. A participant's `CommitAsync` SHALL commit nothing and detach; a participant's
`RollbackAsync` SHALL undo nothing, mark the enclosing boundary rollback-only and detach. The owner's
`CommitAsync` on a boundary marked rollback-only SHALL throw `TransactionRollbackOnlyException` without
committing, and a participant asked to commit a boundary already marked rollback-only SHALL throw it too.

#### Scenario: Nested begin against the same database

- **Given** an owning `SqlUnitOfWork` begun on settings with id `X`, and a second `SqlUnitOfWork` for
  settings with the same id constructed and begun in the same flow
- **When** the second unit of work's `BeginAsync()` completes
- **Then** no connection was created for it, `IsParticipant` is `true`, and its `Context.Connection` /
  `Context.Transaction` are the owner's

#### Scenario: Nested begin against a different database opens its own transaction

- **Given** an active owning boundary for settings id `X`
- **When** a unit of work for settings id `Y` begins in the same flow
- **Then** it finds no enclosing boundary for `Y` and opens its own connection and transaction

#### Scenario: Participant commit defers to the owner

- **Given** a participant `SqlUnitOfWork` on a boundary not marked rollback-only
- **When** its `CommitAsync()` is called
- **Then** no database commit is issued, its `Context` becomes `null` and `IsActive` becomes `false`;
  the writes are committed only when the owner commits

#### Scenario: Participant rollback poisons the owner's commit

- **Given** a participant that called `RollbackAsync()` inside an owner's boundary
- **When** the owner then calls `CommitAsync()`
- **Then** `TransactionRollbackOnlyException` is thrown, nothing is committed, and the owner is still
  active so it can call `RollbackAsync()`

#### Scenario: Disposing a participant without commit or rollback

- **Given** a participant `SqlUnitOfWork` that is disposed while still joined
- **When** `Dispose()` / `DisposeAsync()` runs
- **Then** the join is cleared without marking the boundary rollback-only, and the owner's transaction
  is untouched

### Requirement: Elasticsearch unit of work buffers operations instead of transacting

The system SHALL implement `ElasticSearchUnitOfWork` as `IUnitOfWork<BulkOperationContext>` where
`BeginAsync` only allocates a new in-memory `BulkOperationContext` (returning
`Task.CompletedTask`, contacting no server), `IsActive` is derived from that context being non-null,
and no write reaches Elasticsearch until `CommitAsync`.

#### Scenario: Begin performs no I/O

- **Given** an `ElasticSearchUnitOfWork` built over an `ElasticClient`
- **When** `BeginAsync()` is awaited
- **Then** it completes synchronously, `IsActive` is `true`, `Context` is a fresh
  `BulkOperationContext` with an empty operation list, and no request has been sent

#### Scenario: Buffered writes are invisible until commit

- **Given** an active `ElasticSearchUnitOfWork` on which `Context.Index(document)` has been called
- **When** the same document id is searched for through any other client
- **Then** it is not present, because the operation is only a queued
  `Func<BulkDescriptor, IBulkRequest>` delegate

### Requirement: Bulk operation buffer accepts index, delete and update with optional index override

The system SHALL expose on `BulkOperationContext` the generic methods `Index<T>(T document, string?
index = null)`, `Delete<T>(string id, string? index = null)` and `Update<T>(string id, T
partialDocument, string? index = null)` (each `where T : class`), each appending one descriptor
delegate to the internal list and applying the explicit index name only when the `index` argument is
non-null; the context SHALL also expose the `ElasticClient` used for the flush, and its constructor
SHALL be internal so only the unit of work can create one.

#### Scenario: Index name omitted falls back to client defaults

- **Given** an active `BulkOperationContext`
- **When** `Index(doc)` is called with no index argument
- **Then** the queued descriptor sets only the document, leaving index resolution to NEST's default
  inference

#### Scenario: Index name supplied is applied per operation

- **Given** an active `BulkOperationContext`
- **When** `Delete<MyDoc>("id-1", "custom-index")` is called
- **Then** the queued delete descriptor carries both the id and `custom-index`, independently of any
  other queued operation's index

#### Scenario: Operation ordering is preserved

- **Given** calls to `Index(a)`, `Delete<T>("b")` and `Update("c", partial)` in that order
- **When** `CommitAsync()` builds the bulk request
- **Then** the delegates are applied to the `BulkDescriptor` in exactly the order they were enqueued

### Requirement: Elasticsearch commit flushes one bulk request

The system SHALL, on `CommitAsync`, clear the context and then issue a single `_client.BulkAsync(...)`
call containing every buffered operation, passing the cancellation token to it; when the buffer is
empty it SHALL skip the request entirely and report success.

#### Scenario: Non-empty buffer is flushed as one call

- **Given** an active unit of work with three buffered operations
- **When** `CommitAsync()` succeeds
- **Then** exactly one Bulk request was sent carrying all three items, `IsActive` becomes `false`
  and `Context` becomes `null`

#### Scenario: Empty commit is a silent success

- **Given** an active unit of work with zero buffered operations
- **When** `CommitAsync()` is called
- **Then** no request is sent, no exception is thrown, and the unit of work returns to the inactive
  state

### Requirement: Elasticsearch commit failure is reported as UnitOfWorkException

The system SHALL throw from `CommitAsync` when `response.IsValid` is false — which NEST 7 also reports
when any item failed. When the response names failed items it SHALL throw
`ElasticSearchBulkCommitException` (a `UnitOfWorkException`) whose `Failures` lists each refused item
(`Operation`, `Index`, `Id`, `Status`, `Reason`) and whose `SucceededCount` counts the applied ones;
otherwise it SHALL throw `UnitOfWorkException` with message
`"Elasticsearch bulk operation failed: {reason}"`, the reason falling back through
`ServerError?.Error?.Reason`, then `OriginalException?.Message`, then `"Unknown error"`, with
`response.OriginalException` as the inner exception.

#### Scenario: Whole request rejected by the server

- **Given** a bulk call that comes back with `IsValid == false`, a `ServerError` reason and no items
- **When** `CommitAsync()` evaluates the response
- **Then** `UnitOfWorkException` is thrown whose message embeds that server reason and whose
  `InnerException` is `response.OriginalException`

#### Scenario: Transport failure with no server error

- **Given** a bulk response with `IsValid == false`, no `ServerError`, no items and an `OriginalException`
- **When** `CommitAsync()` evaluates the response
- **Then** the message embeds `OriginalException.Message`, and if both are absent the literal
  `"Unknown error"` is used

#### Scenario: Some items failed, others were applied

- **Given** a bulk response of three items where an `index` failed with 400 and an `update` failed with 404
- **When** `CommitAsync()` evaluates the response
- **Then** `ElasticSearchBulkCommitException` is thrown with those two items in `Failures` and
  `SucceededCount == 1`, and the succeeded item remains applied in Elasticsearch — nothing is
  compensated or undone (SH-M426, fixed by TASK-325; it used to surface as
  `"failed: Unknown error"` naming no item, measured)

### Requirement: A failed Elasticsearch commit ends the unit of work

The system SHALL clear the buffer when the bulk request is sent, whatever its outcome, so a commit
that throws leaves the unit of work `IsActive == false` with nothing queued; the caller retries the
items named in `ElasticSearchBulkCommitException.Failures` in a new unit of work (SH-M426, fixed by
TASK-325).

#### Scenario: A retry does not re-send applied items

- **Given** an `ElasticSearchUnitOfWork` whose `CommitAsync()` threw for a batch of three operations
- **When** the caller calls `BeginAsync()` again
- **Then** the new buffer is empty — the operations that already succeeded are not sent a second time

#### Scenario: Discarding after a failed commit

- **Given** the same failed state
- **When** the caller calls `RollbackAsync()` instead of retrying
- **Then** it returns without effect, leaving Elasticsearch with whichever subset of the operations
  had already been applied

### Requirement: Elasticsearch rollback and disposal discard the buffer locally

The system SHALL implement `RollbackAsync` as a purely local discard — setting the context to null
and returning `Task.CompletedTask` without contacting the server — and SHALL make both `Dispose` and
`DisposeAsync` drop any active context under the `_disposed` guard, so an un-committed unit of work
is abandoned rather than flushed.

#### Scenario: Rollback of buffered writes sends nothing

- **Given** an active unit of work with buffered index and delete operations
- **When** `RollbackAsync()` is awaited
- **Then** no request is sent, `IsActive` becomes `false`, and the buffered work is permanently lost

#### Scenario: Leaving an await-using scope without committing

- **Given** `await using var uow = new ElasticSearchUnitOfWork(client);` with `BeginAsync()` called
  and operations buffered
- **When** the scope exits without `CommitAsync()`
- **Then** `DisposeAsync()` marks the unit of work disposed and drops the buffer; no bulk request is
  sent and no exception is raised to signal the dropped work

### Requirement: Elasticsearch construction and store-derived factory

The system SHALL reject a null `ElasticClient` in the `ElasticSearchUnitOfWork` constructor with
`ArgumentNullException`, and SHALL provide
`static ElasticSearchUnitOfWork FromStore<T>(AsyncElasticSearchStore<T> store)` (with `T :
AbstractModel`) which reads the store's `Connector` and throws
`InvalidOperationException("Store connector is not initialized. Call SetSettings() first.")` when it
is null.

#### Scenario: Null client rejected

- **Given** a call `new ElasticSearchUnitOfWork(null!)`
- **When** the constructor runs
- **Then** `ArgumentNullException` is thrown naming `client`

#### Scenario: Factory on an unconfigured store

- **Given** an `AsyncElasticSearchStore<T>` whose `Connector` is null
- **When** `ElasticSearchUnitOfWork.FromStore(store)` is called
- **Then** `InvalidOperationException` is thrown with the same "Store connector is not initialized"
  message used by `SqlUnitOfWork.FromStore`

### Requirement: Backend divergence in transactional guarantees

The system SHALL NOT offer uniform atomicity across the two implementations: `SqlUnitOfWork` provides
a real ADO.NET transaction (writes are performed as they are issued on the shared connection and are
atomically committed or rolled back by the database), whereas `ElasticSearchUnitOfWork` provides only
best-effort batching (writes are deferred until commit, then applied item-by-item by the Bulk API,
where individual items may succeed or fail independently and rollback of applied items is impossible).

#### Scenario: Mid-transaction failure under SQL

- **Given** two stores enlisted in the same `SqlUnitOfWork` via `SetTransactionContext(uow.Context)`,
  the first write having already executed
- **When** the second write fails and the caller calls `RollbackAsync()`
- **Then** the database undoes the first write as well — nothing from the boundary persists

#### Scenario: Mid-batch failure under Elasticsearch

- **Given** three operations buffered in an `ElasticSearchUnitOfWork`
- **When** `CommitAsync()` sends the bulk request and one item is rejected
- **Then** the other two are already applied and stay applied; `RollbackAsync()` at this point only
  discards the local buffer and cannot restore the pre-commit state

#### Scenario: Isolation differs before commit

- **Given** an active boundary on each backend with one write issued
- **When** a concurrent reader queries the affected record
- **Then** under SQL the write exists inside the transaction (visible to the enlisted stores,
  isolated from others per the provider's isolation level), while under Elasticsearch the write does
  not exist anywhere yet — it is only a delegate in a local list

#### Scenario: A unique refusal inside a SQL transaction differs per provider

- **Given** a `SqlUnitOfWork` whose write is refused by a unique index that another connection's commit now holds
- **When** the write's caller reads again inside the same transaction
- **Then** on PostgreSQL the read fails with `25P02` (the refusal aborted the transaction); on MySQL it succeeds but
  reads the REPEATABLE READ snapshot without the other commit; on SQL Server it sees the other row (TASK-685)

#### Scenario: A retryable write keeps a PostgreSQL transaction usable

- **Given** a `SqlUnitOfWork` on PostgreSQL holding one created row
- **When** a create of a duplicate key runs through `RetryableWrite.RunAsync(store, …)` and is refused
- **Then** the SQL store had run it in a savepoint and rolled back to it, so the transaction still answers and commits the
  first row; the same create made directly leaves the transaction aborted (TASK-694: only retryable writes take a
  savepoint, priced at two round trips each)

### Requirement: Every unit of work declares its transactional capabilities

The system SHALL describe what a boundary promises with `ITransactionCapabilities` — `Atomicity`
(`TransactionAtomicity`: `None` 0, `BestEffort` 1, `Atomic` 2), `Scope` (`TransactionBoundaryScope`:
`None` 0, `SingleDocument` 1, `SinglePartition` 2, `Database` 3, `Cluster` 4),
`ReadsSeeUncommittedWrites`, `RequiresServerTopology` and a nullable `Limitations` text — implemented by
the immutable `TransactionCapabilities`, which also offers a static `TransactionCapabilities.None`
(no atomicity, no scope, reads do not see own writes, with a "no transaction concept" limitation). Each
unit of work SHALL expose a fixed instance:

| Unit of work | Atomicity | Scope | Reads see own writes | Requires topology |
|---|---|---|---|---|
| `SqlUnitOfWork` | `Atomic` | `Database` | true | false |
| `MongoDbUnitOfWork` | `Atomic` | `Cluster` | true | true |
| `RavenDbUnitOfWork` | `Atomic` | `Cluster` | false | false |
| `CosmosDbUnitOfWork` | `Atomic` | `SinglePartition` | false | false |
| `ElasticSearchUnitOfWork` | `BestEffort` | `None` | false | false |
| `InfluxDbUnitOfWork` | `BestEffort` | `None` | false | false |

#### Scenario: A caller inspects a boundary before relying on it

- **Given** a `CosmosDbUnitOfWork`
- **When** the caller reads `Capabilities`
- **Then** `Scope` is `SinglePartition`, `ReadsSeeUncommittedWrites` is `false`, and `Limitations`
  states that reads inside the batch do not see the batch's own writes

#### Scenario: Capabilities render as a sentence

- **Given** the `SqlUnitOfWork` capabilities
- **When** `ToString()` is called
- **Then** it returns `"Atomic within Database, reads see own writes"`; a capabilities object with
  `RequiresServerTopology` appends `", requires a specific server topology"`, and one whose reads do not
  see own writes says `", reads do NOT see own writes"`

### Requirement: MongoDB unit of work wraps a client session transaction

The system SHALL implement `MongoDbUnitOfWork` as `IUnitOfWork<IClientSessionHandle>`, constructed from
an `IMongoClient` or a Birko `MongoDBClient` (null → `ArgumentNullException`), with
`FromStore<T>(AsyncMongoDBStore<T>)` throwing
`InvalidOperationException("Store client is not initialized. Call SetSettings() first.")` when the store
has no client. `BeginAsync` SHALL start a session with `StartSessionAsync(ct)` and call
`StartTransaction()`; `IsActive` SHALL be `_session?.IsInTransaction ?? false`; `CommitAsync` and
`RollbackAsync` SHALL call `CommitTransactionAsync(ct)` / `AbortTransactionAsync(ct)` on the session and
SHALL then dispose and drop it, whether the call succeeded or threw (TASK-578, SH-M429). A failed commit
SHALL propagate the driver's exception unchanged, leave the unit of work inactive, and make the first
`RollbackAsync` that follows a no-op, as on SQL and Elasticsearch (TASK-325).

#### Scenario: Begin against a standalone server

- **Given** a `MongoDbUnitOfWork` over a standalone `mongod`
- **When** `BeginAsync()` is awaited
- **Then** it succeeds; the transaction fails only at the first write, as `Capabilities.Limitations` states

#### Scenario: Commit or rollback releases the session

- **Given** a `MongoDbUnitOfWork` that has begun and committed (or rolled back)
- **When** the call returns
- **Then** `IsActive` is `false`, `Context` is `null` and the session is disposed, so a following
  `BeginAsync()` starts a fresh session (before TASK-578 the old one stayed held and undisposed)

#### Scenario: A failed commit ends the boundary

- **Given** an active `MongoDbUnitOfWork` whose `CommitTransactionAsync` throws (measured live with a
  cancelled token)
- **When** `CommitAsync()` is called
- **Then** the exception propagates unchanged, the session is disposed, which aborts the transaction on the
  server (the same row is written outside it at once), and `RollbackAsync()` returns without effect

### Requirement: RavenDB unit of work wraps a cluster-wide session

The system SHALL implement `RavenDbUnitOfWork` as `IUnitOfWork<IAsyncDocumentSession>`, constructed from
an `IDocumentStore` (null → `ArgumentNullException`), with `FromStore<T>(AsyncRavenDBStore<T>)` throwing
`InvalidOperationException("Store DocumentStore is not initialized. Call SetSettings() first.")` when the
store has no document store. `BeginAsync` SHALL open an async session with
`TransactionMode = TransactionMode.ClusterWide` without contacting the server; `IsActive` SHALL be
whether a session is held; `CommitAsync` SHALL await `SaveChangesAsync(ct)` and dispose and drop the session
whatever the outcome; a failed commit SHALL propagate the driver's exception unchanged and make the first
`RollbackAsync` that follows a no-op, as on every other backend (TASK-658). `RollbackAsync` SHALL dispose and drop the
session without saving.

#### Scenario: Rollback discards unsaved changes

- **Given** an active `RavenDbUnitOfWork` whose session tracks stored documents
- **When** `RollbackAsync()` is called
- **Then** the session is disposed without `SaveChangesAsync`, nothing reaches the server, and `IsActive`
  becomes `false`

#### Scenario: A failed SaveChanges ends the unit of work

- **Given** an active `RavenDbUnitOfWork` whose `SaveChangesAsync` throws: a concurrency conflict
  (`ClusterTransactionConcurrencyException`) or a cancelled token (measured live)
- **When** `CommitAsync()` is called
- **Then** the exception propagates unchanged, `IsActive` is `false` and `Context` is `null`, the first
  `RollbackAsync()` returns without effect, and a second `CommitAsync()` throws `NoActiveTransactionException`.
  Nothing was applied. Before TASK-658 the session stayed held: every retry after a conflict hit the same conflict,
  and a retry after a cancellation wrote the changes late

### Requirement: Document-store sessions are published per flow through AmbientSession

The system SHALL provide `AmbientSession<TSession>` (namespace `Birko.Data.Stores`, `TSession : class`, one chain per
session type) with the same mechanics as `AmbientSqlTransaction`: `InstallCell()` installs a fresh cell seeded with the
inherited head and returns a handle restoring the previous one; `Enter(key, session)` refuses a null or empty key
(`ArgumentException`) and a null session (`ArgumentNullException`), publishes the session and returns a `Scope` whose
disposal marks the entry ended and pops it only if it is still the head; `Find(key)` returns the innermost published
session for the key that is not ended (null for a null or empty key); `MarkRollbackOnly(key)` marks that innermost entry
rollback-only and returns `true`, or returns `false` when nothing is published for the key; `Scope.IsRollbackOnly`
reports the mark. Publishing opens, commits and disposes nothing (TASK-700).

#### Scenario: A published session is found by its key

- **Given** a flow that entered `AmbientSession<S>.Enter("db1", session)`
- **When** `Find("db1")` and `Find("db2")` are called in that flow
- **Then** the first returns `session` and the second returns null; after the scope is disposed `Find("db1")` returns null

#### Scenario: Marking with nothing published

- **Given** a flow with no session published for `"db1"`
- **When** `MarkRollbackOnly("db1")` is called
- **Then** it returns `false` and marks nothing

### Requirement: MongoDB and RavenDB units of work publish their session and honour rollback-only

The system SHALL, in `MongoDbUnitOfWork` constructed from a Birko `MongoDBClient` (keyed by `client.Settings.GetId()`) and
in `RavenDbUnitOfWork` (keyed by `RavenAmbientKey.Of(documentStore)`), install an `AmbientSession` cell synchronously in
the constructor and, on `BeginAsync`, publish the new session under that key, so an async store on that database given no
session of its own (`TransactionContext ?? AmbientSession.Find(AmbientKey)`) writes inside it. The publication SHALL be
withdrawn when the transaction ends (commit, rollback, failed commit) and on disposal, which also releases the cell. A
`MongoDbUnitOfWork` built from a bare `IMongoClient` SHALL publish nothing. `CommitAsync` SHALL throw
`TransactionRollbackOnlyException` without committing when a participant marked the published session rollback-only,
leaving the unit of work active for `RollbackAsync` (TASK-700). The RavenDB unit of work publishes an
`IAsyncDocumentSession`, so the synchronous `RavenDBStore` (which looks up `IDocumentSession`) does not join.

#### Scenario: A store in the same flow joins the MongoDB transaction

- **Given** a `MongoDbUnitOfWork` built from the store's `MongoDBClient` in the caller's flow, and begun
- **When** an `AsyncMongoDBStore` on the same database with no `TransactionContext` writes, and the unit of work rolls back
- **Then** the write ran in the unit of work's session and is undone with it

#### Scenario: A failed participant poisons the commit

- **Given** an active `MongoDbUnitOfWork` or `RavenDbUnitOfWork` whose published session a joined write boundary marked
  with `AmbientSession.MarkRollbackOnly(key)`
- **When** `CommitAsync()` is called
- **Then** `TransactionRollbackOnlyException` is thrown, nothing is committed or saved, `IsActive` stays `true`, and
  `RollbackAsync()` then ends the boundary

#### Scenario: Unit of work over a bare driver client does not publish

- **Given** a `MongoDbUnitOfWork` constructed from an `IMongoClient`
- **When** it begins and a store on the same database writes without a context
- **Then** the store's write does not run in the unit of work's session

### Requirement: Cosmos DB unit of work wraps a single-partition transactional batch

The system SHALL implement `CosmosDbUnitOfWork` as `IUnitOfWork<TransactionalBatch>`, constructed from a
`Container` (null → `ArgumentNullException`) and one `PartitionKey`, with
`FromStore<T>(AsyncCosmosDBStore<T>, PartitionKey)` throwing
`InvalidOperationException("Store Container is not initialized. Call SetSettings() and InitAsync() first.")`
when the store has no container. `BeginAsync` SHALL create a `TransactionalBatch` for that partition key
without I/O; `CommitAsync` SHALL drop the batch before awaiting `ExecuteAsync(ct)`, so it is spent
whatever the outcome, SHALL throw
`UnitOfWorkException("Cosmos DB transactional batch failed with status {StatusCode}.")` when the response
is not a success status, and SHALL propagate an exception `ExecuteAsync` throws unchanged; after either
failure the first `RollbackAsync` SHALL be a no-op (TASK-578, SH-M430). `RollbackAsync` and disposal SHALL
drop the batch without sending it.

#### Scenario: A refused batch is reported with its status

- **Given** an active `CosmosDbUnitOfWork` whose batch execution returns a non-success status (measured
  live: a create of an existing id, `Conflict`)
- **When** `CommitAsync()` is called
- **Then** a `UnitOfWorkException` naming the status is thrown (it was an `InvalidOperationException`
  before TASK-578), the unit of work is inactive, and `RollbackAsync()` returns without effect

#### Scenario: A throwing execute ends the boundary

- **Given** an active `CosmosDbUnitOfWork` whose `ExecuteAsync` throws (measured with a cancelled token)
- **When** `CommitAsync()` is called
- **Then** the exception propagates unchanged and `IsActive` is `false`, so a retry cannot re-send the
  batch (before TASK-578 it stayed active)

### Requirement: InfluxDB unit of work buffers points for one write

The system SHALL implement `InfluxDbUnitOfWork` as `IUnitOfWork<BatchPointContext>`, constructed from an
`InfluxDBClient`, a bucket and an organization (each null → `ArgumentNullException`), with
`FromStore<T>(AsyncInfluxDBStore<T>)` reading the store's client and public `Settings` (bucket,
organization) and throwing `InvalidOperationException` when either is missing. `BeginAsync` SHALL
allocate a `BatchPointContext` (exposing `Bucket`, `Organization`, `AddPoint`, `AddPoints`) without I/O;
`CommitAsync` SHALL drop the context and then write all buffered points in one
`WritePointsAsync(points, bucket, organization, ct)` call when there is at least one, so the points are
spent whatever the outcome; a failed write SHALL propagate the client's exception unchanged and make the
first `RollbackAsync` that follows a no-op (TASK-578, SH-M431). `RollbackAsync` and disposal SHALL drop the
context without writing.

#### Scenario: Empty commit writes nothing

- **Given** an active `InfluxDbUnitOfWork` with no points added
- **When** `CommitAsync()` is called
- **Then** no write API call is made and the unit of work becomes inactive

#### Scenario: A failed write ends the boundary

- **Given** an active `InfluxDbUnitOfWork` with buffered points whose write throws (measured live: a
  bucket that does not exist)
- **When** `CommitAsync()` is called
- **Then** the exception propagates, `IsActive` is `false` and `Context` is `null`, as on
  `ElasticSearchUnitOfWork`, so a retry cannot re-send every point (before TASK-578 they stayed queued);
  `RollbackAsync()` returns without effect

### Requirement: Cancellation tokens are honoured only where I/O occurs

The system SHALL pass the cancellation token to the underlying I/O calls it makes —
`Connection.OpenAsync(ct)`, `BeginTransactionAsync(ct)`, `Transaction.CommitAsync(ct)`,
`Transaction.RollbackAsync(ct)` for SQL and `_client.BulkAsync(..., ct)` for Elasticsearch — and
SHALL otherwise ignore the token, performing no `ThrowIfCancellationRequested` of its own.

#### Scenario: Cancelled token on an Elasticsearch begin

- **Given** an already-cancelled `CancellationToken`
- **When** `ElasticSearchUnitOfWork.BeginAsync(cancelledToken)` is called
- **Then** it completes successfully and the unit of work becomes active — the token is never
  examined

#### Scenario: Cancelled token on an Elasticsearch rollback

- **Given** an active unit of work and an already-cancelled token
- **When** `RollbackAsync(cancelledToken)` is called
- **Then** it completes successfully and the buffer is discarded, with no
  `OperationCanceledException`

#### Scenario: Cancelled token on a SQL commit

- **Given** an active `SqlUnitOfWork` and an already-cancelled token
- **When** `CommitAsync(cancelledToken)` is called
- **Then** the disposed and active checks pass and cancellation is observed only when the provider's
  `DbTransaction.CommitAsync(ct)` reacts to it

### Requirement: A retried write runs through the retryable store beneath any decorators

The system SHALL define `IRetryableWriteStore` (`RunRetryable(Action)`, `RunRetryableAsync(Func<Task>, CancellationToken)`)
and the static `RetryableWrite`, whose `Find(store)` returns the first `IRetryableWriteStore` at or beneath the store
along `StoreChain.Walk` (null for a null store), and whose `Run` / `RunAsync` overloads (plain and result-returning)
refuse a null write with `ArgumentNullException`, run the write as is when the store is null (a caller's last attempt),
and otherwise run it through the found retryable store, or as is when none is found (TASK-694).

#### Scenario: Decorated SQL store

- **Given** a decorator over a SQL store that implements `IRetryableWriteStore`
- **When** `RetryableWrite.RunAsync(decorator, write)` is called
- **Then** the write runs through the SQL store's `RunRetryableAsync`

#### Scenario: No retryable store beneath

- **Given** an InMemory store, which implements no `IRetryableWriteStore`
- **When** `RetryableWrite.Run(store, write)` is called
- **Then** the write runs directly, once

### Requirement: A retried write is declared to the participants beneath it

The system SHALL expose `RetryableWrite.IsDeclared`, true while a write run through `Run` / `RunAsync` with a non-null
store executes (an `AsyncLocal` counter, restored when the write returns or throws, and never flowing back to an async
caller), so a participant beneath — such as a joined SQL localized write boundary — leaves its caller's transaction as it
was on failure instead of marking it rollback-only (TASK-697). A write run with a null store is not declared.

#### Scenario: A retry after a refusal can still commit

- **Given** a slug or default decorator inside a `SqlUnitOfWork` whose first write is refused and retried through
  `RetryableWrite`
- **When** a joined localized write boundary beneath sees the refused write fail while `IsDeclared` is true
- **Then** the boundary had run the write in its own `SqlSavepoint` (nested inside a PostgreSQL store's), rolled it back
  without marking the boundary rollback-only, the retry succeeds, and the owner's `CommitAsync()` commits

#### Scenario: The declaration ends with the write

- **Given** code that awaited `RetryableWrite.RunAsync(store, write)`
- **When** it reads `RetryableWrite.IsDeclared` afterwards
- **Then** it is `false`, whether the write succeeded or threw

#### Scenario: A last attempt is not declared

- **Given** a call `RetryableWrite.Run(null, write)`
- **When** the write reads `RetryableWrite.IsDeclared`
- **Then** it is `false` (unless an outer declared write is running)

### Requirement: SQL stores take a savepoint for a retried write only where a refusal aborts the transaction

The system SHALL, in the SQL stores' `IRetryableWriteStore` (produced only by `SqlRetryableWrite`), run the write inside
a savepoint only when the connector's `AbortsTransactionOnError` is true (PostgreSQL) and a transaction is active —
the store's own `SqlTransactionContext.Transaction`, else the ambient boundary found for
`connector.Settings.GetId()`; with no connector, a provider whose refusal does not abort, or no transaction, the write
SHALL run as is (TASK-694).

#### Scenario: PostgreSQL inside a unit of work

- **Given** a PostgreSQL store with no explicit context, inside a `SqlUnitOfWork` on its database
- **When** a retryable write runs
- **Then** it runs inside a savepoint of the unit of work's transaction

#### Scenario: SQL Server or MySQL

- **Given** a SQL Server or MySQL store inside a `SqlUnitOfWork`
- **When** a retryable write runs
- **Then** no savepoint is taken, so it costs no extra round trips

#### Scenario: No transaction active

- **Given** a PostgreSQL store with no context and no ambient boundary
- **When** a retryable write runs
- **Then** it runs as is

### Requirement: SqlSavepoint is the one producer of savepoints

The system SHALL provide `SqlSavepoint.Run(transaction, write)` and `RunAsync(transaction, write, ct)` (null
arguments → `ArgumentNullException`), which save a savepoint named `birko_retry_{n}` with a process-wide increasing `n`,
run the write, release the savepoint when the write succeeds or throws `EntityNotFoundException` (rethrown, so a
collection write keeps what it applied, TASK-555, TASK-705), and when it throws anything else roll back to the savepoint
and rethrow the write's own exception; a failure of that rollback SHALL NOT replace it but SHALL be recorded as its message under
`exception.Data["Birko.SavepointRollbackFailure"]` (`SqlSavepoint.RollbackFailureKey`). The async form SHALL pass the
caller's token only to the save, and `CancellationToken.None` to the rollback and the release (TASK-694, TASK-697).

#### Scenario: A collection write that met a missing entity keeps what it stored

- **Given** a collection update inside `SqlSavepoint.Run` / `RunAsync` that applies two entities and then throws
  `EntityNotFoundException` for a third
- **When** the savepoint handles the exception
- **Then** the savepoint is released rather than rolled back, `EntityNotFoundException` is rethrown, and the two applied
  updates remain in the transaction (TASK-705)

#### Scenario: A refused write leaves the transaction usable

- **Given** a PostgreSQL transaction with one created row
- **When** `SqlSavepoint.RunAsync(transaction, createDuplicate, ct)` runs and the create is refused
- **Then** the unique-violation exception propagates, the transaction is rolled back to the savepoint, and it still
  commits the first row

#### Scenario: Nested savepoints roll back only their own

- **Given** a savepointed write that itself runs a second savepointed write
- **When** the inner write throws and the outer catches it
- **Then** only the inner savepoint is rolled back, because each has its own name

#### Scenario: A failed rollback to the savepoint is recorded

- **Given** a write that throws inside a savepoint whose rollback also throws
- **When** `Run` returns by throwing
- **Then** the exception is the write's, carrying the rollback's message under `"Birko.SavepointRollbackFailure"`

#### Scenario: Cancellation after the write applied

- **Given** a write that succeeded inside `RunAsync` while the caller's token was cancelled
- **When** the savepoint is released
- **Then** the release runs with `CancellationToken.None`, so the applied write is not reported as cancelled
