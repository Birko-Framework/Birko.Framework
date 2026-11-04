---
id: TASK-712
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-07]
pr: null
github-issue: null
jira-key: null
---

# Fluent model maps: a unique flag on a nullable column enforces nothing, and some declarations are dropped

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `ModelMapRegistry` sets `IsUnique` after `LoadIndexes`, so a fluent `IsUnique` on a nullable column creates no `ux_` index and no inline UNIQUE. Flags on a property with no mapped field are dropped silently (rule 55). The duplicate-index refusal works per descriptor while `ModelMap.Property()` always adds a new one, so it can be bypassed. `RegisterPrecision(null, null)` cannot unregister.

Evidence: Birko.Models.SQL/Mapping/ModelMapRegistry.cs:130,142; DataBase_Table.cs:552; ModelMap.cs:42-48; FieldBuilder.cs:103; DataBase_Field.cs:74-77.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-04, before the fix)

`tests/Birko.Models.SQL.Tests/Task712FluentFlagTests.cs` against the shipped registry.

| Claim | Result |
|---|---|
| A fluent `IsUnique` on a nullable column creates no `ux_` index and no inline UNIQUE | **reproduced:** no index on the loaded table, and on SQLite a second `"a@x"` was taken |
| Flags on a property with no mapped field are dropped silently | **reproduced** for `IsUnique`, `IsRequired`, `IsPrimary` and `HasColumnName` on a get-only or `[IgnoreField]` property, and for `IsUnique` on a property the same map ignores |
| The duplicate-index refusal can be bypassed through a second `Property()` | **reproduced:** two descriptors for one member, no refusal |
| `RegisterPrecision(null, null)` cannot unregister | **reproduced:** precision stayed 8 instead of the attribute's 12 |

Also found: `Ignore(x => x.P)` / `IsIgnored()` was never applied, so the property stayed a column (no consumer uses it:
grepped Birko\Consumers).

## Implementation plan

As TASK-691 did for lengths: `DataBase.RegisterColumnFlags(type, property, unique, required, ignored)` appends
`UniqueField` / `RequiredField` / `IgnoreField` at load, so the attribute path builds the nullable-unique index and drops
the ignored property; the registry registers them before any table loads and no longer writes `IsUnique` / `IsNotNull`
onto the loaded field. A declaration on a property that is no column is refused (rule 55), as `HasMaxLength` already
was. `Property()` reuses the member's descriptor. `RegisterPrecision(…, null, null)` removes a registration.

## Built (2026-11-04)

11 tests, all failing on the old code. `FluentMaxLengthTests.A_type_without_a_fluent_length_keeps_its_fields` declared
`IsRequired`, which is now registered before the load and so rebuilds the fields once, as a length does; it declares an
index instead. The consumer maps (FisData, WorkoutTracker) declare `HasPrimary` / `HasUnique` on `Guid` and lengths:
nothing newly refused, same DDL. 29 suites importing Birko.Models.SQL green. Spec `schema-index-and-ddl.md` updated
(scoped; `generated-at` left).
