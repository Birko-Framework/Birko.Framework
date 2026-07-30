---
area: unit-of-work-and-transactions
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs
  - ../Birko.Data.Patterns/UnitOfWork/IUnitOfWork.cs
  - ../Birko.Data.Patterns/UnitOfWork/UnitOfWorkException.cs
  - ../Birko.Data.SQL/Stores/SqlTransactionContext.cs
  - ../Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs
  - ../Birko.Data.Stores/ITransactionalStore.cs
shaped-by: []
---

# Unit of work and transactional store boundaries

## Purpose

This capability lets a caller group several store operations into one transactional boundary. A
`IUnitOfWork` owns the boundary (`BeginAsync` / `CommitAsync` / `RollbackAsync`) and, in its generic
form, hands out a backend-specific *transaction context* that individual stores enlist in via
`ITransactionalStore.SetTransactionContext`. Two backends implement the boundary, and they are not
equivalent: `SqlUnitOfWork` wraps a real ADO.NET `DbTransaction` on a dedicated connection, so it is
genuinely atomic; `ElasticSearchUnitOfWork` merely buffers operations in memory and flushes them as
one Elasticsearch Bulk call on commit, which is neither atomic nor isolated. Anything that needs
"these writes land together" depends on this capability, and needs to know which of those two shapes
it is actually getting.

## Requirements

### Requirement: Unit-of-work lifecycle surface

The system SHALL expose the transactional boundary as `IUnitOfWork`, which extends both
`IAsyncDisposable` and `IDisposable`, and SHALL expose the boundary state as a read-only `bool
IsActive` property alongside the three lifecycle methods `BeginAsync(CancellationToken)`,
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
when `IsActive` is `false`, with the message `"No active transaction. Call BeginAsync() first."`.

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
`NoActiveTransactionException` and `TransactionAlreadyActiveException` from it, each supplying its
own fixed message via a parameterless constructor.

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

The system SHALL, on `SqlUnitOfWork.BeginAsync`, create a connection via
`_connector.CreateConnection(_settings)`, open it with `OpenAsync(ct)`, begin a transaction with
`BeginTransactionAsync(ct)`, and publish both on a new `SqlTransactionContext`; `IsActive` SHALL be
derived solely from whether that `DbTransaction` field is non-null.

#### Scenario: Begin produces an open connection carrying a live transaction

- **Given** a `SqlUnitOfWork` constructed with a SQL connector and `PasswordSettings`
- **When** `BeginAsync()` completes
- **Then** `IsActive` is `true` and `Context.Connection` / `Context.Transaction` are the connection
  just opened and the transaction just begun on it

#### Scenario: The connector is used purely as a connection factory

- **Given** a `SqlUnitOfWork` built with any `AbstractConnectorBase` (PostgreSQL, MSSql, MySQL or
  SQLite)
- **When** `BeginAsync()` runs
- **Then** the only connector member invoked is `CreateConnection(settings)`; the transaction
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

### Requirement: A failed SQL commit or rollback leaves the unit of work active

The system SHALL await the transaction operation *before* running cleanup, and SHALL NOT wrap that
await in a try/finally — so when `CommitAsync` or `RollbackAsync` throws (deadlock victim, connection
drop, cancellation), `CleanupAsync` never runs and the unit of work remains `IsActive == true` with
the connection and transaction still held.

#### Scenario: Commit fails with a database error

- **Given** an active `SqlUnitOfWork` whose underlying transaction will fail to commit
- **When** `CommitAsync()` is called and the provider throws
- **Then** the exception propagates unwrapped (it is not translated to `UnitOfWorkException`),
  `IsActive` remains `true`, `Context` is still non-null, and the caller must call `RollbackAsync()`
  or dispose to release the connection

#### Scenario: Cancellation during commit

- **Given** an active `SqlUnitOfWork` and a cancellation token that is cancelled mid-commit
- **When** `CommitAsync(ct)` throws `OperationCanceledException`
- **Then** no cleanup has occurred and the connection remains open

### Requirement: A failed SQL begin retains the created connection outside the active state

The system SHALL assign the newly created connection to its field before opening it, so that when
`OpenAsync` or `BeginTransactionAsync` throws, the connection object remains referenced while
`IsActive` stays `false`.

#### Scenario: Open fails with bad credentials

- **Given** a `SqlUnitOfWork` whose settings produce an unreachable or unauthorised connection string
- **When** `BeginAsync()` throws from `OpenAsync`
- **Then** `IsActive` is `false` and `Context` is `null`, so `CommitAsync`/`RollbackAsync` would
  throw `NoActiveTransactionException`; the created connection is released only when `Dispose()` or
  `DisposeAsync()` is eventually called

#### Scenario: Retrying begin after a failed begin

- **Given** a `SqlUnitOfWork` whose previous `BeginAsync()` threw after the connection field was set
- **When** `BeginAsync()` is called again
- **Then** the passed-in-active check does not trip (`IsActive` is still `false`) and the connection
  field is overwritten with a new connection, without the previous one being closed or disposed

### Requirement: SQL disposal releases resources without an explicit rollback call

The system SHALL make `SqlUnitOfWork` disposal idempotent through a `_disposed` flag, with
`DisposeAsync` delegating to `CleanupAsync` and `Dispose` performing the synchronous equivalent
(`_transaction?.Dispose()`, `_connection?.Close()`, `_connection?.Dispose()`, `Context = null`);
neither path calls `RollbackAsync`, so an uncommitted transaction is ended by disposing the
`DbTransaction` rather than by an explicit rollback.

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

The system SHALL, on `CommitAsync`, issue a single `_client.BulkAsync(...)` call containing every
buffered operation and pass the cancellation token to it, then clear the context; when the buffer is
empty it SHALL skip the request entirely and still clear the context and report success.

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

The system SHALL inspect the bulk response and throw `UnitOfWorkException` in two distinct cases:
when `response.IsValid` is false, with message `"Elasticsearch bulk operation failed: {reason}"`
where the reason falls back through `ServerError?.Error?.Reason`, then
`OriginalException?.Message`, then `"Unknown error"`, passing `response.OriginalException` as the
inner exception; and when `response.Errors` is true, with message `"Elasticsearch bulk operation
partially failed: {n} items had errors."` where `n` is `ItemsWithErrors?.Count() ?? 0` and no inner
exception is attached.

#### Scenario: Whole request rejected by the server

- **Given** a bulk call that comes back with `IsValid == false` and a `ServerError` reason
- **When** `CommitAsync()` evaluates the response
- **Then** `UnitOfWorkException` is thrown whose message embeds that server reason and whose
  `InnerException` is `response.OriginalException`

#### Scenario: Transport failure with no server error

- **Given** a bulk response with `IsValid == false`, no `ServerError`, and an `OriginalException`
- **When** `CommitAsync()` evaluates the response
- **Then** the message embeds `OriginalException.Message`, and if both are absent the literal
  `"Unknown error"` is used

#### Scenario: Some items failed, others were applied

- **Given** a bulk response with `IsValid == true` but `Errors == true` and two items in error
- **When** `CommitAsync()` evaluates the response
- **Then** `UnitOfWorkException` is thrown reporting `"2 items had errors."`, and the items that
  succeeded remain applied in Elasticsearch — nothing is compensated or undone

### Requirement: Elasticsearch commit does not clear its buffer on failure

The system SHALL assign `_context = null` only after both response checks have passed, so a commit
that throws leaves the unit of work `IsActive == true` with every buffered operation still queued.

#### Scenario: Retrying a partially failed commit re-sends everything

- **Given** an `ElasticSearchUnitOfWork` whose `CommitAsync()` threw
  `UnitOfWorkException("... partially failed: 1 items had errors.")` for a batch of three operations
- **When** the caller calls `CommitAsync()` again to retry
- **Then** all three operations are sent again — including the two that already succeeded — because
  the buffer was never trimmed or cleared

#### Scenario: Discarding after a failed commit

- **Given** the same failed state
- **When** the caller calls `RollbackAsync()` instead of retrying
- **Then** it succeeds and drops the buffer, leaving Elasticsearch with whichever subset of the
  operations had already been applied

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
