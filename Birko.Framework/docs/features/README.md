# Features — Birko.Framework

_Generated 2026-08-17. **Do not hand-edit** — re-run `/feature status`. The human entry point to the
stakeholder-facing feature tree; each row links to a feature folder._

**This whole tree was backfilled on 2026-08-01** from the pre-existing `tasks/` epics, to close the
[[roadmap]] DV5 gap: this repo's `CLAUDE.md` commits to a family-wide `docs/features/` tree
("Cross-cutting `docs/features/` and `docs/specs/` follow the same split") and only `docs/specs/` had
been built. One feature per epic, ids aligned (`FEATURE-0NN` ↔ `EPIC-0NN`) so the join is unambiguous.
Each feature's `decisions.md` § History log states exactly what its backfilled rows do and do not claim.

> ⚠ **Awaiting sign-off (8):** FEATURE-001 (5), FEATURE-016 (2), FEATURE-014 (1) — these are verification
> debt; finish them before new scope. **FEATURE-018 also reads `review` but is not in this count**: all four
> of its tasks were individually closed with their own tests, so what is outstanding there is a
> *feature-level close-out*, not unverified code. It says `review` because `done` means signed off and this
> repo allows no "done pending" hybrid. **Three of them cannot be cleared here at all**, because they need a
> surface this repo does not contain: FEATURE-016 TASK-135 wants a real comma-keypad phone (the fault is a
> keyboard refusing a character, which no headless run reproduces), FEATURE-001 TASK-136 wants Symbio's
> tax-rate edit path, and FEATURE-014 TASK-118 wants a sign-in-protected application to correlate the
> tenant header against a real JWT claim.
>
> One more sits in `tasks/_loose/` and so appears in no row below: **TASK-201**, a Reps-origin defect whose
> fix is upstream. Its former companion **TASK-204** — a **P0** — was re-homed to FEATURE-014 on 2026-08-17
> and has since been signed off; it had been invisible here for five days. See *Not covered by this tree*.
>
> 💭 **Awaiting a decision (1):** FEATURE-001 D8 — whether `b-form` starts rejecting more kinds of invalid
> input (`typeMismatch` first). Run `/feature decide FEATURE-001`; [[TASK-134]] gathers the evidence and must
> not adopt a flag before the row is settled.

## Features

| Feature | Title | Phase | Decisions (a/c/d/r/p) | Tasks | Prototype | Source |
|---------|-------|-------|-----------------------|-------|-----------|--------|
| [FEATURE-001](FEATURE-001-web-components-ui-polish/) | Birko.Web.Components — UI polish | building | 7/0/0/0/**1** | 4/13 | n/a (backfilled) | [EPIC-001](../../tasks/EPIC-001-web-components-ui-polish/EPIC.md) |
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
| [FEATURE-014](FEATURE-014-code-review-remediation/) | Code review — audit remediation | building | 11/0/0/0/0 | 43/100 | n/a (backfilled) | [EPIC-014](../../tasks/EPIC-014-code-review-remediation/EPIC.md) |
| [FEATURE-015](FEATURE-015-birko-xaml-ui-framework/) | Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web | building | 12/0/0/0/0 | 10/22 | n/a (backfilled) | [EPIC-015](../../tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md) |
| [FEATURE-016](FEATURE-016-birko-backports-from-reps/) | Birko framework backports from Reps (+ cross-provider & Xaml follow-ups) | **review** | 6/0/0/0/0 | 12/14 | n/a (backfilled) | [EPIC-016](../../tasks/EPIC-016-birko-backports-from-reps/EPIC.md) |
| [FEATURE-017](FEATURE-017-tenant-isolation-hardening/) | Tenant isolation hardening | building | 3/0/0/0/0 | 0/1 | n/a (backfilled) | [EPIC-017](../../tasks/EPIC-017-tenant-isolation-hardening/EPIC.md) |
| [FEATURE-018](FEATURE-018-birko-web-core-runtime/) | Birko.Web.Core — the browser-side runtime | **review** | 1/0/0/0/0 | 4/4 | n/a (already ships) | [EPIC-018](../../tasks/EPIC-018-birko-web-core-runtime/EPIC.md) |

Phase ∈ idea · prototyping · deciding · building · review · done · dropped · superseded.
Decisions column = approved/changed/deferred/removed/proposed counts.
Tasks = done/total of tasks carrying `feature: FEATURE-NNN`.

## Not covered by this tree

**Seventeen** tasks in `tasks/_loose/` carry `parent: null` and `feature: null`, so none of them appears
in any row above. It was 6 on 2026-08-01 and peaked at 33; **12 were re-homed to
[EPIC-014](../../tasks/EPIC-014-code-review-remediation/EPIC.md) / FEATURE-014 on 2026-08-17** — framework
Data/SQL defects that were already being worked under that feature's remit and merely sat where it could
not see them. That was not cosmetic: **TASK-204 was a P0 in `review`** and appeared in no rollup and no
sign-off callout, which is how it sat unnoticed for five days.

**The remaining 17 are not a backlog to drain — most belong here.** Four groups:

- **Framework-wide *decision* tickets** (TASK-059, TASK-106, TASK-127, TASK-139) — questions owned by no
  single epic, which deliberately declare no parent so the dashboard does not render them twice.
- **Consumer-origin defects** (TASK-200 Symbio, TASK-201 Reps, TASK-235 FisData) — found *in* a consumer,
  tracked here because the fix or the warning is upstream. Not framework scope.
- **Process and tooling** (TASK-036 workspace layout, TASK-142 spec-map coverage, TASK-149 story
  visibility, TASK-138 an API-ergonomics papercut recorded as deliberately out-of-feature).
- **One theme/token defect** (TASK-130) — colour-contrast gating that spans `Birko.Web.Components` **and**
  `Birko.Xaml.Avalonia`, so it belongs to FEATURE-001 or FEATURE-015 and not to both. Still deliberately
  unmoved: a wrong home is less visible than no home.

> **Resolved 2026-08-17 — the four `Birko.Web.Core` defects had no home because the runtime had no epic.**
> TASK-198, 199, 202 and 203 were expected to fit EPIC-016's STORY-052 or EPIC-001. Neither works:
> STORY-052 is explicitly about `b-*` catalogue components (*"the fix lives in the component or it is not a
> fix"*) and an HTTP timeout is not a component fix, while EPIC-001's six stories are all component work.
> `Birko.Web.Core` appears in **four** epics' `affects:` lists and none of them is *about* it — being named
> as affected is not ownership. [EPIC-018](../../tasks/EPIC-018-birko-web-core-runtime/EPIC.md) and
> FEATURE-018 were created for it and the four moved there.
>
> The absence had a measurable cost: **two of the four are tracking backfills** for fixes that shipped in
> `Birko.Web.Core` with no aggregator commit, no task and no spec regen, found only by diffing the sibling
> repository's `git log` against this one. Work with nowhere to be filed ships untracked.

> **The audit rule cannot tell these apart.** [[roadmap]]'s DV5 flags any parentless task, and the
> rationale for a deliberate one lives in prose inside the task body — or, for TASK-138, in a feature
> ledger entry rather than the task at all. There is no machine-readable marker, so DV5 will keep counting
> the legitimate ones. Giving it one is a change to the shared skill, recorded here rather than improvised.

> **Resolved 2026-08-16:** **TASK-205** was checked against **TASK-211** criterion by criterion and
> **cancelled** — same defect, and all six of its acceptance rows are satisfied by TASK-211 (read path)
> and TASK-216 (write path). It was filed 2026-08-12 as an *inferred, unmeasured* claim; the measured
> version arrived three days later as a separate `_loose` task and neither recognised the other. Its
> § Disposition records why, because an inferred task is the shape most likely to be silently re-filed
> from evidence later.
