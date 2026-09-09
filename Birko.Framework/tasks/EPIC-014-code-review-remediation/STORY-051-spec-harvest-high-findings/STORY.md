---
id: STORY-051
parent: EPIC-014
status: in-progress
created: 2026-07-30
source: SPEC-HARVEST-FINDINGS-2026-07-30.md
severity: high
finding-count: 57
finding-ids: SH-H001 … SH-H057
---

# Spec-harvest — high findings

## Decomposed 2026-09-08 — the other 39 findings now have tasks

This story held **31 task files covering 18 of its 57 findings**. The remaining **39 had no task at
all**, so they were invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]] — *a
checklist line is filed, not scheduled*. That is the exact defect [[STORY-053]] diagnosed for the
**medium** tier on 2026-08-09 and fixed by decomposing into 22 per-area triage tasks; it was never
applied to the tier that outranks it. Filed by `/tasks intake --epic EPIC-014 --story STORY-051` into
**15 per-area tasks**, one per `### area:` section of the findings doc that still has open findings.

Grouping is by **area**, for the reason STORY-053 recorded: findings in one area share a spec, a source
set and frequently a root cause, and the intake rule is that findings fixed in one edit are one task.
Two areas needed no task — `bulk-filter-operations` and `entity-tagging` are fully covered by existing
work.

| Task | Area | Open findings | Priority |
|---|---|---|---|
| [[TASK-308]] | `filter-expression-translation` | 7 (`SH-H021`,`022`,`024`–`028`) | **P0** | ✅ **done 2026-09-09** — 7 of 7 confirmed (2 narrower), all fixed |
| [[TASK-309]] | `data-sync` | 7 (`SH-H008`–`SH-H014`) | **P0** |
| ~~[[TASK-310]]~~ | `caching` | ~~3 (`SH-H004`,`005`,`007`)~~ | **P0** — **DONE 2026-09-09** |
| ~~[[TASK-311]]~~ | `tenant-isolation` | ~~2 (`SH-H049`,`053`)~~ | **P0** — **DONE 2026-09-09** |
| ~~[[TASK-312]]~~ | `security-and-authorization` | ~~1 (`SH-H040`)~~ | **P0** — **DONE 2026-09-08** |
| [[TASK-314]] | `migrations` | 5 (`SH-H029`–`SH-H033`) | P1 |
| [[TASK-313]] | `entity-localization` | 4 (`SH-H015`–`SH-H018`) | P1 |
| [[TASK-315]] | `workflow-state-machine` | 2 (`SH-H056`,`057`) | P1 |
| [[TASK-316]] | `repository-contract` | 2 (`SH-H034`,`035`) | P1 |
| [[TASK-317]] | `background-jobs` | 1 (`SH-H001`) | P1 |
| [[TASK-318]] | `event-bus-and-messaging` | 1 (`SH-H020`) | P1 |
| [[TASK-319]] | `schema-index-and-ddl` | 1 (`SH-H038`) | P1 |
| [[TASK-320]] | `specifications-and-paging` | 1 (`SH-H045`) | P1 |
| [[TASK-321]] | `store-crud-contract` | 1 (`SH-H046`) | P1 |
| [[TASK-322]] | `views-and-aggregation` | 1 (`SH-H055`) | P1 |

### Closed since the decomposition

- **[[TASK-312]]** (`security-and-authorization`, `SH-H040`) — **done 2026-09-08.** An authentication
  fail-open: `Enabled: true` with no usable token accepted every caller, including one presenting none.
  Confirmed, **trigger corrected** (the filed *renamed `${VAR}}`* case measurably fails **closed**; the real
  triggers are nothing-configured and a *blank* variable), and **widened** to a second independent gate in
  the SSE middleware that no finding had named. 128 tests green across five suites; two disjoint mutations
  red 3 of 57 and 2 of 24. ⚠ It also measured that **no spec area covers any of the four transports** that
  share this engine, so the SSE half produced no spec diff — recorded as a third instance on [[TASK-142]].

- **[[TASK-311]]** (`tenant-isolation`, `SH-H049` + `SH-H053`) — **done 2026-09-09.** Both confirmed, and
  they needed **different kinds of fix**: `SH-H049` a code change (the middleware took its
  `ITenantContext` from the root provider, so a scoped registration was never observed and the tenant
  wrappers' deliberate fail-open then spanned every tenant — now injected per request), `SH-H053` a
  **documentation** correction, because a mis-wired event bridge and a genuine system event are
  byte-identical from the event and narrowing the null branch would break cross-tenant system events. 193
  tests green; the true revert of the code half is a **build break** rather than a red test, so a
  compiler-tolerated mutation was run to produce a 2-of-74 split. ⚠ `SH-H049`'s contested downgrade is
  **resolved**: TASK-118's fail-open was about the guard, which TASK-118 itself routed around. Escalated
  as [[TASK-328]]: should the bridge refuse to widen rather than document the hazard?

- **[[TASK-310]]** (`caching`, `SH-H004` + `SH-H005` + `SH-H007`) — **done 2026-09-09.** All three
  confirmed. A SQL cache key did not identify its query in two independent ways (a captured filter value
  rendered identically for every value, so one key served every tenant; and no database identity, so two
  databases sharing a cache served each other's rows), and a cached read inside `UpdateAsync(filter,
  Action<T>)` made a full-row write silently revert a concurrent writer. ⚠ **The design turned on a
  measurement that inverted the obvious fix**: reusing the framework's funcletizer fixes captured
  *scalars* but not collections, so a set-membership filter would still have collided — hence
  normalise-then-**check**, and refuse to cache what cannot be described. 1,057 tests green; three
  disjoint mutations red 4, 4 and 1 of 23. Also closes [[TASK-117]]'s year-old *"Verify them next"*.

### The priority rule, stated so it is not arbitrary

Every finding here is high severity, so severity cannot be the discriminator — applying the intake
verb's "theme 1–2 blockers → P0" mechanically would make all 15 tasks P0 and rank nothing. The rule
used instead was calibrated against this story's **existing** 8 P0 tasks (auth bypass, SQL injection,
whole-table write, cross-tenant read/delete, silent non-persistence):

- **P0** — the claim, if true, is auth bypass, cross-tenant leakage, or silent loss/destruction of data.
- **P1** — a real defect whose blast radius is confined to one opted-into feature, decorator, migration
  or provider.

Each task states its own reason in a **Why P0/P1** line, so the call can be argued with rather than
inherited.

### ⚠ Consumer reach was measured, and it is mostly latent

Measured 2026-09-08 across all 16 consumer repos, and recorded per task so a fix is priced on what it
protects rather than on the claim's wording. Only **two** areas have live consumer use of the exact
type the finding names:

- `security-and-authorization` — `AuthenticationService`, **6** consumer `.cs` files.
- `filter-expression-translation` — `Birko.Data.SQL` in **6** consumer aggregators, and `DataBase.cs`
  is on every SQL read's path.

For the rest, the module is imported by an aggregator (often only `Birko.Sandbox`, which imports
everything) and **0** consumer `.cs` files construct the type. That is **not** a reason to downweight
them — this framework's recent history is largely defects that stayed latent until a consumer selected
the backend, and § TASK-219/256 record that the window *"closes the moment one does"*. It is a reason
not to overstate urgency.

### ⚠ Nothing was quietly already-fixed — checked item by item

[[TASK-252]]'s lesson (*a grouped latent-gaps task must be re-checked item by item before it is
worked*) was applied at filing rather than at draining. **13** of the 39 are *mentioned* in closed
task bodies; every one of those mentions is an explicit **"separate task"**, **"unverified"** or
**"not fixed here"** boundary, so none is closed. Two carry-overs worth knowing:

- **`SH-H049` is contested, not merely open.** Three closed tasks call it *"downgraded in STORY-051,
  not tasked"*, while [[TASK-118]] describes a live fail-open path through it and closed saying *"SH-H049
  is not fixed here. This task routes around it rather than through it."* [[TASK-311]] carries the
  instruction to re-measure the downgrade's premise before relying on it.
- **`SH-H038` has been mis-cited once already** — [[TASK-197]] corrected a working tree that used it for
  an unrelated field-mapping defect. It is the ElasticSearch reindex finding and nothing else.

## ⚠ The 2026-09-08 recovered-findings fold did NOT change this story

[[TASK-195]] folded 11 recovered findings into the severity backlog, and **none of them is high**. Its one
proposed high (`UOW-1` — an ElasticSearch commit failure leaving the buffer queued) was **downgraded to
medium** on measurement: all three operations that UoW can buffer are idempotent, so the "retry
double-applies" consequence is materially harmless, and the class's own doc comment already states it is
not a true ACID transaction — so partial application is documented rather than silent. It is now `SH-M426`
under [[TASK-325]].

So `finding-count: 57` and `finding-ids: SH-H001 … SH-H057` stand unchanged, and the 15-task decomposition
below still covers this tier completely. Recorded because [[TASK-195]]'s criteria asked for this story's
count to be updated, and *"no change, for this reason"* is the answer rather than an omission.

## Area closed: `filter-expression-translation` (2026-09-09)

[[TASK-308]] drained the first of the 15 per-area triage tasks. **7 of 7 confirmed** — 5 outright, 2
**confirmed-narrower** — and **0 refuted**, so the prior this story recorded (13 confirmed, 2 narrower, 0
refuted out of 15 hand-checked) held almost exactly.

The two narrowings are the same correction and worth carrying to the other 14 areas: `SH-H021` and
`SH-H026` both claimed *"Delete(filter) deletes the whole table"*, and that half was **already closed** by
SH-H002 + [[TASK-137]] — measured, both shapes threw `WholeTableWriteException` and left 3 of 3 rows. What
survived was the **read**-path wrong answer those tasks explicitly left open: the same predicate returned
every row, silently. So the story's own instruction — *check what TASK-109/116/137 already cover before
assuming a finding still holds* — was the right one, and it changed the scope rather than the verdict.

Root causes were **5, not 7**: `SH-H021`+`SH-H026` are one (an expression node the parser has no branch
for), `SH-H025`+`SH-H027` are one (a failed sub-translation becoming a query that means something else),
and `SH-H022`, `SH-H024`, `SH-H028` are one each. `SH-H022` was the most serious of the seven and the only
one whose destructive path SH-H002 could **not** cover, because its clause is non-empty and simply wrong:
`DeleteAsync(x => !(x.Amount == 10 && trueFlag))` threw nothing and destroyed the complement of the rows
it named.

Spawned: [[TASK-331]] (P1) — an expression-valued `UPDATE` that binds no parameter issues no statement at
all, found while writing `SH-H024`'s opt-out test. Different layer, different root cause, so it got an id
rather than an out-of-scope sentence, and TASK-308 pins the defect meanwhile.

## Progress

**30 / 57 findings closed** (SH-H021/022/024/025/026/027/028 via [[TASK-308]], SH-H039 via [[TASK-108]], SH-H047 via [[TASK-114]], SH-H054 via [[TASK-115]],
SH-H003 via [[TASK-110]], SH-H048 via [[TASK-118]], SH-H050+SH-H051+SH-H052 via [[TASK-113]],
SH-H002+SH-M023 via [[TASK-109]], SH-H041+SH-H042+SH-H043+SH-H044 via [[TASK-116]], SH-H036 via [[TASK-125]], SH-H019 via [[TASK-126]],
SH-H023 via [[TASK-111]]) — and **18 of the 57 findings now have a task**, across 31 files: 29 done, 1 in review ([[TASK-118]]), 1 cancelled. The **other 39 findings were decomposed into 15 per-area triage tasks on 2026-09-08** — see the section above; the counts in the rest of this paragraph predate that and describe the original 23-task set. [[TASK-137]] closed the defect [[TASK-109]] filed against itself while being
planned, and closed it much wider than filed: the empty `NOT IN`'s `1 = 1` was not merely an injection
lookalike in a log, it was a **non-empty `WHERE` that constrains nothing**, so it satisfied the whole-table
write guard TASK-109 had installed 18 days earlier and `Delete(x => !empty.Contains(x.Col))` emptied the
table silently (0 of 3 rows, no exception). A guard defeated by the code it guards is the shape to remember:
the tautology was chosen *for* being harmless. Its close gate spawned [[TASK-212]] (MongoDB's `RequireFilter`
refuses only a *null* filter, so the same shape is unguarded there — mechanism deliberately filed
**unverified**, since the driver owns the translation) and [[TASK-213]] (a *computed* operand inside
`Contains` is silently replaced by a fabricated predicate, answering 1 row where the truth is 0 —
pre-existing, unrelated, and found only because the fix added its shapes to the compiled-delegate oracle),
taking the story to 21 tasks. [[TASK-213]] then closed: `ids.Contains(x.Amount + 1)` never emitted an `IN` at
all, because a computed operand was parsed as a nested *predicate* and fabricated a subcondition that the
renderer preferred over the `In` — so the statement carried a **different** predicate, answering 1 row where
C# says 0 and 3 where it says 4. Its fix is a **reuse**: `RenderValueFragment` and `BuildValueComparison`
already did exactly this for comparisons, so the "translate or refuse" question the task posed had been
answered by shipped code. Two of these three tasks were found by *instrumenting* rather than by reading —
the oracle suite earned its place twice in one day. [[TASK-212]] then carried the same rule to its **third**
backend: MongoDB's overrides guarded only a *null* filter, so a present one covering every document reached
`DeleteMany` unrefused. Its measurement is the reusable part — the driver renders
`!empty.Contains(x.F)` as a **one-element** `{ "$nin": [] }` document, so the obvious guard ("refuse an empty
filter") would never have fired, exactly as "refuse when nothing was rendered" never fired on `1 = 1`. The
guard therefore went on the **expression** (`PredicateScope` in `Birko.Data.Core`, `RequireBoundedFilter` on
the bulk bases), which is translation-independent and available to every backend;
`WholeTableWriteException` moved to `Birko.Data.Core` so one `catch` still selects it everywhere. Wired into
MongoDB only, with [[TASK-215]] filed to carry it to InMemory and ElasticSearch after their own measurements,
and [[TASK-214]] filed for two serialization failures the probe stumbled into. Story now 23 tasks. TASK-126 completes the tenant trio with TASK-114 (write guard) and TASK-125 (read
bypass): a base class that documented its scoping contract in a comment and enforced none of it now
re-checks every record its hooks return. Unlike the other two it is hardening rather than a reproduced
leak — the framework ships no implementation of `TagServiceBase` — which is why it lost the ranking twice
before winning it. TASK-125 was the read-side sibling of TASK-114: an ordered `ReadOne` reached
`repository.Connector`, which unwraps to the innermost store, so it read around every decorator including
the tenant one. Its shape is worth remembering — a safe instance method and an unsafe same-named
*extension* differing only in arity, so adding an ordering to a working call silently dropped tenant
scoping. TASK-116 closed four findings that were one root cause — a degraded leaf constant is
indistinguishable from a real predicate, so negation inverts match-none into match-ALL — and the fix is a
tracked flag rather than four patched sites, because two further sites of the same species (unfiled) had
already made the same mistake. Its both-engines agreement test then found a fifth, in `RuleEvaluator`,
against which no finding had ever been filed. TASK-109 closed two findings that
were one decision: the SQL native paths and the portable bases both let a null or untranslatable filter
become a whole-table write, so one policy — refuse unless every-row was asked for explicitly — needed two
edits, at the four connector funnels and at the store boundaries. It also spawned [[TASK-141]] at its close
gate (MongoDB's four repeated guards are untested), taking the story to 17 tasks. TASK-113 closed three findings at once: they shared one root cause
(`ApplyTenantFiltering` scoping only the save predicates), so scoping the *fetch* closed the read, preview,
delete and knowledge paths together rather than one guard per path. TASK-110 also
closed the medium twin SH-M022, which counts under [[STORY-053]], not here; and [[TASK-128]] closed a defect
that has no `SH-` id at all, so the task count now runs ahead of the finding count.
TASK-125 (SH-H036) and TASK-126 (SH-H019) were verified by hand and filed on 2026-07-31 while closing
TASK-114, taking the verified-and-tracked set to 16 findings across 13 tasks. 15 are hand-verified — 13 CONFIRMED (one of them re-verified WIDER), 2 CONFIRMED-NARROWER, 0 refuted — and 14 of those
15 now have tasks ([[TASK-108]] … [[TASK-118]]). The remaining **42 are unverified harvester claims** and
must be confirmed before they are fixed. Per-finding detail is in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md).

**A verified finding can be verified wrong.** SH-H048 was filed CONFIRMED-**NARROWER** on the grounds that its
"route" tenant source did not exist. It does (`TenantMiddleware.cs:111-120`), and the verdict plus [[TASK-118]]
both instructed the fix to record it as nonexistent — which would have left a live, unguarded source out of a
security fix. Re-verified WIDER at fix time and retracted in the findings doc. Worth carrying into the
remaining 42: the verification pass is not automatically more trustworthy than the claim it is correcting.

**Four tasks were filed from the remediation itself, by hand and none with an `SH-` id**, taking the
story from 13 tasks to 17. TASK-109 alone produced two of them, at two different stages, which is the
pattern worth noticing: [[TASK-137]] (P2, blocked on TASK-109) came out of *planning* it — the
empty-`NOT IN` → `1 = 1` rendering shipped 2026-07-27 is a false SQL-injection signal in query logs, found
because TASK-109 proposed `1 = 1` as its own all-rows idiom and had it rejected on that ground — and
[[TASK-141]] (P2) came out of its **close gate**, where the review found MongoDB's four repeated null-filter
guards have no test at all, while the InMemory half of the same sweep had been *discovered* by a failing
test. The other two: [[TASK-128]] (P0, **closed same day**) — the view path's ORDER BY interpolated
caller text, the same defect one project over from TASK-110 — and [[TASK-129]] (P1, open) — an aggregate
view's generated DDL carries a double alias, so no persistent aggregate view can be created at all, found
while writing TASK-128's tests.

Two of the four surfaced at a *gate* rather than during the coding, so the gates are earning their place:
planning caught one, the close review caught another, and neither was a symptom anybody had observed.

## User story

As a maintainer, I want every **high**-severity spec-harvest finding confirmed and then fixed (or explicitly
waived), so behaviour the specs now record as wrong stops being the behaviour the framework ships.

## Where this came from

Generating `docs/specs/` (commits `3728969`, `acbbe9d`, `d40aba2`) meant reading 648 files across 25
cross-cutting areas at code HEAD `f3ac675`. The specs record behaviour **as-is**, defects included; this
story is the queue for changing it. Different provenance from STORY-024…027, which came from the
2026-06-17 review audit — so these carry an `SH-` prefix and do not renumber `CR-*`.

## Scope

The 57 high findings `SH-H001 … SH-H057`. Severity is by blast radius: **high** = silent data loss,
cross-tenant leakage, auth bypass, a destructive op on the wrong rows, or a predicate degrading to
match-all on a write path.

Distribution is uneven, and that is informative: the highs concentrate in **data-access, tenant and
security** — the same areas this repo's own CHANGELOG history sits in. `serialization`,
`llm-provider-and-agents` and `validation-and-rules` produced **no highs at all** between them.

## Tasks

Unlike STORY-024…027, the **verified subset is pre-created** — 11 tasks covering 14 of the 15 verified
findings. That deviation is deliberate: 14 bounded, hand-traced defects are pickable work, whereas
mirroring 865 unverified claims into the tree is exactly what EPIC-014 decided against. The other 42 highs
stay extraction-on-demand, one task per `SH-Hxxx`, **verification first**.

| Task | Findings | Prio | What |
|---|---|---|---|
| [[TASK-108]] | SH-H039 | P0 | `Pbkdf2.Verify` returns true for any password against an empty-segment hash |
| [[TASK-109]] | SH-H002, SH-M023 | P0 | Null/untranslatable filter renders `DELETE FROM "T"` |
| [[TASK-110]] | SH-H003, SH-M022 | P0 | ORDER BY identifiers unresolved + unquoted — injection sink *and* silent empty reads |
| [[TASK-111]] | SH-H023 | P1 | `rule.Field` reaches the WHERE clause unresolved and unquoted |
| [[TASK-112]] | SH-H037 | P0 | `long`/`double`/`float`/`short`/`byte[]` map to no column and never persist |
| [[TASK-113]] | SH-H050, H051, H052 | P0 | `TenantSyncProvider` scopes only saves — reads, previews, deletes span tenants |
| [[TASK-114]] | SH-H047 | P0 | Write guard trusts the caller-settable `item.TenantGuid` |
| [[TASK-115]] | SH-H054 | P1 | Nested `WithTenant` does not narrow reads inside an all-tenants scope |
| [[TASK-116]] | SH-H041, H044, H042, H043 | P0 | `RuleSpecification` leaves degrade to match-all on destructive paths |
| [[TASK-117]] | SH-H006 | P1 | `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set |
| [[TASK-118]] | SH-H048 | P1 | Tenant header/claim guard covers only the hard-coded `X-Tenant-Id` |
| [[TASK-125]] | SH-H036 | P1 | `ReadOne` queries the connector directly, bypassing every store decorator |
| [[TASK-126]] | SH-H019 | P1 | `TagServiceBase` states its tenant contract in a comment and enforces nothing |

Two groupings worth explaining, because they are not one-task-per-finding:

- **TASK-113 is three findings, one root cause.** `ApplyTenantFiltering`'s own XML doc says it "only
  modifies save filters, not fetch predicates" — so the unscoped read (H052), the unscoped delete (H051)
  and the knowledge/save mismatch (H050) are one omission seen from three angles. Fixing them separately
  would mean three partial fixes to the same method.
- **TASK-116 pulls in two unverified findings on purpose.** H041–H044 are all in `RuleSpecification.cs`
  (lines 62, 94, 97, 100) and all are "a leaf degrades to a constant that then widens to match-all". H044's
  *verdict* explicitly identifies H043's non-string `BuildStringMethod` path as its real trigger, so they
  cannot be fixed independently. One fixture covers the file.

The two SQL identifier-injection sinks (H003 → TASK-110, H023 → TASK-111) are **kept separate** despite
sharing a root cause — `DataBase.ResolveColumnName` exists and neither path calls it. They live in
different files with different resolution needs (an ORDER BY key vs. a rule leaf's column), so one task
would have carried two unrelated fixtures.

## SH-H049 is downgraded, not tasked

`UseTenantMiddleware` really does bind `ITenantContext` from the root provider, but the shipped
`TenantContext` holds tenant state in `AsyncLocal`, so the **default registration is safe per-request**.
Only a consumer registering a scoped `ITenantContext` that keeps per-request state in *fields* is bitten.
Recorded here rather than fixed so the downgrade is a decision on the record — re-rate it if a consumer
ever ships such a registration.

## Coverage gaps

Not to be mistaken for a complete audit:

- **42 of 57 are unverified.** Of the 15 checked, 3 needed their scope corrected and 1 named the wrong
  trigger entirely — roughly a quarter were imprecise. **Verify before fixing.**
- **All 22 previously-capped areas were swept uncapped** and every agent self-reported
  `sweptToExhaustion: true` — the agents' own claim, not independently checked.
- **3 areas carry no severity rating at all** and so appear in no severity story — tracked as [[STORY-055]].
- Missing test coverage was out of scope for the sweep and is not reported.

## Acceptance criteria

- [ ] The 42 unverified high findings are each confirmed, narrowed, or refuted against the code
- [ ] Every CONFIRMED high is fixed with a regression test, or explicitly waived with a reason
- [x] All 22 capped areas swept uncapped and folded in (2026-07-30)
- [x] The verified subset is decomposed into pickable tasks (2026-07-30)
- [ ] Any finding whose fix changes behaviour triggers a `/specs regen` of its area, and the resulting spec
      diff is reviewed — the specs currently document the defective behaviour as shipped

## Human test plan

N/A — this is a tracking story. Each child task carries its own plan.
