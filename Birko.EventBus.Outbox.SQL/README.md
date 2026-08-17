# Birko.EventBus.Outbox.SQL

SQL-backed persistent store for the Birko outbox. Built on Birko.Data.SQL, so it works with any
`AbstractConnector` (SQLite, PostgreSQL, MSSql, MySQL).

`Birko.EventBus.Outbox` ships only `InMemoryOutboxStore`, which loses every pending entry when the process
stops — an outbox that does not outlive the process is a queue with extra steps. This is the durable
sibling.

## Features

- **Persistent storage** — entries survive process restarts, stored via Birko.Data.SQL stores
- **Auto-schema creation** — the `__Outbox` table is created by the SQL connector on first use; no DDL
- **Atomic claiming** — `GetPendingAsync` claims each entry it returns, so two processors polling the same
  table cannot publish the same event twice
- **Bounded retry** — honours the `maxAttempts` the core contract passes in, so a poisoned event reaches
  `Failed` instead of being retried forever
- **Safe cleanup** — deletes only `Published` and `Failed` rows; a pending entry is never removed for age

## Dependencies

- Birko.EventBus.Outbox (core contracts)
- Birko.Data.SQL (stores, connectors, attributes)
- .NET 10.0+

## Usage

```csharp
using Birko.EventBus.Outbox.Extensions;
using Birko.EventBus.Outbox.SQL;

// Settings-based (server SQL)
services.AddOutbox(sp => new SqlOutboxStore<PostgreSQLConnector>(settings), opts =>
{
    opts.BatchSize = 100;
    opts.MaxAttempts = 5;
});

// Store-based (SQLite, whose settings are PasswordSettings rather than SqlSettings)
var store = new AsyncSQLiteStore<OutboxEntryModel>();
store.SetSettings(passwordSettings);
services.AddOutbox(sp => new SqlOutboxStore<SqLiteConnector>(store));

services.AddOutboxEventBus();   // decorates IEventBus so PublishAsync writes to the store
```

The `AddOutbox(factory, …)` overload exists because a SQL store is parameterised by connection settings
and a connector type — the container cannot activate it, so the generic `AddOutbox<TStore>()` does not
apply.

## What it guarantees, and what it does not

**At-least-once delivery, inherently.** An entry can be claimed, published, and then fail to be marked
published; a crash in that window republishes it on recovery. The alternative — mark first, publish second
— loses events instead, which is strictly worse. Consumers must tolerate a repeat.

**A claim is not a lease.** If a processor dies mid-batch its entries stay `Publishing` and nothing
reclaims them, because nothing here knows the holder is gone. Reclaiming needs a heartbeat or an age-based
sweep, and both need a policy decision about how long "too long" is.

**Durability is not atomicity.** Storing entries in the same database as the business write makes an atomic
enqueue *possible*; whether the enqueue actually joins the caller's transaction is up to the caller.
