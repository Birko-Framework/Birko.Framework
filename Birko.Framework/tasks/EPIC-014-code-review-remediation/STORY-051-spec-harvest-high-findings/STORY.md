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

## Progress

**17 / 57 findings closed** (SH-H039 via [[TASK-108]], SH-H047 via [[TASK-114]], SH-H054 via [[TASK-115]],
SH-H003 via [[TASK-110]], SH-H048 via [[TASK-118]], SH-H050+SH-H051+SH-H052 via [[TASK-113]],
SH-H002+SH-M023 via [[TASK-109]], SH-H041+SH-H042+SH-H043+SH-H044 via [[TASK-116]], SH-H036 via [[TASK-125]], SH-H019 via [[TASK-126]],
SH-H023 via [[TASK-111]]) — **20 of 23 tasks
done, 1 in review, 2 todo.** [[TASK-137]] closed the defect [[TASK-109]] filed against itself while being
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
