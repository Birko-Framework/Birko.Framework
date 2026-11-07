---
id: TASK-840
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P2
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A Cosmos DB view filter on a nested property reads the wrong field

## Context

Found while probing TASK-839 (2026-11-07), from reading the code; not yet reproduced. The user chose to file it as
its own task.

- `CosmosFilterTranslator.TranslateFieldAccess` (Birko.Data.CosmosDB.Views) renders `v.Inner.X` as `c.X`, keeping
  only the last member name.
- A filter on a nested property therefore compares the wrong field, which is usually missing on the document, so it
  matches nothing, or it matches a same-named top-level field. Either way the answer is wrong and nothing is raised
  (rules 41–44).

## Acceptance criteria

- [x] Reproduced first on the live emulator: a view with a nested property, filtered on it, returns the wrong rows
      today. Record what it returns.
      → Reproduced on vnext-preview, 2026-11-07. The documents carry a top-level `X = 7 - i` beside `Sub.X = i`, and the
        view maps `Sub` to `Child`.
        - `v.Child.X == 2` rendered `c.X = @p0` and returned the row whose `Sub.X` is 5 (its top-level `X` is 2). The
          oracle gives `Sub.X` 2.
        - `v.Child.Name` and `v.Child.Deep.Level` returned nothing.
        - `v.Maybe.Value == 6` rendered `c.Value`, a 400.
        - The base store's LINQ renders `["Sub"]["X"]`.
- [x] A nested path renders in full (`c.Inner.X`, through `MapViewPropertyToSource` where the view maps it), or a
      nested path that cannot be mapped refuses by name before any request. The read equals the C# oracle.
      → `DocumentPath` renders `c.Sub.X`. Only the first segment is mapped: the projection selects the source field
        whole (`c["Sub"] AS Child`), and `Select<TSource,TProp>` keeps the type, so deeper names are the same.
        `Nullable.Value` is read as the value.
      → Refused by name before any request: string `Length`, `HasValue`, `Count`, a struct's member, a chain not
        rooted at the view, and a negation like `-v.X`, which used to render as the bare field.
      → TASK-839's refusal of `Contains` on a nested member is lifted, since it shares the path. Both forms are tested.
- [x] The regression test is red before the fix.
      → `Task840CosmosViewNestedPathTests` (19 tests): with `HEAD`'s `CosmosViewStore.cs`, 18 of 19 fail (only the
        LINQ comparison probe passes). Live suites green on the combined tree: CosmosDB.Views 90, CosmosDB 192.
      → **Found and filed:** `ViewDefinitionBuilder.GetMemberName` flattens a nested projection the same way →
        TASK-843 (silent, likely every provider). An unquoted alias named like a reserved word gives a 400 →
        TASK-844.
      → The test view's property is `Child`, not `Inner`, because of TASK-844.

## Out of scope

- `Contains` support (TASK-839).

## Human test plan

N/A — fully covered by automated tests against the emulator.

## Implementation plan
