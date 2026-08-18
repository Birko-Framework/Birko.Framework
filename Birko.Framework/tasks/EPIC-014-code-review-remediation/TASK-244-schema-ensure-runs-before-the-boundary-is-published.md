---
id: TASK-244
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: unassigned
created: 2026-08-18
depends-on: []
blocks: [TASK-243]
related: [TASK-240, TASK-242, TASK-243]
findings: []
pr: ""
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# Lazy schema-ensure runs before the store publishes its transaction boundary

## The ordering

Every SQL store's public CRUD wrapper is shaped:

```csharp
public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null)
{
    EnsureInitialized();          // <- schema-ensure happens HERE
    CreateCore(data, storeDelegate);   // <- the Core override publishes the boundary HERE
}
```

`EnterTransactionScope()` lives inside `*Core` (and, for the provider overrides, was only put there by
[[TASK-242]]). So a store's lazy `CREATE TABLE` **never** runs inside the boundary published from
`TransactionContext`, and — where the caller installed the ambient itself, e.g. through `SqlUnitOfWork` —
it **always** does, because the ambient travels with the flow rather than with the store.

Two different orderings for the same operation depending on which door the caller used. Neither is
obviously the intended one, and nothing states which it is.

## Why this is filed separately from TASK-243

[[TASK-243]] is the *measured harm*: on MySQL the DDL implicitly commits the open transaction, so the
boundary is silently lost. That is a defect with a live blast radius and its own acceptance.

This task is the *ordering* underneath it, stated provider-independently. It is currently harmless on
PostgreSQL, SQL Server and SQLite (transactional DDL, or no concurrent second connection involved), and it
is the thing a fix for TASK-243 will have to settle — hence `blocks: [TASK-243]`. Splitting it keeps the
MySQL fix from quietly becoming a redesign of store initialisation.

## Questions to settle

- **Should schema-ensure ever participate in a caller's transaction?** Argument for: a create-then-write
  inside one boundary is atomic. Argument against: schema is not the caller's unit of work, and a rollback
  that also un-creates the table makes the *next* request pay for it again.
- **If not, how does it stay off the boundary without opening a second connection?** On SQLite that is
  exactly the deadlock TASK-242 exists to remove. "Run it outside" cannot mean "run it on another
  connection" on every provider.
- **Is `EnsureInitialized` in the public wrapper even the right place** now that `*Core` owns the scope?
  Moving the scope up into the wrapper (before `EnsureInitialized`) is the one-line alternative and gives
  the opposite answer — worth costing, since it makes both doors agree.

## Acceptance

- One stated, documented answer for whether schema-ensure participates, applied identically to the
  ambient door and the `SetTransactionContext` door.
- A test per provider pinning the chosen ordering — including SQLite, where the wrong choice deadlocks
  rather than misbehaves.
