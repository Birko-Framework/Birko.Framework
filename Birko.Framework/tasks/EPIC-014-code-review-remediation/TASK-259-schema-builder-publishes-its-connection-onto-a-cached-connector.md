---
id: TASK-259
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-240, TASK-242, TASK-247, TASK-253]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.SQL, Birko.Data.SQL]
---

# `SqlSchemaBuilder` publishes its connection onto a process-wide cached connector and never clears it

Found while grilling **TASK-253**'s plan, evaluating (and rejecting) the option of routing the TimescaleDB
migration emitters through `connector.CreateHypertable` via `SetExternalTransaction`. The mechanism turned
out to be one the framework had deliberately abandoned everywhere *except* here.

## What is wrong

`Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs` calls
`_connector.SetExternalTransaction(_connection, _transaction)` at **three** sites — `:133`
(`EnsureExternalTransaction`, itself called from five methods), `:218` and `:286` — and **never calls it
again with nulls**. It is the only remaining caller in the framework.

`AbstractConnector.SetExternalTransaction` (`:215`) stores the pair on the connector, and
`DoCommand` (`:186`) / `DoCommandWithTransaction` (`:232`) check
`ExternalConnection != null && ExternalTransaction != null` and route the command onto it. Connectors are
cached **process-wide per (type, settings id)** by `DataBase.GetConnector`.

So once any migration has run, the shared connector retains that migration's connection and transaction
for the life of the process. Every subsequent command from any store against the same database — reads
included — takes the `ExternalConnection` branch onto a connection the migration runner has since
disposed.

**Both stores already carry the explanation, as a reason they stopped doing this:**

> *"Replaces the former `Connector.SetExternalTransaction` call, which published one caller's transaction
> onto a connector cached process-wide per (type, settings id) — i.e. onto every concurrent caller against
> the same database."* — `Birko.Data.SQL/Stores/DataBaseStore.cs:45`

> *"It deliberately no longer calls `Connector.SetExternalTransaction`: connectors are cached process-wide
> per (type, settings id), so that call published one caller's transaction to every concurrent caller
> against the same database."* — `Birko.Data.SQL/Stores/AsyncDataBaseStore.cs:48`

TASK-240 replaced it with `AmbientSqlTransaction`, which is scoped to the async flow and restores itself.
The schema builder was not migrated with them. § Conventions records the pair as *"deliberately **not**
suppressed: its only user is the migrations `SqlSchemaBuilder`, which exists to run DDL in a transaction it
owns"* — which explains why it is *read*, and says nothing about it never being cleared.

## Measure before fixing — this may be entirely latent

Do this first and record the answer as a number, because it decides whether this is a live data-integrity
bug or dead code:

- **Is `SqlSchemaBuilder` reachable from any consumer?** TASK-247 swept all 16 consumer repos and found
  **0** uses of `ISchemaBuilder` and 0 migration-declared indexes. If that still holds, nothing calls this
  in production and the fix is hygiene. Re-run the sweep rather than citing it — TASK-247 closed today and
  the claim is worth one command.
- **Does the stale pair actually survive the runner?** Check whether `SqlMigrationRunner` disposes or
  replaces the connector, and whether anything resets connector state between migrations. It is possible
  the runner constructs a connector that is never the cached one, in which case the blast radius is one
  object.
- **Is a disposed `DbConnection` on that branch loud or silent?** An `ObjectDisposedException` far from the
  migration is bad; Npgsql silently reopening a pooled connection with no transaction would be worse — a
  write that believes it is in a transaction and is not, which is the whole family TASK-240/242 exists to
  close.

State plainly which of firing / latent it is. TASK-246 had to be corrected after its commit landed for
claiming live impact it did not have.

## Acceptance criteria

- [ ] The measurement above recorded in this file as numbers, and the firing-or-latent verdict stated
      before any fix is written.
- [ ] `SqlSchemaBuilder` no longer leaves the pair set past its own use — either scoped and restored (the
      `AmbientSqlTransaction.Suppress()` shape: install, then restore exactly what was there), or migrated
      onto `AmbientSqlTransaction` outright so the legacy pair loses its last caller.
- [ ] If the pair loses its last caller, say whether `SetExternalTransaction` /
      `ExternalConnection` / `ExternalTransaction` are deleted or kept, and why. Keeping a public mechanism
      with no callers is the shape TASK-247 deleted; deleting public surface is a contract change.
- [ ] § Conventions' sentence about the pair being deliberately unsuppressed is updated to match whatever
      is true afterwards. It currently reads as a blessing of the current state.
- [ ] Proven able to fail: a test that runs a migration through `SqlSchemaBuilder` and then asserts a
      subsequent command on the same cached connector does **not** go through the migration's connection.
      That test must go red against today's code.
- [ ] The four provider live suites run, not just built — this touches the path every migration takes.

## Out of scope

- The TimescaleDB migration emitters' identifier quoting and folding — **[[TASK-253]] owns those**, and it
  deliberately does *not* route them through the connector precisely because of this defect.
- `AmbientSqlTransaction`'s own semantics. TASK-240/242/243 settled them; this task either reuses it or
  scopes the legacy pair, and changes neither.
- Whether schema-ensure belongs in a caller's unit of work at all — **[[TASK-244]] owns that**, still open.

## Human test plan

- [ ] N/A — mechanical; the proof is the connection identity a post-migration command runs on.
