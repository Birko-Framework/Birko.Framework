# Features — Birko.Framework

_Generated 2026-08-01. **Do not hand-edit** — re-run `/feature status`. The human entry point to the
stakeholder-facing feature tree; each row links to a feature folder._

**This whole tree was backfilled on 2026-08-01** from the pre-existing `tasks/` epics, to close the
[[roadmap]] DV5 gap: this repo's `CLAUDE.md` commits to a family-wide `docs/features/` tree
("Cross-cutting `docs/features/` and `docs/specs/` follow the same split") and only `docs/specs/` had
been built. One feature per epic, ids aligned (`FEATURE-0NN` ↔ `EPIC-0NN`) so the join is unambiguous.
Each feature's `decisions.md` § History log states exactly what its backfilled rows do and do not claim.

> ⚠ **Awaiting sign-off (8):** FEATURE-001 (4), FEATURE-016 (4) — these are verification debt; finish them before new scope.

## Features

| Feature | Title | Phase | Decisions (a/c/d/r/p) | Tasks | Prototype | Source |
|---------|-------|-------|-----------------------|-------|-----------|--------|
| [FEATURE-001](FEATURE-001-web-components-ui-polish/) | Birko.Web.Components — UI polish | building | 7/0/0/0/0 | 4/9 | n/a (backfilled) | [EPIC-001](../../tasks/EPIC-001-web-components-ui-polish/EPIC.md) |
| [FEATURE-002](FEATURE-002-birko-data-redis/) | Birko.Data.Redis | idea | 1/0/0/0/0 | 0/1 | n/a (backfilled) | [EPIC-002](../../tasks/EPIC-002-birko-data-redis/EPIC.md) |
| [FEATURE-003](FEATURE-003-birko-caching-ncache/) | Birko.Caching.NCache | idea | 1/0/0/0/0 | 0/1 | n/a (backfilled) | [EPIC-003](../../tasks/EPIC-003-birko-caching-ncache/EPIC.md) |
| [FEATURE-004](FEATURE-004-storage-cloud-providers/) | Birko.Storage — Cloud providers | idea | 3/0/0/0/0 | 0/3 | n/a (backfilled) | [EPIC-004](../../tasks/EPIC-004-storage-cloud-providers/EPIC.md) |
| [FEATURE-005](FEATURE-005-messaging-provider-expansion/) | Birko.Messaging — Provider expansion | idea | 3/0/0/0/0 | 0/5 | n/a (backfilled) | [EPIC-005](../../tasks/EPIC-005-messaging-provider-expansion/EPIC.md) |
| [FEATURE-006](FEATURE-006-messagequeue-provider-expansion/) | Birko.MessageQueue — Provider expansion | idea | 4/0/0/0/0 | 0/5 | n/a (backfilled) | [EPIC-006](../../tasks/EPIC-006-messagequeue-provider-expansion/EPIC.md) |
| [FEATURE-007](FEATURE-007-telemetry-exporters/) | Birko.Telemetry — Additional exporters | idea | 3/0/0/0/0 | 0/3 | n/a (backfilled) | [EPIC-007](../../tasks/EPIC-007-telemetry-exporters/EPIC.md) |
| [FEATURE-008](FEATURE-008-health-mq-cloud-checks/) | Birko.Health — Queue + cloud health checks | idea | 2/0/0/0/0 | 0/4 | n/a (backfilled) | [EPIC-008](../../tasks/EPIC-008-health-mq-cloud-checks/EPIC.md) |
| [FEATURE-009](FEATURE-009-communication-protocols/) | Birko.Communication — Remaining protocols | done | 2/0/0/0/0 | 2/2 | n/a (backfilled) | [EPIC-009](../../tasks/EPIC-009-communication-protocols/EPIC.md) |
| [FEATURE-010](FEATURE-010-ravendb-index-ergonomics/) | Birko.Data.RavenDB — Index ergonomics | idea | 1/0/0/0/0 | 0/1 | n/a (backfilled) | [EPIC-010](../../tasks/EPIC-010-ravendb-index-ergonomics/EPIC.md) |
| [FEATURE-011](FEATURE-011-test-coverage-gaps/) | Birko.Framework — Test coverage gaps | idea | 4/0/0/0/0 | 0/7 | n/a (backfilled) | [EPIC-011](../../tasks/EPIC-011-test-coverage-gaps/EPIC.md) |
| [FEATURE-012](FEATURE-012-mqtt-v5-features/) | Birko.MessageQueue.MQTT — v5 features | idea | 1/0/0/0/0 | 0/1 | n/a (backfilled) | [EPIC-012](../../tasks/EPIC-012-mqtt-v5-features/EPIC.md) |
| [FEATURE-013](FEATURE-013-reference-consumers/) | Reference consumers — integration smoke harness + Web playground | building | 1/0/0/0/0 | 1/2 | n/a (backfilled) | [EPIC-013](../../tasks/EPIC-013-reference-consumers/EPIC.md) |
| [FEATURE-014](FEATURE-014-code-review-remediation/) | Code review — audit remediation | building | 11/0/0/0/0 | 5/16 | n/a (backfilled) | [EPIC-014](../../tasks/EPIC-014-code-review-remediation/EPIC.md) |
| [FEATURE-015](FEATURE-015-birko-xaml-ui-framework/) | Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web | building | 12/0/0/0/0 | 10/22 | n/a (backfilled) | [EPIC-015](../../tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md) |
| [FEATURE-016](FEATURE-016-birko-backports-from-reps/) | Birko framework backports from Reps (+ cross-provider & Xaml follow-ups) | building | 6/0/0/0/0 | 9/13 | n/a (backfilled) | [EPIC-016](../../tasks/EPIC-016-birko-backports-from-reps/EPIC.md) |
| [FEATURE-017](FEATURE-017-tenant-isolation-hardening/) | Tenant isolation hardening | building | 3/0/0/0/0 | 0/0 | n/a (backfilled) | [EPIC-017](../../tasks/EPIC-017-tenant-isolation-hardening/EPIC.md) |

Phase ∈ idea · prototyping · deciding · building · review · done · dropped · superseded.
Decisions column = approved/changed/deferred/removed/proposed counts.
Tasks = done/total of tasks carrying `feature: FEATURE-NNN`.

## Not covered by this tree

Six tasks in `tasks/_loose/` carry `parent: null` and therefore no feature: **TASK-036**, **TASK-058**,
**TASK-059**, **TASK-106**, **TASK-127**, **TASK-130**. Five are framework-wide *decision* tickets that
belong to no single epic (and deliberately declare no parent, so the dashboard does not render them
twice); TASK-130 spans the token/theme layer that no epic covers. They are tracked, just not
feature-homed — a real gap rather than an oversight, and the honest place to record it is here.
