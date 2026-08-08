---
id: TASK-126
parent: STORY-051
feature: FEATURE-014
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-114, TASK-125]
pr: >-
  Birko.Data.Tagging@75c9500, Birko.Data.Tagging.Tests@9ce8713
github-issue: null
jira-key: null
findings: [SH-H019]
---

# `TagServiceBase` states its tenant contract in a comment and enforces nothing

## Context

`../Birko.Data.Tagging/Services/TagService.cs:11` — **CONFIRMED by hand (2026-07-31)**.

The class opens with the contract written out in prose:

```
// TENANT-SCOPING CONTRACT: this base class stamps TenantGuid = GetCurrentTenantId() on every
// insert, but the read/delete hooks below receive NO tenant parameter — implementations MUST
// scope every one of them (including GetTagByIdAsync) to the ambient tenant themselves.
// A hook that skips the filter returns/deletes other tenants' data; the base class has no guard.
```

That comment is accurate, and it is the whole enforcement. All 12 abstract data-access hooks (lines 16–28)
take no tenant parameter, and `TagServiceBase` never compares a loaded record's `TenantGuid` to
`GetCurrentTenantId()` — which it has, at line 30. `Tag` carries `TenantGuid`.

**It is not only a read leak.** `UpdateTagAsync:70` and `DeleteTagAsync:82` both reach their target through
the same unguarded `GetTagByIdAsync`, and `DeleteTagAsync` then calls `DeleteAllEntityTagsForTagAsync(tagId)`.
So one implementation omitting one filter in one hook exposes cross-tenant **writes** and a cascade delete,
not just reads — across every implementation of the base, of which there may be N.

Same family as [[TASK-114]] (write guard trusted a caller-settable field) and [[TASK-125]] (read helper
skipped the decorator chain), one rung weaker: this one trusts the *implementor*.

## Why this is cheap

`GetTagByIdAsync` is a **single choke point** — get, update and delete all funnel through it. A post-load
assertion there turns a silent cross-tenant leak into a hard failure for every implementation at once,
without touching any of them. The list/search/link hooks need their own handling but are read-only.

## Approach

Add a base-class assertion on every record that comes back from a hook, comparing `TenantGuid` to
`GetCurrentTenantId()`. Decide two things explicitly:

1. **Fail-closed vs filter-out.** For the by-id path a **throw** is right — the caller named a specific id
   and getting silence would hide the misconfiguration that caused it. For `ListAllTagsAsync` /
   `SearchTagsByNameAsync` / the `GetEntityTagLinks*` batch hooks, throwing on one foreign row would take
   down a whole list; filtering is probably right there, but a filtered-out row is exactly the signal that an
   implementation's hook is unscoped, so it must at minimum be observable rather than silent.
2. **What "no tenant" means.** `GetCurrentTenantId()` returns a non-nullable `Guid`. Check what an unset
   tenant resolves to before writing the comparison — `Birko.Data.Tenant` learned the hard way
   (`ModelByTenant`) that `Guid.Empty` is *a tenant value*, not "unset", and that treating it as a wildcard
   makes reads fail open while writes fail closed. Do not repeat that.

The prose contract stays, but it should then describe an enforced invariant rather than ask implementors to
be careful.

## Acceptance criteria

- [x] A hook returning a foreign-tenant `Tag` from `GetTagByIdAsync` causes `GetTagAsync`, `UpdateTagAsync`
      and `DeleteTagAsync` to **fail** rather than operate on it — asserted with a deliberately unscoped
      test implementation of the base
- [x] `DeleteTagAsync` does not reach `DeleteAllEntityTagsForTagAsync` for a foreign tag
- [x] The list / search / entity-link paths do not return foreign-tenant records, and whatever they do
      instead (filter or throw) is deliberate, documented, and not silent
- [x] `Guid.Empty` behaviour is decided explicitly and pinned by a test — no wildcard-by-accident
- [x] A correctly-scoped implementation is unaffected: no extra failures, no behaviour change
- [x] Regression tests in `Birko.Data.Tagging.Tests`
- [x] `/specs regen` for `entity-tagging`, spec diff reviewed
- [x] `Birko.Data.Tagging/CLAUDE.md` updated — the contract is now enforced, not advisory

## Out of scope

- `AttachTagAsync` validating a tag's existence and ownership — now filed as [[TASK-147]].

- Auditing consumer implementations of `TagServiceBase` for missing filters. The point of this task is that
  the base stops depending on them being correct.
- `SH-H036` ([[TASK-125]]) and the tenant-wrapper work in [[TASK-114]].

## Outcome

**What the fix is.** `TagServiceBase` stated its tenant-scoping contract in a comment and enforced nothing
— 12 data-access hooks take no tenant parameter, and nothing compared a loaded `Tag.TenantGuid` to
`GetCurrentTenantId()`. One implementation omitting one filter in one hook exposed cross-tenant reads,
cross-tenant writes and a cascade delete, because `GetTagAsync`, `UpdateTagAsync`, `DeleteTagAsync` and
`GetEntityTagsAsync` all reach their target through the same `GetTagByIdAsync`. The base now re-checks
what the hooks return.

**Two rules, chosen by failure mode.** A tag loaded **by identity** is asserted and a foreign record
throws `CrossTenantTagAccessException`; a **collection** result has foreign records filtered out. The
split is deliberate: the caller of a by-identity read named one record, so a wrong answer must not be
downgraded to "not found" nor written to — while one leaked row must not blank an entire tag picker.

**Step 6 — reverted the guard only: 7 of 20 failed.** A full revert removes the exception type too and
5 tests stop compiling, so only `TagService.cs` was reverted, isolating the guard from the scaffolding.
Fix-dependent (7): `GetTag_throws_…`, `UpdateTag_throws_…`,
`DeleteTag_throws_and_never_reaches_the_entity_link_cascade`, `CreateTag_throws_…`,
`AttachTagByName_throws_…`, `List_and_search_drop_foreign_tags_instead_of_throwing`,
`An_empty_tenant_on_a_record_is_a_value_not_a_wildcard`. **Contract pin, not evidence (1):**
`A_correctly_scoped_implementation_sees_no_behaviour_change`, which passes both ways *by design* — that
is what makes it the back-compat half rather than proof.

**Judgement calls, and the stricter option rejected.**

- **Collections filter instead of throwing.** Throwing everywhere would be stricter and more consistent,
  and was rejected: it makes one leaked row take out a whole list, turning a data problem into an outage.
  The cost — a broken hook is quieter on those paths — is stated in the code and the spec rather than
  left implicit.
- **`Guid.Empty` is a tenant value, not a wildcard.** The permissive reading would let an unconfigured
  scope see everything, which is the exact accident a tenant wrapper elsewhere in this family already
  shipped once.
- **A dedicated exception rather than `InvalidOperationException`.** An isolation breach is worth
  selecting on. It lives in `Birko.Data.Tagging` because this project has no `Birko.Data.Tenant`
  dependency, and acquiring one to share `TenantMismatchException` would be a larger change than the
  guard.
- **Two by-name paths were pulled in that the finding did not name** — `CreateTagAsync` returns
  `FindTagByNameAsync`'s hit *instead of inserting*, so an unscoped hook hands back a foreign tag as
  newly created. Same root cause and same function family, so guarded here rather than spawned.
- **An existing test was changed, and the fixture was at fault, not the guard.** It seeded a tag with
  `TenantGuid` left at `Guid.Empty` under a real ambient tenant — a state `CreateTagAsync` cannot
  produce. `SeedTag` now defaults the tenant; `SeedForeignTag` seeds a leak deliberately.

**Flagged, not fixed.**

- **This is hardening, not a reproduced leak.** The framework ships **no** implementation of
  `TagServiceBase`, so no shipped code was leaking — that is exactly why this task lost to TASK-116 and
  TASK-125 twice on reachability. The tests use a deliberately unscoped implementation, which is the only
  shape available.
- **`AttachTagAsync` still validates neither the tag's existence nor its ownership**, so a link carrying
  this tenant's stamp can still point at a foreign tag. Reading that entity's tags now raises rather than
  leaking, but the bad link is still creatable. Left alone: the finding is about the read/delete hooks,
  and validating attach is a behaviour change with its own cost (an extra lookup per attach).
- **Consumer implementations are not audited**, per `## Out of scope` — the point of the task is that the
  base stops depending on them being correct.

## Progress log

- 2026-08-07 — **step 2 — picked.** Cross-tenant leakage is the highest severity tier in the pool and, with
  [[TASK-125]] closed, nothing else reaches it. Ranked above [[TASK-117]] (`RedisCache.ClearAsync` issues
  `FLUSHDB` when `KeyPrefix` is null — the default — destroying sibling projects' queued messages and
  pending jobs): severe and reachable by default configuration, but an unbounded destructive write sits a
  tier below cross-tenant leakage, and its fix carries an unsettled owned-key-namespace choice that would
  risk stalling mid-session. Also above [[TASK-112]] (silent data loss, but a four-provider type-mapping
  build with an open design question) and [[TASK-111]] (injection, but needs a caller-influenced
  `rule.Field`).
- 2026-08-07 — **the reachability caveat that demoted this twice is recorded up front, not discovered
  later.** The framework ships **no** implementation of `TagServiceBase` — only the abstract base and
  `ITagService` — so this is hardening against a contract every future implementor must honour by hand,
  not a leak reproducible against shipped code. That is exactly why it lost to TASK-116 and TASK-125, and
  why it wins now that both are done. It also shapes the fix: the test cannot be "a real implementation
  leaks", it has to be "an implementation that omits the filter is caught by the base".
- 2026-08-07 — **step 3 — verified: HOLDS exactly as filed.** `TagServiceBase` opens with the contract in
  prose and that comment was the whole enforcement: 12 abstract hooks take no tenant parameter and nothing
  compared a loaded `Tag.TenantGuid` to `GetCurrentTenantId()`. `GetTagByIdAsync` really is the single choke
  point for `GetTagAsync`, `UpdateTagAsync`, `DeleteTagAsync` **and** `GetEntityTagsAsync`, and
  `DeleteTagAsync` reaches `DeleteAllEntityTagsForTagAsync` through it — so the read, the write and the
  cascade delete all hang off one unguarded call.
- 2026-08-07 — **two by-name paths the finding did not name, pulled in (same root cause, same function
  family).** `CreateTagAsync` returns `FindTagByNameAsync`'s hit *instead of inserting*, so an unscoped
  by-name hook hands the caller another tenant's tag as though they had just created it;
  `AttachTagByNameAsync` then links a foreign tag to a local entity. Guarded alongside rather than spawned.
- 2026-08-07 — **step 4 — layer: local** to `Birko.Data.Tagging`. The guard belongs in the base class,
  which is the thing that was trusting implementors.
- 2026-08-07 — **step 5 — fix in `Birko.Data.Tagging/Services/TagService.cs` +
  `Services/CrossTenantTagAccessException.cs` (registered in `.projitems`); tests in
  `Birko.Data.Tagging.Tests/TagServiceTenantGuardTests.cs` (8 new) — suite 20/20 green.** Two rules, chosen
  by failure mode: **by-identity loads throw**, because the caller named one record and a wrong answer must
  not be downgraded to "not found" nor written to; **collections filter**, because one leaked row must not
  blank a whole tag picker.
- 2026-08-07 — **an existing test failed, and the FIXTURE was wrong rather than the guard.**
  `AttachTagByName_ConcurrentCreateBetweenMissAndInsert_ReusesRacedTag` seeded a raced tag with
  `TenantGuid` left at `Guid.Empty` while the fake's ambient tenant is a real Guid — a state no real
  implementation can produce, since `CreateTagAsync` stamps the tenant on every insert. `SeedTag` now
  defaults the tenant (with `SeedForeignTag` to seed a leak deliberately). Criterion 5 is about a
  *correctly-scoped* implementation being unaffected; this fixture was not one.
- 2026-08-07 — **step 6 — reverted the guard only: 7 of 20 failed.** A full revert would have removed
  `CrossTenantTagAccessException` too and 5 tests would not compile, so only `TagService.cs` was reverted —
  that isolates the guard rather than the scaffolding. Fix-dependent (7): `GetTag_throws_...`,
  `UpdateTag_throws_...`, `DeleteTag_throws_and_never_reaches_the_entity_link_cascade`,
  `CreateTag_throws_...`, `AttachTagByName_throws_...`,
  `List_and_search_drop_foreign_tags_instead_of_throwing`,
  `An_empty_tenant_on_a_record_is_a_value_not_a_wildcard`.
  **Contract pin, not evidence (1):** `A_correctly_scoped_implementation_sees_no_behaviour_change` — it
  passes both ways *by design*; that is what makes it the back-compat half rather than proof of the fix.
- 2026-08-07 — **step 7 — respecced `entity-tagging`.** The requirement TITLE was the defect stated as
  contract — *"Tenant scoping is stamped on inserts only, never enforced on reads or deletes"* — now
  *"...and re-checked on every record the hooks return"*, with its three "not blocked by the base class"
  scenarios replaced by the refusal/filter ones. The Purpose paragraph carried the same claim and was
  corrected too. Diff reviewed: every change traces to an acceptance row, nothing unexplained.
- 2026-08-07 — **step 8 — closed `done`; Birko.Data.Tagging@75c9500, tests@9ce8713.**

## Human test plan

N/A — covered by automated tests. A deliberately unscoped test implementation stands in for the
misconfigured consumer.
