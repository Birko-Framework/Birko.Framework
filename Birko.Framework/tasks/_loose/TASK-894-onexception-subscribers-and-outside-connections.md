---
id: TASK-894
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-09
depends-on: [TASK-875]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `OnException` subscribers may never run; statements outside the connector are not announced

## Context

Found by TASK-875 (2026-11-09), by reading the code; not reproduced:
- **Consumer `OnException` subscribers never run:** each provider registers its own handler first in its constructor,
  and that handler always throws, so a later subscriber is not reached (rule 25).
- **Statements on connections outside the connector are not announced to `OnExecute`:** `Birko.Data.Migrations.SQL`,
  `Migrations.TimescaleDB`, `SQL.View.Migrations` and `BackgroundJobs.SQL`'s lock provider open their own connections.
  They need an announce entry point those projects can reach.

## Acceptance criteria

- [ ] Reproduce each (rule 54).
- [ ] Every `OnException` subscriber runs, whatever the provider's own handler does; the outside statements are
      announced through `AnnounceStatement`/`AnnounceCommand`, or each gap is recorded with its reason.
- [ ] Tests, red before the fix.

## Out of scope

- The connector's own paths (TASK-875, done).

## Human test plan

N/A — fully covered by automated tests.

## Implementation plan
