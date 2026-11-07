---
id: TASK-836
parent: EPIC-014
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: [TASK-833]
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-051]
pr: null
github-issue: null
jira-key: null
---

# `AddParameter` scans the parameter collection on every add, so rendering an IN list is quadratic

## Context

This is TASK-833's Step E. The user set a threshold: spawn this task when rendering exceeds ~20% of a read. It does.

**Measured in Release on SQLite (`Task832InListCostTests`, after TASK-833):**

| ids | render (whole list) | read (chunked) |
|---|---|---|
| 1000 | 16 ms | 44–50 ms |
| 4000 | 199–205 ms | 112–224 ms |

- At one 1000-value chunk, rendering is about a third of the read.
- **Cause:** `AbstractConnectorBase.AddParameter` checks `command.Parameters.Contains(name)` before each add, and
  that is a linear scan on SqliteParameterCollection and SqlParameterCollection.
- Names from `SqlBuilderContext.GenerateParameterName` are unique by construction, because their `_{count}` suffix
  strictly increases.
- `AddParameter` is overridden in all 4 provider connectors, so a fast path needs rule 12's shape: a non-virtual
  `AddParameter` wrapper, with the per-provider value conversion behind a virtual.

## Acceptance criteria

- [ ] Rendering a 4000-value IN list grows linearly: each doubling costs ≤ ~2.5×, measured with the
      `Task832InListCostTests` stage table before and after.
- [ ] A name that is added twice still overwrites its value rather than adding a duplicate, pinned by a test, for
      the callers that rely on it (insert / update `SET`).
- [ ] The provider overrides keep their conversions; every SQL importer suite stays green.

## Out of scope

- Chunking (TASK-833).

## Human test plan

N/A — fully covered by automated tests: a render-cost measurement and a duplicate-name test.

## Implementation plan
