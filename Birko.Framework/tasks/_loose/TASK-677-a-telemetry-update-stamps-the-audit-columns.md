---
id: TASK-677
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: ai
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-050]
pr: null
github-issue: null
jira-key: null
---

# A counter update through the decorator chain stamps `UpdatedBy` / `UpdatedAt`, so an anonymous hit erases who last edited the row

## Context

Found in the field by Symbio, in the close-gate review of its TASK-774 (2026-09-26). Recorded in Symbio
`tasks/EPIC-024-module-feature-gaps/STORY-079-storefront-readiness-flowerfurstudio/TASK-814-redirect-hit-counter-vs-staff-edits.md`,
which says: "the mechanism is a framework change — for example a way to mark columns or updates as not
audited in Birko's decorators. File that in `Framework/Birko.Framework/tasks` rather than bypassing the
decorators here, because bypassing them also skips tenant and soft delete."

**What happens.** Symbio counts redirect hits with one `IncrementWhereAsync` per anonymous request, a
`PropertyUpdate<T>` `Increment` through the whole decorator chain. The chain has to stay: tenant scoping
and soft delete must apply. But two wrappers also stamp every filter update unconditionally:

- `AsyncAuditBulkStoreWrapper.UpdateAsync(filter, PropertyUpdate<T>)`
  (`Birko.Data.Patterns/Decorators/AsyncAuditBulkStoreWrapper.cs:64-69`) adds
  `.Set(x => x.UpdatedBy, _auditContext.CurrentUserGuid)`. For an anonymous request that is `null`.
- The Timestamp wrapper (`AsyncTimestampBulkStoreWrapper`, and the sync twins) stamps `UpdatedAt` the same way.

So a crawler hitting a redirect overwrites "who last changed this redirect and when" with "nobody, just
now". The counter itself is exact. Only the audit trail is lost.

**This applies to any write that is not an edit**: view counters, last-seen timestamps, hit and download
counts, a background job's progress column. Today the only way past the stamp is to bypass the
decorators, and that also skips tenant scoping and soft delete.

## Acceptance criteria

- [ ] Decide the mechanism and record why. Options include:
      - a flag on the `PropertyUpdate<T>` (e.g. "not an edit"), honoured by the Audit and Timestamp wrappers;
      - a per-property declaration that a column is telemetry, so updates touching only such columns are not stamped;
      - a scoped ambient suppression.
      The choice must keep tenant scoping and soft delete applied. Per rule 55, a declaration the
      wrappers do not honour must be refused, never silently ignored.
- [ ] The Audit and Timestamp bulk wrappers, async and sync, honour it on the filter-based
      `Update(filter, PropertyUpdate<T>)`. Measured: decide whether the entity-based `Update(T)` needs it too.
- [ ] Tests: an exempt increment leaves `UpdatedBy` / `UpdatedAt` unchanged and still bumps the
      counter. The same update through a tenant-scoped chain still cannot touch another tenant's row.
      A normal update is still stamped. Each test goes red when its half is reverted.
- [ ] Docs (`Birko.Data.Patterns/CLAUDE.md`, `docs/`) and a Recent Updates entry. The entry names
      Symbio TASK-814 as able to adopt it.

## Out of scope

- Symbio's second problem in TASK-814: a staff `PUT` writing back a stale `HitCount`. That is Symbio's
  read-modify-write in `RedirectService`, fixed there.
- Rate limiting or sampling hits.

## Human test plan

N/A — covered by automated tests.

## Implementation plan

_Populated by `/tasks plan TASK-677` — leave empty until then._
