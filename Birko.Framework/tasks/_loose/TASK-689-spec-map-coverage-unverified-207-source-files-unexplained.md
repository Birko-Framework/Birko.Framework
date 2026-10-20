---
id: TASK-689
parent: null
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-20
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The spec map was never coverage-checked: 207 source files are unexplained and `ignore:` matches nothing

## Context

Found by `/adopt-project`'s `/specs init` delegation (2026-10-20). `docs/specs/.map.yml` has no `coverage:` key, so its
25 areas have never been checked against a scan. Measured with every git-tracked file as the scan set and each glob
matched as a `:(glob)` pathspec from this directory:

- 4 913 files are tracked and 778 are mapped. Of the 4 127 unmapped, 3 240 are housekeeping (tests, aggregator docs and
  tasks, `.md`, project files) and 680 are in the project families the header declares out of scope.
- **207 source files in 38 projects are unexplained.** Most are per-backend store bodies, which the header excludes in
  prose but no glob expresses (JSON 20, XML 19, ElasticSearch 10, the ViewModels). The rest may be real drift:
  Birko.Data.SQL 36, the SQL providers 10–11 each, Birko.Models 15, Security.Grants.AspNetCore 6, Data.Stores 3,
  Data.Core 2, EventBus.Outbox.SQL 2.
- **`ignore:` matches 0 files.** Its entries resolve from the aggregator directory, so they reach `Birko.Framework/`
  only, and the header's note about test code at `../../tests/` names the wrong path (`../tests/`).

The user deferred all three map changes to this task instead of making them during adoption. Related: TASK-142 (no
coverage detection) and TASK-226 (per-project spec trees for the out-of-scope families).

## Acceptance criteria

- [x] `ignore:` reaches the whole repo, and the header's test-path note is corrected.
- [x] The declared out-of-scope families and per-backend store bodies are either expressed as globs or left unmapped on
      purpose, and the map says which.
- [x] Each of the 207 files is sorted into an area or `ignore:`, and the files that moved into areas are listed as
      `coverage-drift:`.
- [x] The map carries `coverage:`, `tracked-files-at-scan:` and `coverage-drift:` (via `/specs init`, with the map
      blessed again).

## Out of scope

- Creating the per-project spec trees (TASK-226).

## Outcome

Done 2026-10-20. The map was blessed by the user and written as an edit; no header prose was re-rendered.

**Measured first.**
- 4 947 tracked files: 787 mapped, 0 ignored, 4 150 unmapped.
- `ignore:` resolves from `Birko.Framework/` like `sources:`, so its 7 entries matched nothing. They are now written
  with `../` and reach the whole repo.
- The test-path note is corrected to `../tests/`.

**Every out-of-scope family was triaged, not assumed:**
- commits since 2026-09-01, where 2 is the monorepo migration and so means dormant;
- the tasks that name each family;
- the area types it references.

Two read-only agents read the ambiguous families: EventSourcing, Telemetry, Messaging, Localization, the OAuth server
and the server endpoints.

**Decided by the user:**
- **The per-backend store bodies.** They were the most-changed excluded projects, 10–30 commits each. A new area,
  `backend-store-implementations`, holds all 12 backends' `Stores/` and `Repositories/`, specced side by side. The
  2026-08-08 revert is recorded as superseded on new evidence, not deleted.
- **Joined an existing area:**
  - AI.Providers, Agents, Orchestration and Resilience → `llm-provider-and-agents`;
  - Models.SQL, the connectors' DDL / drift / repair and the drift health checks → `schema-index-and-ddl`;
  - the whole of SQL.View and the provider `*.View` projects → `views-and-aggregation`;
  - EventSourcing and Telemetry's instrumented wrappers → `store-decorator-composition`;
  - the transport auth adapters (REST / SSE / WebSocket / SOAP), `OwnerAccess` / `OwnedCrudResults`, Grants.AspNetCore
    and the stale-grant check → `security-and-authorization`;
  - the remaining drift files → their areas.
- **`ignore:` records each family with its reason:** housekeeping; "own tree" (real behaviour awaiting [[TASK-226]],
  now noted there); or "nothing to spec yet" (dormant, with no contract link). Only 13 backend files stay ignored:
  Mongo change streams, ES highlighting, client wrappers and DI registration.

**Result:**
- `coverage: verified`, `tracked-files-at-scan: 4947`, and `coverage-drift:` lists the 279 files now in an area.
- 26 areas, 1 066 mapped, 3 906 ignored, 0 unmapped.

No spec is regenerated here: [[TASK-696]] does that for the 14 areas that gained files.
