---
id: TASK-689
parent: null
feature: null
status: todo
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

- [ ] `ignore:` reaches the whole repo, and the header's test-path note is corrected.
- [ ] The declared out-of-scope families and per-backend store bodies are either expressed as globs or left unmapped on
      purpose, and the map says which.
- [ ] Each of the 207 files is sorted into an area or `ignore:`, and the files that moved into areas are listed as
      `coverage-drift:`.
- [ ] The map carries `coverage:`, `tracked-files-at-scan:` and `coverage-drift:` (via `/specs init`, with the map
      blessed again).

## Out of scope

- Creating the per-project spec trees (TASK-226).
