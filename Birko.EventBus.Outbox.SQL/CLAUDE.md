# Birko.EventBus.Outbox.SQL

## Overview

SQL-backed persistent store implementing `IOutboxStore` from Birko.EventBus.Outbox. Built on the Birko data
layer — uses `AsyncDataBaseBulkStore<DB, OutboxEntryModel>` for all database operations. Deliberately
mirrors `Birko.BackgroundJobs.SQL` file for file: the two solve the same problem (a core contract needs a
row) and a second convention would only make both harder to read.

## Structure

```
Birko.EventBus.Outbox.SQL/
├── Models/
│   └── OutboxEntryModel.cs   - AbstractModel with SQL attributes, ToEntry/FromEntry mapping
└── SqlOutboxStore.cs         - IOutboxStore<DB> (save, claim-and-return pending, mark published/failed, cleanup)
```

## Dependencies

- Birko.EventBus.Outbox (IOutboxStore, OutboxEntry, OutboxStatus, OutboxOptions)
- Birko.Data.Core (AbstractModel)
- Birko.Data.Stores (store interfaces, OrderBy, PropertyUpdate)
- Birko.Data.SQL (AsyncDataBaseBulkStore, AbstractConnector, SqlSettings, attributes)

## Key Design Decisions

**`GetPendingAsync` CLAIMS, it does not merely read.** Each returned entry is marked with a token via an
`UPDATE` conditional on the row still being `Pending`, so the database serializes concurrent writes and
only one processor's `WHERE` can match. Without it, two processors poll the same table, both see the same
pending rows, and every integration event is published twice. Same mechanism as `SqlJobQueue`'s dequeue —
and the reason `OutboxEntry.ClaimedAt` already exists on the core contract.

**The claim token is verified by re-reading.** The store API exposes no rows-affected count, so reading the
row back and comparing the token is how a losing processor learns it lost.

**At-least-once, inherently.** An entry can be claimed, published, then fail to be marked published; a
crash in that window republishes on recovery. Mark-first-publish-second loses events instead, which is
strictly worse.

**A claim is not a lease.** A processor that dies mid-batch leaves its entries `Publishing` and nothing
reclaims them. Deliberate for a first version — reclaiming needs a heartbeat or an age-based sweep, and
both need a policy decision about how long "too long" is.

**`MarkFailedAsync` takes `maxAttempts` from the caller**, not a constant, because the core contract passes
it from `OutboxOptions.MaxAttempts` — one place to configure it rather than one per store. Reaching the cap
is terminal so a permanently failing handler stops being retried and the failure can be seen.

**Cleanup deletes only `Published` and `Failed`.** A pending entry is the thing this table exists to
protect; removing one for being old would destroy it exactly when something is already wrong.

**Unreadable `Headers` JSON is swallowed, not propagated.** Headers are metadata, the payload is the
message — failing the entry over them would silently lose an event, the exact failure the outbox prevents.

## Gotchas

- The table is `__Outbox`, created by the connector on first use. **No DDL is required** — this is a new
  table, and schema-ensure creates missing tables (it does not add missing columns to existing ones).
- SQLite settings are `PasswordSettings`, not `SqlSettings`, so use the store-based constructor there and
  the settings-based one for server SQL.
