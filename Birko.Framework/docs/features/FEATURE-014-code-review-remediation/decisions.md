---
id: FEATURE-014
created: 2026-06-18
---

# Code review — audit remediation — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Critical findings ([[STORY-024]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D2 | High findings ([[STORY-025]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D3 | Medium findings ([[STORY-026]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-06-18 | ai | — (tracked in prose) |
| D4 | Low findings ([[STORY-027]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D5 | Integration-test tier — the Docker-gated remediation findings ([[STORY-042]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-14 | ai | — (tracked in prose) |
| D6 | Workflow backends — unify the serialization seam (ISerializer everywhere) ([[STORY-043]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-17 | ai | — (tracked in prose) |
| D7 | Spec-harvest — high findings ([[STORY-051]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | [[TASK-108]], [[TASK-109]], [[TASK-110]], [[TASK-111]], [[TASK-112]], [[TASK-113]], [[TASK-114]], [[TASK-115]], [[TASK-116]], [[TASK-117]], [[TASK-118]], [[TASK-125]], [[TASK-126]], [[TASK-128]], [[TASK-129]], [[TASK-137]], [[TASK-141]], [[TASK-207]], [[TASK-209]], [[TASK-212]], [[TASK-213]], [[TASK-214]], [[TASK-215]], [[TASK-218]], [[TASK-219]], [[TASK-220]], [[TASK-221]], [[TASK-222]], [[TASK-223]], [[TASK-224]], [[TASK-225]] |
| D8 | Spec-harvest — medium findings ([[STORY-053]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Decomposed for real on 2026-08-09 — one task per spec area, replacing the on-demand policy that had produced nothing. | 2026-07-30 | ai | [[TASK-151]], [[TASK-152]], [[TASK-153]], [[TASK-154]], [[TASK-155]], [[TASK-156]], [[TASK-157]], [[TASK-158]], [[TASK-159]], [[TASK-160]], [[TASK-161]], [[TASK-162]], [[TASK-163]], [[TASK-164]], [[TASK-165]], [[TASK-166]], [[TASK-167]], [[TASK-168]], [[TASK-169]], [[TASK-170]], [[TASK-171]], [[TASK-172]] |
| D9 | Spec-harvest — low findings ([[STORY-054]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Decomposed for real on 2026-08-09 — one task per spec area. | 2026-07-30 | ai | [[TASK-173]], [[TASK-174]], [[TASK-175]], [[TASK-176]], [[TASK-177]], [[TASK-178]], [[TASK-179]], [[TASK-180]], [[TASK-181]], [[TASK-182]], [[TASK-183]], [[TASK-184]], [[TASK-185]], [[TASK-186]], [[TASK-187]], [[TASK-188]], [[TASK-189]], [[TASK-190]], [[TASK-191]], [[TASK-192]], [[TASK-193]], [[TASK-194]] |
| D10 | Spec-harvest — the three unrated areas ([[STORY-055]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`; its remaining work became one task on 2026-08-09. | 2026-07-30 | ai | [[TASK-195]] |
| D11 | Work tracked directly on the epic, outside any story | approved | Backfilled: these tasks exist and are tracked, so the scope decision was taken. | 2026-06-18 | ai | [[TASK-131]], [[TASK-208]], [[TASK-226]], [[TASK-227]], [[TASK-229]], [[TASK-230]], [[TASK-231]], [[TASK-232]], [[TASK-233]], [[TASK-234]], [[TASK-058]], [[TASK-144]], [[TASK-146]], [[TASK-150]], [[TASK-196]], [[TASK-197]], [[TASK-204]], [[TASK-205]], [[TASK-210]], [[TASK-211]], [[TASK-216]], [[TASK-217]], [[TASK-236]], [[TASK-237]], [[TASK-240]], [[TASK-241]], [[TASK-242]], [[TASK-243]], [[TASK-244]], [[TASK-245]], [[TASK-246]], [[TASK-247]], [[TASK-248]], [[TASK-249]], [[TASK-250]], [[TASK-238]], [[TASK-239]], [[TASK-251]], [[TASK-252]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-014` had been tracked in `tasks/` since 2026-06-18 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
- 2026-08-03 — **D7** — [[TASK-137]] spawned from [[TASK-109]] while planning it: the empty-`NOT IN` → `1 = 1`
  rendering shipped 2026-07-27 is a false SQL-injection signal in query logs, by the same argument that led
  TASK-109 to *reject* `1 = 1` as its all-rows idiom. No state change — D7 is already `approved` and this is
  one more task realizing it, filed under STORY-051 alongside the other two non-`SH-` tasks the remediation
  itself produced ([[TASK-128]], [[TASK-129]]). Blocked on TASK-109, because dropping an always-true term can
  leave a `DELETE` with no `WHERE` and must reach TASK-109's deliberate-all-rows path rather than its refusal.
- 2026-08-03 — Also spawned from TASK-109, but **deliberately outside this feature**: [[TASK-138]]
  (`ReadAsync()` with no arguments does not compile — CS0121). Filed to `tasks/_loose/` with `feature: null`,
  because it is an API-ergonomics papercut rather than code-review remediation. Recorded here so the spawn is
  traceable from the feature the origin belongs to, without widening this feature's scope to cover it.
- 2026-08-06 — **D7** — [[TASK-109]] closed (SH-H002 + SH-M023): a null or untranslatable filter no longer
  renders a whole-table `DELETE`/`UPDATE`. Two findings, one decision — the SQL native paths and the portable
  bases shared the defect, so one policy took two edits. No state change; D7 stays `approved`.
- 2026-08-06 — **D7** — [[TASK-141]] spawned at TASK-109's **close gate**, not during its coding: MongoDB's
  four repeated null-filter guards have no test, while the InMemory half of the same sweep was *discovered*
  by a failing one. Adjacent scope rather than an unmet criterion (criterion 9 named the SQL and portable
  suites), so it is filed rather than folded in. No state change — one more task realizing D7.
- 2026-08-07 — **D7** — [[TASK-116]] closed (SH-H041 + SH-H042 + SH-H043 + SH-H044): a rule leaf that cannot
  be evaluated no longer widens the filter to every row. Four findings, one root cause, plus two unfiled
  sites of the same species pulled in. The fix also moved `Birko.Rules`' in-memory evaluator onto match-none
  for a string operator against a non-string member — a **user decision**, taken because the expression path
  cannot portably stringify a column and the two engines had to converge somewhere. No state change; D7
  stays `approved`.
- 2026-08-07 — **D7** — [[TASK-125]] closed (SH-H036): the ordered `ReadOne` no longer reads around the
  store decorator chain, so it can no longer return another tenant's row. The bypassing extension was
  removed rather than repaired — `Store` is `protected`, so an extension cannot reach the decorated chain
  and the capability is only implementable safely as an instance method. No state change; D7 stays
  `approved`.
- 2026-08-07 — **D7** — [[TASK-126]] closed (SH-H019): `TagServiceBase` now re-checks the tenant of every
  record its data-access hooks return — throwing on a by-identity load, filtering a collection — instead
  of depending on each implementor to filter. Hardening rather than a reproduced leak: the framework ships
  no implementation of the base. No state change; D7 stays `approved`.

- 2026-08-09 — **D8 / D9 / D10** — decomposed by `/tasks intake --epic EPIC-014` into **45 tasks**
  ([[TASK-151]]–[[TASK-195]]): 22 per-area triage tasks for the mediums, 22 for the lows, and one bounded
  task for the recovered unrated set. No state change; all three stay `approved` — this records *how* the
  approved scope is now tracked, not a change to what was approved.

  **Why now.** All three rows read `— (tracked in prose)` and their stories said "extract on demand". In the
  ten days since filing, **zero** extractions happened, and that is structural rather than a lapse: only
  `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot, or by `/fix-next`, so 808
  findings sat where no picker could see them. The `/roadmap` DV12 audit is what surfaced it — the rule
  exists precisely to catch a review that reads as drained while part of it was never scheduled.

  **Why per-area and not per-finding.** Findings in one area share a spec, a source-glob set and often a root
  cause, so they are fixed in one edit — the intake rule for what belongs in one task. 808 individual tasks
  would have been as unusable as none. Per-finding extraction still happens *inside* an area task, via
  `/tasks spawn`, when triage confirms something too large to fix there.

  **A constraint this created.** Each task carries an **explicit contiguous** `findings:` list, because that
  is what `/fix-next` greps to build its pool — a range string would match nothing. Appending new `SH-` ids
  past the current maxima stays safe; renumbering inside an existing range now silently invalidates up to 44
  files at once. [[TASK-195]] carries that constraint as an acceptance criterion.
- 2026-08-16 — **`→ Tasks` backfilled for 15 tasks** filed since 2026-08-11: D7 gains TASK-207, 209,
  212–215 and 218–225; D11 gains TASK-208. Raised by [[roadmap]] as **DV9** — each already carried
  `feature: FEATURE-014` and sat under STORY-051 (or the epic directly), so the work was tracked and the
  ledger simply had not been told. No decision state changed and no new row was needed: all fourteen D7
  additions are spec-harvest high findings, which is exactly what D7 approved. The gap is a `/tasks spawn`
  omission that recurs whenever a fix uncovers its successor mid-run, and it will recur again — the
  ledger's `→ Tasks` column has no writer other than a human remembering to update it.
- 2026-08-16 — **D11 gains [[TASK-226]]**, spawned from [[TASK-131]] and backfilled *at creation* rather
  than by a later audit — the first task to exercise the rule change that closed the gap named in the line
  above (`/tasks new` step 10b now fires on the `feature:` link instead of on `--from-feature`). Inside
  D11's approved scope: epic-direct spec-layer infrastructure, same as TASK-131 itself, so no new decision
  row. **TASK-131 also changed shape without changing state** — it set out to build per-sub-repo spec
  trees and measured that those cover 4 of 25 areas, so it fixed staleness with a per-sibling baseline
  instead and re-homed the per-sub-repo work as TASK-226. The decision D11 approved ("work tracked
  directly on the epic") is unaffected; the *mechanism* inside one of its tasks changed, which is a task
  concern and is recorded there.
- 2026-08-16 — **D11 gains [[TASK-227]]**, spawned while draining the DV7 backlog TASK-131 made visible.
  Backfilled at creation, same as TASK-226. Inside D11's scope (epic-direct spec-layer infrastructure), so
  no new decision row. It records a defect in the **generic** specs skill rather than in this repo:
  `generated-at` is stamped before the spec file is committed, so it always names the preceding commit and
  staleness is measured from too early — true for 25 of 25 areas here, and the reason TASK-131's first
  pass over-reported 15 stale areas where the real count is 6.
- 2026-08-17 — **D11 gains [[TASK-229]] and [[TASK-230]]**, both backfilled at creation, both spawned from
  [[TASK-210]]'s sweep criterion. Inside D11's scope (work tracked directly on the epic) and surfaced by a
  review gate, so no new decision row. TASK-230 carries the 4 remaining advisories across 13 test projects
  plus the sandbox; TASK-229 carries the architectural cause — **2 of 171 shared projects `using` a driver
  they do not declare**, so one advisory class costs one line in MongoDB and ten repos in SQLite.
  A **user decision is recorded in TASK-229**: shared projects declare their own drivers, floating rather
  than pinned, accepting non-reproducible builds in exchange for advisories that self-heal on restore.
  Shipping the backends as real NuGet packages stays **deferred** until the libraries stabilise — TASK-229
  is forward-compatible with it rather than a substitute.
- 2026-08-17 — **D11 gains [[TASK-231]] and [[TASK-232]]**, both backfilled at creation. Inside D11's scope
  (work tracked directly on the epic) and both framework-consistency findings, so no new decision row.
  TASK-231: `Birko.EventBus.Outbox.SQL` is a complete project — own repo, `.shproj`, `IOutboxStore`
  implemented — that is registered in **nothing**: absent from the `.slnx`, the `.code-workspace`, the
  build-validation aggregator (so **no build compiles it**) and with no sibling `.Tests` project. The only
  such project of 341 swept. Raised P1 because unbuilt-and-untested is a different class from
  undocumented, and because the `AddOutbox(storeFactory)` overload added in
  `Birko.EventBus.Outbox@bbd9389` was written *for* this store.
  TASK-232 is a **DECISION** ticket, not a fix: `IJobLockProvider` (`Birko.BackgroundJobs@9b1395d`) is
  correctly implemented by both existing providers, but only **2 of 8** job backends can supply one at all,
  and nothing in the framework consumes the interface yet. What the six should do — and whether a
  TTL *lease* may masquerade as the documented *session*-scoped lock — has to be settled before any code.
- 2026-08-17 — **D11 gains [[TASK-233]]**, backfilled at creation, spawned by [[TASK-229]]'s float. Inside
  D11's scope, no new decision row. Floating `Microsoft.Azure.Cosmos` to `3.*` moved 3.46.1 → 3.62.1 and
  the SDK now translates the span-bound `Contains` natively, rendering byte-identical SQL — so
  [[TASK-220]]'s Cosmos rewrite may be retirable. **Found by the premise-pinning test written for exactly
  this**, whose comment predicted it verbatim. TASK-229 itself closed `done`: all 8 backends declare their
  driver floating, 166/166 projects build clean, and the float cleared 29 of 44 advisory findings with no
  per-project edit — while exposing two further undeclared dependencies (`Birko.Data.Repositories`,
  `Birko.Data.Tenant`) that had been borrowing assemblies transitively from RavenDB.Client.
- 2026-08-17 — **D11 gains [[TASK-234]]**, backfilled at creation. Inside D11's scope, no new decision row.
  [[TASK-229]] settled the declare-your-own-dependency convention and applied it to 10 projects;
  [[TASK-230]] added 3 more, each surfaced by an advisory. TASK-234 carries the remaining **38**, measured
  across all 171 `.projitems` — `NEST` ×7, `MongoDB.Driver` ×5, `StackExchange.Redis` ×5,
  `Microsoft.AspNetCore.App` ×4 and ten others. The count is **refined**: a first pass said 43 by treating a
  namespace root as a package id, which is wrong (`using Raven.*` comes from `RavenDB.Client`). Two of the
  13 already fixed were *found* by a driver bump removing an assembly they had been borrowing, so any of the
  38 may be in the same position with nobody aware.
  Also filed, deliberately **outside** this feature: [[TASK-235]] in `tasks/_loose/` with `feature: null` —
  a courtesy warning that a *consumer* (`FisData.Stock.Angular.Server`) will hit `NETSDK1087` once its net10
  migration builds, because a duplicate `FrameworkReference` is a hard error. Consumer work, blocked on an
  external condition, and recorded here only so the spawn is traceable from the feature its cause belongs to.
- 2026-08-17 — **D11 gains 12 re-homed tasks**: TASK-058, 144, 146, 150, 196, 197, 204, 205, 210, 211, 216
  and 217, moved out of `tasks/_loose/` and re-parented to EPIC-014 / FEATURE-014. Raised by [[roadmap]] as
  **DV5**. No decision state changed and no work moved — these are framework Data/SQL defects that were
  already being worked under this feature's remit; they simply sat parentless, so **this feature could not
  see its own tasks**. That is not cosmetic: TASK-204 was a **P0 in `review`** and TASK-205 a cancelled
  duplicate, and neither appeared in any feature rollup or the sign-off callout — which is precisely how a
  P0 sat unnoticed for five days.
  **The loose pile is not emptied, and should not be.** 21 tasks remain there legitimately: 4 framework-wide
  *decision* tickets that belong to no single epic, 3 consumer-origin defects (Symbio, Reps, FisData), the
  process/tooling items, and 6 `Birko.Web`-surface defects whose right home is EPIC-016's STORY-052 or
  EPIC-001 rather than here — deliberately not moved on a guess. **DV5 cannot distinguish deliberate
  parentlessness from an oversight**, and prose rationales inside task bodies are not machine-readable;
  giving it a marker is a [[roadmap]] skill change, recorded rather than improvised.
- 2026-08-17 — **D11 gains [[TASK-236]] and [[TASK-237]]**, both backfilled at creation and both split out
  of [[TASK-232]], which closed `done`. Inside D11's scope, no new decision row.
  TASK-232 was filed as "which of the six remaining job backends should get a lock provider" and the
  question underneath it turned out to be prior: **the two existing providers did not implement the same
  contract**, while `IJobLockProvider` had been introduced that morning specifically to declare them
  substitutable. One call meant three things — SQL/PostgreSQL ignored `timeout`, SQL/MSSql+MySQL waited on
  it, and Redis used it as the key's expiry, **releasing the lock while the holder was still working**.
  **Three user decisions, 2026-08-17:** split the durations (`acquireTimeout` + `leaseDuration?`); keep
  session semantics *and* expose `IsLeaseBased`, with Redis renewing its lease on a heartbeat; and adopt
  leader election rather than a per-decision lock. The breaking signature change was taken now because the
  interface had **zero consumers** — a window that closes on first adoption.
  TASK-237 carries the leader election, and records why the per-decision alternative was rejected: with a
  lock it cannot work, because a late process finds the lock free and duplicates — *"has this occurrence
  already been enqueued"* is an idempotency question, whose right answer is a unique key on the queue and
  would serve all eight backends rather than the two that can express a lock. TASK-236 carries the six
  missing providers, and flags that this task's own first guess about `JSON`/`XML` may be **backwards**: an
  OS file lock is released by the kernel on process death, which is genuine session semantics — the one
  guarantee none of the document stores can offer.
- 2026-08-18 — **D11 gains [[TASK-246]] and [[TASK-247]]**, both spawned from [[TASK-245]]'s planning
  grill and backfilled at creation. Inside D11's scope, no new decision row. **Also backfills eight
  entries of drift**: TASK-240 through TASK-245 were never added to D11 by their own closes, so the row
  had ended at TASK-237 while six tasks (four of them `done`) sat outside the ledger. Recorded here as
  drift found during TASK-245 rather than as this task's own work — the closes that should have added
  them are 240/241/242/243.
  The two spawns are both emitters of index DDL that the grill's audit reached while establishing that
  [[TASK-245]]'s new funnel really was a funnel (§ Conventions, TASK-243's "a funnel with four overrides
  is not a funnel"). Neither is MySQL-specific, so neither belonged in TASK-245:
  **TASK-246** is the one that is shipping: `SqlIndexBuilder.Build()` never copies `_unique` onto the
  `IndexDefinition` it hands the connector, so a migration's `.Unique()` emits a plain `CREATE INDEX` on
  all four providers — a silently missing **constraint**. What hid it is that the raw-SQL fallback three
  lines below *does* honour `_unique`, i.e. the feature works in the path nobody uses and fails in the
  path everybody uses; and no test in the tree calls `CreateIndexes` directly at all, while the existing
  unique-index suites all exercise the attribute path, which populates `Unique` correctly.
  **TASK-247** is latent: those same fallbacks emit `CREATE … INDEX IF NOT EXISTS` with quoted columns
  (1064 on MySQL, unresolvable folded column on PostgreSQL) and `DROP INDEX IF EXISTS … ON …`, which is
  wrong on both providers in *opposite* directions — MySQL rejects `IF EXISTS` but requires the `ON`,
  PostgreSQL accepts `IF EXISTS` but permits no `ON`. It depends on TASK-245 so it can reuse corrected
  emitters instead of re-deriving a fifth copy, and its first acceptance criterion asks whether the
  fallbacks should exist at all.
  TASK-245 itself was **retitled and widened** during the same grill, from "MySQL cannot create any
  declared index" to index DDL on every provider: measuring risk R4 confirmed PostgreSQL 16 cannot create
  any declared PascalCase index either — the quoted column identifiers cannot resolve against the folded
  columns bare base-table DDL creates — so `affects:` grew to four production repos. Kept as one task per
  this repo's precedent (TASK-242, TASK-243) rather than promoted to a story; the widening is recorded in
  its own `#### Resolved decisions` block with the added acceptance criteria stated explicitly rather than
  absorbed into the original four.
- 2026-08-18 — **D11 gains [[TASK-248]]**, spawned from [[TASK-245]] while choosing that task's test model
  and backfilled at creation. Inside D11's scope, no new decision row. TASK-245 fixed the *syntax* half of
  "MySQL builds no declared index" (`CREATE INDEX IF NOT EXISTS` → 1064); this is the remaining half and a
  different cause: an unbounded `string` maps to `LONGTEXT`, and MySQL cannot index a BLOB/TEXT column
  without a key length (**1170**), unique or not. It matters because that is the shape the *canonical*
  documented example declares — `CompositeUniqueIndexEndToEndTests`' `UxDoc.Number` — which passes on SQLite
  and silently yields no index and no constraint on MySQL. Filed with three candidate fixes and the explicit
  instruction to decide per index kind rather than take the cheapest, since a prefix-truncated UNIQUE
  constraint is worse than none. Its boundary is pinned by a TASK-245 test asserting 1170, so closing it
  means updating that test rather than deleting it.
  [[TASK-245]] itself closed `done` the same day: four production repos, four test repos, 61 new tests,
  1,086 green across 14 suites against live MySQL 8.4 / PostgreSQL 16 / SQL Server 2022. Its close gate found
  one thing worth recording here — making index columns bare (required, or PostgreSQL cannot resolve them)
  removed an accidental containment on `IIndexManager.CreateAsync`, whose column names come from the caller,
  so a payload reached the DDL exactly as SH-H023's rule field did. Guarded and tested in the same task.
- 2026-08-18 — **D11 gains [[TASK-249]]**, backfilled at creation, and closed `done` the same day. Inside
  D11's scope, no new decision row. It carries the four findings [[TASK-245]]'s own close-gate `code-review`
  returned **after** that task's nine commits had landed — three of them defects in those commits. The
  serious one is a second injection sink: TASK-245 made index columns be emitted bare (required for
  PostgreSQL) and guarded the one caller-derived sink it found, while
  `Birko.Data.Migrations.SQL`'s `SqlIndexBuilder.WithField` was the other, letting a migration append a
  statement through a column name. Also fixed: the new guard accepted a `Table.` qualifier *and its own test
  pinned that as correct*; `IIndexManager` was left **more** divergent on MySQL than before, on both index
  verbs, because it bypasses the `CreateIndexes` funnel by design; and a comment asserted an invariant the
  same commit had reversed. Filed as its own task rather than reopening TASK-245, whose acceptance was met
  and whose list stays useful only if it is not retrofitted — but fixed immediately rather than scheduled,
  because the first finding was a live injection. 1,100 tests green with all four providers live.
- 2026-08-18 — **[[TASK-246]] closed `done`.** Inside D11's scope, no new decision row. A migration's
  `.Unique()` had built a **plain** index on all four providers, because `SqlIndexBuilder.Build()`'s connector
  path never copied `_unique` onto the `IndexDefinition` it handed the connector — a missing *constraint*,
  silently accepting duplicates the migration existed to forbid. One line; the value is in why it survived.
  `SqlSchemaBuilder`'s builders have **two branches**, and the raw-SQL fallback (taken only when
  `connector == null`) honoured the flag correctly — which is exactly how every pre-existing test in that
  project constructed the builder. The feature worked in the branch nobody uses and failed in the branch
  everybody uses, and the suite was green throughout. Now a standing § Conventions rule: where a component has
  a fallback branch, a test that takes the fallback is not a test of the component. Second instance of the
  lost-flag shape in two days after TASK-245's `ToSqlIndexDefinition`. 7 new tests, 46 green with live
  PostgreSQL 16 (chosen over MySQL so the test also proves the index binds to the folded columns it names);
  revert fails 3 of 46 including the live one.
- 2026-08-18 — **[[TASK-248]] closed `done`**, the last of [[TASK-245]]'s three spawns. Inside D11's scope, no
  new decision row — but it carries a **real design decision**, recorded in the task body as its first
  acceptance criterion demanded. MySQL cannot index a BLOB/TEXT column without a key length (1170) and a plain
  `string` maps to `LONGTEXT` there, so a declared index over one was unbuildable even after TASK-245.
  **The measurement inverted the obvious fix.** Refusing the declaration at table load (§ SH-H037) would have
  converted **7 live consumer entities** into start-up failures — Symbio's docnumber and e-mail UNIQUE
  composites, the same `(TenantGuid, Number)` pairs the TASK-204 incident was about — all of which work
  correctly on PostgreSQL today, while **0** framework domain models declare an index attribute at all. So the
  provider's limit is absorbed at the provider: `VARCHAR(255)` for an indexed string on MySQL alone, with the
  other three asserted unaffected. A prefix index was rejected on semantics (every real case is UNIQUE, and a
  prefix makes the constraint *weaker than declared*), and bounding all four was rejected for the mirror reason
  (a 255-char ceiling where none exists today). 1,170 tests green across 19 suites including the five
  `Birko.Models.*.SQL` domain suites the criterion named for clearance.
  Two process notes: a revert that dropped half the fix failed **0 of 67** tests, because every model in the
  first suite used `[CompositeIndex]` while `LoadIndexes` resolves columns at two points — a revert that fails
  nothing is a missing test. And the blast-radius survey was **wrong twice** before it was right: the consumer
  entities declare their attributes fully qualified, which an unqualified grep misses, and a hand-rolled parser
  then mis-classified them as bounded. A survey that under-reports reads exactly like a clean bill of health.
- 2026-08-18 — **Correction to the [[TASK-246]] entry above: latent, not firing.** While scoping [[TASK-247]]
  a sweep of all 16 consumer repos found **0** calls to `context.Schema.CreateIndex(...)` / `.Unique()` and
  **0** uses of `ISchemaBuilder` anywhere — Symbio's unique docnumber and e-mail indexes come from
  `[CompositeIndex]` attributes via schema-ensure, which that defect never touched. So the missing `Unique`
  flag was a real hole in a **public API** that nothing yet drove, not an actively-corrupting one; the original
  entry's "silently accepting duplicates" framing overstated the live impact. Recorded here rather than
  softened in place, because a reader would otherwise conclude duplicate documents had been accepted in
  production. The fix stands on the API being public — and on Symbio's own `UniqueIndexDataCheck` existing
  precisely because they expect to add migrations.
- 2026-08-18 — **[[TASK-247]] closed `done`**, and with it the whole index-DDL thread [[TASK-245]] opened
  (245 → 246 → 247 → 248 → 249, all closed the same day). Inside D11's scope, no new decision row — but it
  carries a decision: the connector-free raw-SQL fallbacks in `SqlSchemaBuilder` were **deleted** rather than
  repaired, and the connector made required across `SqlSchemaBuilder`, `SqlMigrationContext` and
  `SqlDataMigrator`. Two of the eight fallbacks emitted DDL that MySQL and PostgreSQL reject, so the
  connector-free path was the appearance of portability rather than portability. Reachability was measured
  first: the runner already requires a connector, so the context's optional argument was the sole door, and a
  sweep of all 16 consumer repos found 0 hand-built contexts and 0 uses of `ISchemaBuilder`.
  **The fallback's real cost was test integrity, not runtime behaviour** — `connector == null` was how every
  test in that project built the builder, so six exercised only the dead branch, which is exactly how
  TASK-246's defect stayed green. Requiring the connector converted those six into real tests. Whole-solution
  build clean, 1,199 tests across 17 suites, revert fails 2 of 47. Two capabilities are recorded as
  deliberately dropped (composite `PRIMARY KEY (a, b)`, and `RenameField` keeping hand-written SQL), because a
  deletion that quietly loses a capability is indistinguishable later from one that never had it.
- 2026-08-18 — **D11 gains [[TASK-238]], [[TASK-239]] and [[TASK-250]]**, closing the DV9 finding from
  `/roadmap --check`: all three carried `feature: FEATURE-014` while no decision row listed them, so the ledger
  did not know about its own work. **TASK-250 is this session's own miss** — D11 was backfilled with 240–249
  *before* the duplicate `TASK-240` was renumbered to 250, so the new id never got added; 238 and 239 predate
  it. Worth noting the shape rather than just the fix: DV9 exists because a task created under an existing
  parent inherits `feature:` without passing through any of the three backfill hooks, and a **renumber** is a
  fourth way in that none of them cover.
- 2026-08-18 — **D11 gains [[TASK-251]]**, backfilled at creation, from the `/roadmap --check` drift audit.
  Inside D11's scope, no new decision row. The audit found **DV7 ×9** (stale specs), **DV9 ×3** (tasks the
  ledger did not list) and **DV11 ×25** (`shaped-by` never derived, which was *suppressing DV8 entirely* —
  a whole class of drift detection dark). DV9 and DV11 are closed; DV7 went **9 → 3**. Two of those six were
  closed by **measurement rather than harvest**: `repository-contract` and `views-and-aggregation` showed no
  content drift at all and were flagged only for unknown baselines, and the repos concerned had no commits
  touching those sources since the specs were written — so their current HEADs are provably equivalent
  baselines. The other four (`migrations`, `schema-index-and-ddl`, `event-bus-and-messaging`,
  `background-jobs`) were harvested. TASK-251 carries the three wide-surface areas left, deliberately not
  rushed: each spans ~10 tasks' worth of behaviour and the spec **diff review** — "was this change
  intended" — is the actual deliverable, which a hurried pass cannot answer. It also records the 5 unknown
  baselines that could *not* be measured away, so nobody stamps them on assumption.
- 2026-08-18 — **D11 gains [[TASK-252]]**, backfilled at creation. Inside D11's scope, no new decision row.
  It collects six per-provider gaps the index-DDL thread surfaced and correctly kept out of the task in
  hand — `RENAME COLUMN` not being universal, composite primary keys being unsupported through the schema
  builder, `Sparse()`/`WithProperty()` as silent no-ops, MySQL's 3072-byte key ceiling for *bounded*
  columns, `byte[]` still unindexable there, and `AsyncDataBaseStore.InitCoreAsync` being sync-over-async so
  the async schema-ensure loop has no store-level caller.
  **Filed because each had come to rest as prose in a closed task's out-of-scope section**, which is
  [[TASK-149]]'s defect arriving in the shape it warns about: only `status: todo` tasks are ranked by `pick`,
  `Next up` or `fix-next`, so six paragraphs inside finished tasks were filed but not schedulable. Grouped
  rather than split six ways because they are one family and splitting would bury the connection that makes
  them cheap together. Each requires a **measurement before a fix**, explicitly — the same thread produced
  TASK-248, whose honest-looking fix was measured and rejected for breaking seven live consumer entities, and
  the task records the traps already visible for four of the six.
