---
id: TASK-217
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-15
depends-on: []
blocks: []
related: [TASK-216]
pr: null
github-issue: null
jira-key: null
findings: []
---

# `Update(Table, values, conditions)` builds its SET list from every column, so a partial update cannot work

## Context

Found by [[TASK-216]]'s own regression test (2026-08-15), which called this overload the obvious way — update
one column — and hit an unrelated failure. Filed rather than absorbed: different mechanism, different fix.

`AbstractConnector_Update.cs:110` (and its async twin) builds the SET list from **all** of the table's
columns while the parameters come from the caller's `values` dictionary:

```csharp
public void Update(Tables.Table table, IDictionary<string, object> values, IEnumerable<Condition>? conditions = null)
{
    IDictionary<int, string> fields = table.GetSelectFields();   // every column
    Update(tableName, fields, values, conditions);               // values = the caller's subset
}
```

So updating one column of a four-column table emits four assignments and binds one parameter:

```sql
UPDATE "TpPeople" SET Name= @SETName, Score= '99', Guid= @SETGuid WHERE Score = '1'
```

`@SETName` and `@SETGuid` are never bound. **The overload can only work when `values` covers every column**,
which no caller signature suggests and which makes the `values` parameter pointless.

**Measured on both providers, because the failure mode decides the severity:**

| Provider | Result |
|---|---|
| SQLite | throws; the row is **unchanged** (measured: `name=a score=1` after an update to `99`) |
| PostgreSQL | throws — `42703: column "setname" does not exist`, the unbound placeholder parsed as an identifier |

So it is **loud everywhere and loses no data** — that is why this is P2 and not higher. The initial guess was
that an unbound parameter would bind as NULL and silently blank the other columns; measuring says otherwise,
and the guess would have justified a much bigger fix.

**Nothing in the framework reaches it.** Both store paths (`DataBaseBulkStore.UpdateInternal` and the async
twin) build `fields` and `values` from the *same* `PropertyUpdate<T>` assignments, so they are consistent by
construction and never hit this. It is reachable only by a consumer calling the connector overload directly —
which is a supported, public API.

## Approach

The two halves have to come from one source; today they come from two. Options:

1. **Derive the SET list from `values`.** `fields` becomes the keys of `values`, resolved against the table's
   metadata (so an unmapped key is refused rather than interpolated — the § Conventions identifier rule).
   Makes a partial update work, which is what the signature already promises.
2. **Refuse a `values` set that does not cover every column.** Keeps today's semantics and makes them
   honest. Cheaper, but leaves a whole-row-only API that the caller must build a full dictionary for, and
   `GetSelectFields()` includes the primary key, so the caller would have to assign `Guid` to itself.

Option 1 looks right — the parameter is called `values`, the store path already works this way, and it is
what any reader would expect. Check both overloads (sync + async) and whether anything depends on the
all-columns behaviour before changing it.

## Acceptance criteria

- [ ] A partial `values` dictionary updates exactly those columns and leaves the rest untouched, verified
      end-to-end on SQLite **and** on live PostgreSQL (the recipe is in TASK-211's § Measured)
- [ ] An unmapped key in `values` is refused with a message naming the key and the entity, rather than
      reaching the statement — the identifier rule in `CLAUDE.md` § Conventions
- [ ] The async twin is fixed with the same change and has its own test
- [ ] Red-verified; split as numbers, contract pins named as pins
- [ ] Full SQL suite sweep

## Out of scope

- The qualifier stripping in `AddRequiredWhere` — [[TASK-216]] owns it and this task must not regress it.
- The `isExpressionValues` path, which builds its own SET fragments and is not affected.

## Human test plan

N/A — an update either changes the intended columns and leaves the others alone or it does not, which an
automated test observes directly on both providers.

## Implementation plan

_Populated by `/tasks plan TASK-217` — leave empty until then._
