---
id: TASK-310
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P0
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H004, SH-H005, SH-H007]
pr: "[Birko.Data.SQL.Caching@328b2ee, Birko.Data.SQL.Caching.Tests@2a34e2e, Birko.Data.SQL.Tests@b57fd14]"
github-issue: null
jira-key: null
---

# Triage the 3 remaining high spec-harvest findings in `caching`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **3** open findings in the `caching` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H004` | Cache key derives from Expression.ToString(), so closure-captured filter values collide across tenants | `Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:51` |
| `SH-H005` | SQL cache keys carry no database, connection or tenant identity | `Birko.Data.SQL.Caching/Caching/SqlCacheKeyBuilder.cs:24` |
| `SH-H007` | UpdateAsync(filter, Action<T>) writes back a stale cached snapshot, silently reverting concurrent changes | `Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:96` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: caching`, lines 58-84.

**The contract under review** is specced in [`docs/specs/caching.md`](../../../docs/specs/caching.md),
harvested from 13 source files — `../Birko.Caching.Hybrid/HybridCache.cs`, `../Birko.Caching.Hybrid/HybridCacheOptions.cs`, `../Birko.Caching.Redis/Exceptions/WholeDatabaseDeleteException.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `SqlCacheKeyBuilder`: **0** consumer `.cs` files; `Birko.Data.SQL.Caching` appears only in the Sandbox aggregator.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** `SH-H004`/`SH-H005` claim cache keys that **collide across tenants** — cross-tenant read exposure, the class of [[TASK-113]] and [[TASK-114]], both P0. The original finding text calls them *"arguably higher impact"* than the `FLUSHDB` defect [[TASK-117]] closed.

The original finding text calls `SH-H004`/`SH-H005` *"arguably higher impact"* than the `FLUSHDB` defect
[[TASK-117]] closed, and TASK-117's own Out-of-scope says **"Verify them next."** That instruction has
had nothing scheduling it since 2026-07-31 — this task is the schedule.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/caching.md` lying until `/specs regen caching` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 3 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen caching`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 36 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-169]] owns those.
- Low-severity findings in this area — [[TASK-184]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

**N/A — fully covered by automated tests.**

Resolved at close (2026-09-09). The reason: every claim here is a string-equality or a stored-value
question, and both are asserted directly. The key-identity halves are compared as keys; the two
end-to-end halves run against a real SQLite database and assert the **stored column** after a
concurrent out-of-band write, which is the observed state rather than the absence of an exception.

A human could not see any of it: the whole defect is that two different queries produced the *same*
key, and a cache serving the wrong rows looks exactly like a cache serving the right ones from outside.
That indistinguishability is the finding.

## Implementation plan

_Populated by `/tasks plan TASK-310` — leave empty until then._

## ⚠ Step 3 complete — all three CONFIRMED from source. Step 4+ BLOCKED on a broken toolchain

Verification finished 2026-09-09; the fix was **not** started, because the .NET 10 SDK was removed from
the machine mid-session (see § *Blocker* at the end). Everything below was established by reading shipped
source, which needs no compiler — so a resumed session starts at step 4 and must not redo this.

### `SH-H004` — CONFIRMED (read, one premise not measured)

Both read overrides take the filter's string form off the **raw** expression:

```csharp
var filterString = filter?.ToString();                                   // :51 and :80
var key = SqlCacheKeyBuilder.BuildKey(_tableName, filterString, …);
```

No funcletization, no normalization. ⚠ **The premise I could NOT measure**: that a closure-captured local
renders identically regardless of its value (`value(<>c__DisplayClass0_0).tenantGuid`). That is standard
C# closure rendering and the finding says it was verified at harvest time, but my own probe could not run
— the SDK died before it executed. **A resumed session must measure it first**; the whole finding rests on
it, and this file should not be the place where a read became a claim.

### `SH-H005` — CONFIRMED, both halves

`BuildKey` composes `sql:{table}:{filterHash}:{orderHash}:{limit}:{offset}` — no database, connection or
tenant component anywhere. And `ResolveTableName()` is `static`, deriving the name from
`DataBase.LoadTable(typeof(T))` with a `typeof(T).Name` fallback, so it depends on **T's mapping alone**:
two stores pointed at different databases but sharing one `ICache` compute byte-identical keys.

⚠ **The flip side, which the finding does not mention and a fix must not break.** The same missing
identity makes `InvalidateCacheAsync` **over**-invalidate: it calls
`RemoveByPrefixAsync("sql:{table}:")`, so a write through one store wipes the other database's entries
too. That direction is correctness-preserving (a spurious miss, not a stale read), so adding identity to
the key must keep invalidation covering everything it should — narrowing the prefix without care would
turn an over-invalidation into a **missed** one, which is worse than the leak being fixed.
(Not the § Conventions Redis hazard: the prefix here is never empty.)

### `SH-H007` — CONFIRMED

`AbstractAsyncBulkStore.UpdateAsync(filter, Action<T>)` is, verbatim:

```csharp
var items = (await ReadAsync(filter, null, null, null, ct)).ToList();
foreach (var item in items) { updateAction(item); await UpdateAsync(item, ct: ct); }
```

`ReadAsync` routes to the overridden, **caching** `ReadCoreAsync`, so the loop mutates rows as they were
up to `DefaultExpiration` ago and `UpdateCoreAsync` issues a full-row UPDATE of every mapped column.
Lost updates, silently.

⚠ **And an existing comment in the file reads as though this path were fine.** Above the filter-write
overrides:

> *"(The `UpdateAsync(filter, Action<T>)` overload is already safe: it loops per-item through
> `UpdateAsync -> UpdateCoreAsync`, which invalidates.)"*

That is true **about invalidation** and says nothing about the stale **read** — but "already safe" is
exactly the phrase that stops the next reader looking. It needs correcting as part of the fix, or the
comment will refute the finding for whoever reads it next.

### What the fix has to decide (for the resumed session)

- `SH-H004`: normalise the filter before keying — evaluate captured constants (funcletize) so distinct
  values produce distinct keys — **or** refuse to cache a filter that cannot be rendered distinctly.
  ⚠ Prefer the second if the first cannot be made total: a cache that silently keys two different
  queries the same is the defect, and this framework's own rule is that a partial normalisation missed at
  one node is the identical silent wrong answer.
- `SH-H005`: add database/connection identity to the key. `Settings.GetId()` is the natural component
  (`Location:Name(:UserName:Port)`), **but** it is unavailable at construction — `ResolveTableName()` runs
  before `SetSettings` deliberately (CR-L177) — so the key can no longer be built purely in the
  constructor. That is the shape of the work, not a detail.
- Whether tenant identity belongs in the key at all, or whether `SH-H004` fixed properly makes it
  unnecessary (a correctly-keyed `x => x.TenantGuid == tenant` already separates tenants). ⚠ Answer this
  before adding a tenant component: two mechanisms for one property is the shape § Conventions keeps
  recording.
- `SH-H007`: the read inside a read-then-write loop must bypass the cache. Decide whether that is a
  per-call opt-out or an override of the filter-Update overload here.

## ⚠ Blocker — the .NET 10 SDK was removed from this machine mid-session

**Steps 4–6 cannot run.** Diagnosed 2026-09-09, and it is an environment failure, not a code one:

| Check | Result |
|---|---|
| `dotnet --info` → `.NET SDKs installed` | **"No SDKs were found."** (host reports 8.0.31) |
| `ls "C:\Program Files\dotnet\sdk"` | four `10.0.*` directories present but **empty** (0–1 entries each) |
| `host/fxr` | only `6.0.16`, `6.0.36`, `8.0.31` — **no 10.0 resolver**; `fxr` mtime 2026-09-09 07:45 |

Every project here targets `net10.0`, so nothing compiles and no test runs. Earlier in this same session
`dotnet test` ran four suites green, so the removal happened **during** it — consistent with a Windows
Update or an installer running in the background.

**The task is left `in-progress` with this log deliberately**, so a resumed run picks up at step 4 rather
than repeating the verification. It needs the SDK reinstalled (`winget install Microsoft.DotNet.SDK.10`
or the installer from dotnet.microsoft.com) — a machine action, not a repo one.

⚠ **No fix was written.** Writing one that cannot be compiled or tested would be a guess, and the entry
this session added to CLAUDE.md an hour earlier says exactly that: *"A fix in a code path nothing tests is
a guess."* Three confirmed high findings and no code is the honest state.

## Outcome

**What was broken.** A SQL query cache key did not identify the query it stood for, in two independent
ways, and a third defect made a cached read corrupt a write.

- **`SH-H004`** — the key was built from `filter.ToString()` of the **raw** expression. A
  closure-captured local renders as `value(<>c__DisplayClass…).field`, identical for every value, so
  `x => x.TenantGuid == tenant` produced **one key for every tenant** and the first tenant's rows were
  served to all the others. Measured: two different `Guid`s render byte-identically; inline literals do
  not, which is why every existing test — all of which used literals — passed.
- **`SH-H005`** — the key carried no database identity, so two stores on different databases sharing one
  `ICache` computed identical keys and each served the other's rows.
- **`SH-H007`** — `UpdateAsync(filter, Action<T>)` is read-then-write, and its read came from the cache.
  The loop mutated a snapshot up to 5 minutes old and `UpdateCoreAsync` wrote back **every mapped
  column**, silently reverting whatever another writer had changed.

**The fix.** `SqlCacheKeyBuilder` gained a `scope` segment (the database identity, hashed) that narrows
both lookup **and** the invalidation prefix, and a `TryDescribeFilter` producer that funcletizes the
filter and then **checks** the rendering. The store reads the scope lazily, bypasses the cache entirely
for a filter that cannot be described, and wraps the base filter-update in a flow-scoped scope its reads
honour.

### ⚠ The design turned on a measurement that inverted the obvious fix

Normalisation looked sufficient — the framework already ships `ExpressionNormalizer`, whose
funcletization folds a captured local to its value, so reusing it was the one-producer answer. Measured
before committing to it: it fixes **scalars** (`Guid`, `int`, `string` all render distinctly) and **does
not** fix collections — `List<int>{1,2,3}` and `{9,9,9}` both render
`value(System.Collections.Generic.List`1[System.Int32])`. So `ids.Contains(x.Id)`, the ordinary
set-membership filter, would still have collided across different id sets, and a fix that stopped at
normalisation would have closed the ticket while leaving the same class of leak live.

Hence two steps, not one: normalise, then **verify the rendering distinguishes the values**, and refuse
when it does not. Refusing costs a cache miss, which is always correct; sharing a key costs one caller
another caller's rows. **The trade is stated rather than hidden: set-membership and object-valued filters
are no longer cached at all.** Affordable because reach was measured at 0 consumer `.cs` files.

### Step 6 — three disjoint mutations, one per finding

| Mutation | Red |
|---|---|
| raw unnormalised filter, no opaque check | **4 of 23** — `Two_tenants_do_not_share_a_cache_key`, `Captured_strings_are_also_distinguished`, `A_set_membership_filter_is_refused_rather_than_keyed`, `A_set_membership_filter_reads_correctly_by_not_being_cached` |
| drop the scope segment | **4 of 23** — `Two_databases_do_not_share_a_cache_key`, `..._an_invalidation_prefix`, `The_scope_is_hashed_so_a_connection_string_cannot_leak_into_a_key`, `BuildKey_HashSegments_Are16HexChars` |
| stop honouring the read bypass | **1 of 23** — `FilterUpdate_DoesNotRevertAConcurrentWritersColumn` |

**Contract pins, green throughout and pins rather than evidence:** `Inline_literals_keep_working`,
`A_null_filter_is_describable_because_everything_is_a_real_query`,
`A_keys_prefix_is_still_its_own_invalidation_prefix`, `Different_tables_in_one_database_still_differ`,
`FilterUpdate_StillAppliesItsOwnChange`, `FilterUpdate_LeavesTheCacheUsableAfterwards`,
`Read_IsServedFromCache_ThenInvalidatedByWrite`, `Disabled_Cache_AlwaysHitsTheDatabase`. And
`The_raw_expression_really_does_collide_so_the_premise_is_pinned` pins the *premise* — if a future
runtime changed how a closure renders, that test says so instead of the fix silently becoming pointless.

**Suites:** Caching 23/23, `Birko.Data.SQL` 686/686, SqLite 348/348 — **1,057 green, 0 failed.**

### Judgement calls

- **Reused `ExpressionNormalizer` rather than writing a funcletizer.** One producer for "fold the
  parameter-free subtrees", already in the framework and already tested. The *check* on top of it is new,
  because that producer legitimately does not promise value-distinguishing renderings.
- **The refuse-marker is `"value("`, not a type whitelist.** A whitelist of "safe" constant types would
  have to be complete to be safe and silently wrong when it is not; the marker is what
  `ConstantExpression` actually emits when a value does not describe itself, so it is total by
  construction and errs toward not caching.
- **The scope is hashed, not inlined.** `GetId()` is colon-delimited, so inlining it would make the key's
  segments ambiguous — and a cache key is often visible in a log or a Redis keyspace where a host and
  service account do not belong. The table name stays in clear so a key is still recognisable by eye.
- **⚠ Rejected: scoping only the invalidation prefix, or only the key.** They must move together. A
  scoped key under an unscoped prefix merely over-invalidates (a spurious miss, harmless — and that is
  exactly what the code did before this change); an unscoped key under a scoped prefix would leave
  entries nothing ever removes, which is **worse than the leak being fixed**. Both halves are asserted.
- **⚠ Rejected: re-implementing the filter-update loop for `SH-H007`.** That was the planned shape and it
  was wrong twice over: the guard it needs (`RequireFilter`) is **private** to `AsyncDataBaseBulkStore`
  so it cannot be called, and copying the loop would duplicate a rule that already has one producer. The
  override now delegates to the base and diverts only the read.
- **The bypass is an `AsyncLocal` instance field with save-and-restore**, not a plain bool. A store can
  be a singleton serving concurrent requests, so a plain field would let one request's update disable
  another's cache — or leave it disabled. This is the mechanism TASK-270 established after exactly that
  mistake, and the restore-on-dispose is why an exception cannot strand the flag.
- **Rejected: shortening `DefaultExpiration` for `SH-H007`.** Any non-zero window is a lost update, and a
  window short enough to be safe would make the cache pointless.
- **A required parameter, not an overload.** Adding `scope` as the first parameter of `BuildKey` /
  `GetTablePrefix` breaks every existing call **loudly** (`CS7036`, measured at 31 sites, 0 of them in a
  consumer). An overload would have left the unscoped key reachable — a second implementation of the
  thing being fixed.

### Flagged, not fixed

- **⚠ Three key-shape tests failed on purpose and were re-aimed, not deleted.**
  `BuildKey_HashSegments_Are16HexChars`, `BuildKey_StartsWithSqlPrefix` and
  `GetTablePrefix_ReturnsCorrectFormat` asserted the old segment layout; their failure *is* the evidence
  the scope segment landed. The first now also asserts the table stays in clear, and the other two assert
  through `GetTablePrefix` rather than a literal so key and prefix cannot drift apart again.
- **`Widget` gained a second mapped column (`Body`)** in the existing test fixture. `SH-H007` is not
  observable with one column — the update action overwrites it either way — which is part of why the
  defect went unnoticed. Recorded because it changes a shared fixture.
- **The new tests live in the existing `CachedStoreBehaviorTests` class deliberately.**
  `ModelMapRegistry.ApplyToDatabase` mutates global table registration, and two parallel test classes
  each doing that is the shared-global-state family [[TASK-276]] exists for. Reusing the fixture mutates
  it once.
- **⚠ [[TASK-329]] spawned, and it is a P0: the SQL bulk stores never apply `RequireBoundedFilter` at
  all.** Noticed while reading `AsyncDataBaseBulkStore` for this fix, then **measured** with a throwaway
  probe rather than filed as a reading:
  `UpdateAsync(x => !empty.Contains(x.Name), r => r.Name = "OVERWRITTEN")` on on-disk SQLite gave
  `thrown=NONE | overwritten=3 of 3 | untouched=0` — a silent whole-table rewrite, which is
  [[TASK-137]]'s empty-`NOT IN` shape on the primary provider. Root cause is a hierarchy assumption:
  TASK-215 wired that guard into `AbstractBulkStore`/`AbstractAsyncBulkStore`, and the SQL bulk stores
  do **not** derive from them — they implement `IAsyncBulkStore<T>` directly and carry their own
  filter-based overloads, which is also why they have their own private `RequireFilter`. Unrelated to
  caching, so spawned rather than folded in; the probe was deleted.
- **`CountAsync` is not cached at all**, so none of this applies to it. Stated because a reader comparing
  the read paths would otherwise wonder.

## Progress log

- step 2 — picked; **chosen by the user**, and it was already this run's ranked top after TASK-311 closed:
  cross-tenant leakage on key 1, and the strongest key 4 in the pool (three findings, one project,
  no contested status). Runner-up TASK-309 (`data-sync`, 7 findings) is silent data loss — one tier down
  on key 1 — and much weaker on key 4. Key 6 inert: no story in this tree declares `theme:`, so all 81
  pool candidates are undeclared. ⚠ [[TASK-117]]'s own Out-of-scope has said *"Verify them next"* about
  `SH-H004`/`SH-H005` since 2026-07-31, calling them *"arguably higher impact"* than the `FLUSHDB`
  defect it closed.
- step 3 — verified: **all three HOLD**. `SH-H005` confirmed in both halves (`BuildKey` has no db/tenant component; `ResolveTableName` is static off `typeof(T)`), `SH-H007` confirmed against `AbstractAsyncBulkStore.UpdateAsync(filter, Action<T>)`'s verbatim read-then-write loop, `SH-H004` confirmed as code (raw `filter?.ToString()` in both overloads) with its closure-rendering premise **read but NOT measured** — the probe never ran. Two things recorded that the findings do not say: the missing key identity also causes **over**-invalidation (which a fix must not turn into a missed one), and an existing comment calls the `SH-H007` path *"already safe"* about invalidation in a way that reads as covering the stale read.
- ⚠ **step 4 BLOCKED — the .NET 10 SDK was removed from the machine mid-session** (`dotnet --info` reports "No SDKs were found"; the `sdk/10.0.*` directories are empty and `host/fxr` has no 10.0 resolver, mtime today 07:45). Every project targets `net10.0`. **No fix was written**, deliberately: it could be neither compiled nor tested. Resume at step 4 once the SDK is reinstalled.

### Resumed 2026-09-09 — the SDK blocker cleared and steps 4-8 ran

The machine had a **new** SDK installed (10.0.401, 192 entries) and the `10.0.12` fxr resolver was back:
it was a mid-flight upgrade that stripped the old 10.0.x SDKs, not an uninstall. `dotnet test` verified
before resuming (7/7 on the caching suite), then the run picked up at step 4 from the log below.

- step 3 (completed) — ⚠ **and one correction to it.** The premise I had recorded as *read but not
  measured* is now **measured**: captured `t1` and `t2` render byte-identically as
  `value(...DisplayClass5_0).tenant`, while inline literals render distinctly. ⚠ **And my step-3 note
  cited the wrong class for `SH-H007`** — `AsyncDataBaseBulkStore` does **not** derive from
  `AbstractAsyncBulkStore`; it implements `IAsyncBulkStore<T>` directly and has its own
  `UpdateAsync(filter, Action<T>)`. The mechanism is identical (a `ReadAsync(filter, …)` feeding a
  per-item update loop) so the verdict stands, but the citation was wrong and mattered: that class's
  `RequireFilter` is **private**, which ruled out the fix shape I had planned.
- step 4 — layer: **local**, all three. `Birko.Data.SQL.Caching`'s own key builder and store.
- step 5 — fix in `Caching/SqlCacheKeyBuilder.cs` (scope segment + `TryDescribeFilter`) and
  `Stores/CachedAsyncDataBaseBulkStore.cs` (both reads, invalidation prefix, filter-update read bypass);
  tests in `Birko.Data.SQL.Caching.Tests/CrossDatabaseAndFilterKeyingTests.cs` (12 new) and 6 added to
  `CachedStoreBehaviorTests.cs`; 31 existing call sites updated for the new signature, and 3 key-shape
  pins re-aimed. Suites: Caching 23/23, `Birko.Data.SQL` 686/686, SqLite 348/348 — **1,057 green**.
- step 6 — **three disjoint mutations, one per finding**: raw unnormalised filter + no opaque check →
  **4 of 23** red (`Two_tenants_do_not_share_a_cache_key`,
  `Captured_strings_are_also_distinguished`, `A_set_membership_filter_is_refused_rather_than_keyed`,
  `A_set_membership_filter_reads_correctly_by_not_being_cached`); drop the scope segment → **4 of 23**
  (`Two_databases_do_not_share_a_cache_key`, `..._an_invalidation_prefix`,
  `The_scope_is_hashed_...`, `BuildKey_HashSegments_Are16HexChars`); stop honouring the read bypass →
  **1 of 23** (`FilterUpdate_DoesNotRevertAConcurrentWritersColumn`).
- step 7 — respecced `caching`. Requirement **retitled** *"SQL query cache keys are deterministic and table-scoped"* → *"…deterministic, database-scoped and table-scoped"* (the old title asserted the defect's scope), its SHALL rewritten for the `scopeHash` segment plus why it is hashed, and a *"Two databases do not collide"* scenario added. The read-path requirement no longer quotes `filter?.ToString()` and now specifies the `TryDescribeFilter` gate and the lazy scope. **Two new requirements**: *"A filter that cannot be described by value is not cached"* (SH-H004) and *"A read-then-write filter update reads past the cache"* (SH-H007) — the latter had **no** requirement at all, which is part of why the path went unexamined. Also corrected two scenarios that quoted the old key shape verbatim.
- step 5d — out-of-scope sweep: one bullet described unowned work and now has an id. [[TASK-329]] (**P0**) — the SQL bulk stores never apply `RequireBoundedFilter`; **measured** at 3 of 3 rows silently rewritten by a reduces-to-everything filter on SQLite. Everything else in § Out of scope names an owner or states a limit.
