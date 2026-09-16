---
id: TASK-309
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
findings: [SH-H008, SH-H009, SH-H010, SH-H011, SH-H012, SH-H013, SH-H014]
pr: Birko.Data.Sync@a527109, Birko.Data.Sync.CosmosDB@1f8ef41, Birko.Data.Aggregates@a1d475d, Birko.Data.Sync.Tests@3ce26b7, Birko.Data.Sync.CosmosDB.Tests@645b7af, Birko.Data.Aggregates.Tests@085acfc
github-issue: null
jira-key: null
---

# Triage the 7 remaining high spec-harvest findings in `data-sync`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **7** open findings in the `data-sync` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H008` | Bidirectional direction never applies SyncAction.Create — new items are silently dropped | `Birko.Data.Sync/SyncProvider.cs:276` |
| `SH-H009` | A never-uploaded bidirectional item is deleted on the next run | `Birko.Data.Sync/Internal/SyncProviderBase.cs:97` |
| `SH-H010` | Conflict resolution cannot fire for any conflict the provider emits | `Birko.Data.Sync/SyncProvider.cs:380` |
| `SH-H011` | Knowledge deletion flags are computed pre-write, so every Create marks the destination deleted | `Birko.Data.Sync/SyncProvider.cs:344` |
| `SH-H012` | SyncAsync persists knowledge with the run's own token, so a cancelled run loses all knowledge | `Birko.Data.Sync/AsyncSyncProvider.cs:205` |
| `SH-H013` | RavenDB/CosmosDB knowledge stores mishandle a null tenantId in opposite directions | `Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs:50` |
| `SH-H014` | Many-to-many expansion emits Insert/Delete of the child entity, never junction rows | `Birko.Data.Aggregates/Mapping/AggregateMapper.cs:213` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: data-sync`, lines 86-128.

**The contract under review** is specced in [`docs/specs/data-sync.md`](../../../docs/specs/data-sync.md),
harvested from 51 source files — `../Birko.Data.Aggregates/Core/AggregateDefinition.cs`, `../Birko.Data.Aggregates/Core/ExpressionHelper.cs`, `../Birko.Data.Aggregates/Core/IAggregateDefinition.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `SyncProvider`: **0** consumer `.cs` files; `Birko.Data.Sync` appears in **1** project file, the Sandbox aggregator. Compiled everywhere, selected nowhere.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** three of the seven claim **silent loss or deletion of a consumer's rows** (`SH-H008` drops every new item, `SH-H009` deletes a never-uploaded one, `SH-H012` loses all knowledge on cancellation). [[TASK-113]], also in the sync family and also opt-in, was P0.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/data-sync.md` lying until `/specs regen data-sync` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 7 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [x] Any behavioural fix is followed by `/specs regen data-sync`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 32 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-157]] owns those.
- Low-severity findings in this area — [[TASK-177]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

`N/A — fully covered by automated tests.` Resolved as the task predicted, and for the predicted reason:
every one of the seven findings lives in a library contract with no UI surface, and each is observable
entirely through store state (`local.Count()`, `remote.Read(...)`), knowledge-row fields, `SyncResult`
counters, the rendered Cosmos SQL, or the emitted `SyncOperation` list. Nothing here needs a human to look at
anything a test cannot assert. The 38 new tests cover all seven findings, and step 6's seven mutations show
each is red-verified.

## Implementation plan

_Populated by `/tasks plan TASK-309` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-314 (migrations, P1) because three of its seven findings claim silent loss or deletion of consumer rows (key 1, severity), which separates before any later key
- step 3 — verified all 7 against source. **6 confirmed, 1 confirmed-narrower (SH-H013: refuted for RavenDB, confirmed for CosmosDB).** Four (H008/H009/H010/H011) are one interlocking chain and are fixed together; H012, H013, H014 are independent root causes in three other files.
  - `SH-H008` **CONFIRMED** — `SyncProvider.cs:275-300` / `AsyncSyncProvider.cs:276-301`: the `Create` arm tests `Direction == Download` then `== Upload` with no else. `SyncOptions.Direction` defaults to **`Bidirectional`** (`Models/SyncOptions.cs:14`), and `DetermineSyncAction` returns `Create` for the two Bidirectional one-sided branches (`SyncProviderBase.cs:114`, `:137`). Neither arm matches, nothing is written, yet `result.Processed++` / `progress.ProcessedItems++` (`:340-341`) and a knowledge row (`:344`) are still emitted — and `SkippedItems` is *not* incremented, so the drop is invisible in every counter.
  - `SH-H009` **CONFIRMED** — consequence of H008 + H011. Run 1 on a local-only new item writes knowledge with `RemoteVersion = null` → `IsRemoteDeleted = true` (`SqlSyncKnowledgeStore.cs:68`, identical in all six backends). Run 2 hits `SyncProviderBase.cs:97`: `RemoteWins` → `Delete` on local (`:101`) **destroys the row**, `LocalWins` → `Create` → no-op again, `NewestWins`/`Custom` → `Conflict` → no-op (H010). All three lose the item.
  - `SH-H010` **CONFIRMED** — `SyncProvider.cs:380-394`. Conflicts are only produced by `SyncProviderBase.cs:105-112` (`RemoteItem = null`) and `:128-135` (`LocalItem = null`), so the opposite item is null by construction. `UseLocal when localItem != null` then inner `if (remoteItem != null …)`, and `UseRemote` symmetrically — every path falls through with no store write and no counter change.
  - `SH-H011` **CONFIRMED** — `SyncProvider.cs:344` builds the knowledge row from the **pre-action** `localItem`/`remoteItem`. After a Download create, `localItem` is still null, so every backend sets `IsLocalDeleted = string.IsNullOrEmpty(localItemHash)` = true. The flags mean "absent when decided", and the Delete branches (`SyncProviderBase.cs:78`, `:120`) read them as "deleted".
  - `SH-H012` **CONFIRMED** — `AsyncSyncProvider.cs:205/207/208` pass `options.CancellationToken` to `CreateAsync`/`UpdateAsync`/`SetLastSyncTimeAsync`, which run *after* the batch loop breaks on cancellation (`:181-182`), so they throw immediately and the outer catch (`:228`) records a generic "Sync failed". Knowledge for items **already written to the stores** in completed batches is lost. `SyncProvider.cs:203-207` passes no token and does persist — sync and async diverge on one contract.
  - `SH-H013` **CONFIRMED-NARROWER** — confirmed for CosmosDB, **refuted for RavenDB**. The finding assumes both are wrong "in opposite directions"; measured against the sole caller, only one is. `ResolveTenantScope` (`TenantSyncProvider.cs:118-141`) returns `null` in exactly two legitimate cases — an explicit `IsAllTenantsScope`, and an entity with no `TenantGuid` property — and **both mean "do not filter by tenant"**. So Raven's `if (tenantId.HasValue)` (`AsyncRavenSyncKnowledgeStore.cs:49`) is the *correct* rendering of "optional tenant", and its cross-tenant delete is the sanctioned all-tenants path, not a fail-open. Cosmos's unconditional `x.TenantId == tenantId` (`AsyncCosmosSyncKnowledgeStore.cs:48`, `:75`; `CosmosSyncKnowledgeStore.cs:46`, `:66`) renders `c.TenantId = null` in Cosmos SQL, which is **Undefined** and matches nothing — including rows whose `TenantId` genuinely is null (the model declares it `Guid?`). So knowledge is never found, never updated and never deleted, and every run looks like an initial sync. Fix narrowed to Cosmos, which is then made to agree with Raven.
  - `SH-H014` **CONFIRMED** — `AggregateMapper.cs:192-224`: `ExpandCollection` treats `ManyToMany` with the same code as `OneToMany`, tagging every operation with `relationship.ChildType` and the child entity. `JunctionType` / `JunctionParentFk` / `JunctionChildFk` are set by `RelationshipBuilder.Through<TJunction>` (`:44-47`) and read **nowhere** in the mapper — grep confirms the read side uses them (`GetRelatedViaJunction`, `:40/:68/:124/:153`) and the write side does not. `IAggregateMapper.Expand`'s own doc promises "insert/delete operations for child **and junction table** entities". Removing one category from a product emits `Delete` of the shared `Category` row.
- step 4 — layer: local (all four sites are Birko.* framework repos in this polyrepo: Birko.Data.Sync ×2 files, Birko.Data.Sync.CosmosDB ×2 files, Birko.Data.Aggregates ×1 — no upstream dependency is implicated)
- step 5 — fix in `Birko.Data.Sync/{SyncProvider,AsyncSyncProvider}.cs` (H008/H009/H010/H011/H012), `Birko.Data.Sync.CosmosDB/Stores/{CosmosSyncKnowledgeQuery (new),CosmosSyncKnowledgeStore,AsyncCosmosSyncKnowledgeStore}.cs` + `.projitems` (H013), `Birko.Data.Aggregates/{Mapping/AggregateMapper.cs,Core/RelationshipBuilder.cs}` (H014). Tests in `Birko.Data.Sync.Tests/{BidirectionalCreateAndConflictTests,CancelledSyncKnowledgePersistenceTests}.cs`, `Birko.Data.Sync.CosmosDB.Tests/CosmosSyncKnowledgeQueryTests.cs`, `Birko.Data.Aggregates.Tests/AggregateMapperJunctionExpandTests.cs`. **219/219 green across 10 suites** (Sync 64, Sync.Tenant 37, Sync.Sql 7, Sync.Json 7, Sync.Xml 7, Sync.MongoDb 5, Sync.ElasticSearch 11, Sync.RavenDB 9, Sync.CosmosDB 14, Aggregates 58), 38 new.
- step 6 — **seven disjoint mutations**, each reverting one root cause with the others left fixed, so the split attributes failures to the right half. All suites return to 220/220 green after restore.
  - **H008** (restore the `Direction ==` gates on the Create arm) → **7 of 64 failed** (Sync). Fix-dependent: `SH_H008_Bidirectional_uploads_a_new_local_item_instead_of_dropping_it`, `SH_H008_Bidirectional_downloads_a_new_remote_item_instead_of_dropping_it`, `SH_H008_Async_bidirectional_uploads_a_new_local_item`, `SH_H008_A_bidirectional_create_is_never_counted_as_processed_without_landing_somewhere`, `SH_H009_A_new_local_item_survives_a_second_bidirectional_run` (3 theory rows), `SH_H011_Knowledge_after_an_upload_records_the_remote_as_present_not_deleted`.
  - **H011** (restore the pre-action knowledge hashes) → **3 of 64**. Fix-dependent: the three `SH_H011_Knowledge_after_*` tests — including `..._after_a_delete_does_record_the_deleted_side`, which is the other side of the switch and is what stops "never mark deleted" passing as a fix.
  - **H010** (restore the both-items-required conflict arms) → **4 of 64**. Fix-dependent: the four `SH_H010_*` write tests. `SH_H010_Skip_still_leaves_both_sides_untouched` stayed green — **contract pin, not evidence**.
  - **H012** (restore `options.CancellationToken` on the three knowledge-persistence calls) → **3 of 64**. Fix-dependent: the three `SH_H012_*` tests. `The_synchronous_provider_records_knowledge_for_a_cancelled_run_too` stayed green — **contract pin**: that twin was never broken and is the behaviour the async side was made to match.
  - **H013** (restore the unconditional `x.TenantId == tenantId`) → **4 of 14** (Cosmos). Fix-dependent: `A_null_tenant_emits_no_TenantId_term_at_all`, `A_null_tenant_never_emits_a_comparison_against_null`, `A_null_tenant_selects_every_row_in_the_scope`, `The_scope_term_is_always_applied`. The two `A_supplied_tenant_*` tests stayed green — **contract pins** for the other side of the switch, which is what stops "drop the tenant term unconditionally" passing.
  - **H014** (restore child-entity operations for many-to-many) → **4 of 59** (Aggregates). Fix-dependent: `Removing_a_category_deletes_the_junction_row_not_the_shared_category`, `Adding_an_existing_category_inserts_a_junction_row_not_a_duplicate_category`, `Swapping_one_category_for_another_emits_two_junction_operations`, `ExpandAsync_produces_the_same_junction_operations`. The three one-to-many pins and `An_unchanged_category_emits_nothing` stayed green — **contract pins**, and they are what stops "always emit a junction row" passing.
  - **H014-refusal** (remove the keyless-child refusal) → **1 of 59**, exactly `A_desired_category_with_no_Guid_is_refused_rather_than_half_written`. Run separately because the mutation above leaves the refusal intact, so without this the guard would be untested.
  - ⚠ **H011 alone does NOT red the H009 theory, and that is informative rather than a gap.** With the Create arm fixed but the hashes stale, run 1 really does upload, so run 2 sees both sides present and takes the Update path — the delete branch is never reached. H009 is a *consequence* that needs H008 broken to reproduce, which is why the H011 flags are asserted directly as well.
  - ⚠ **The `Through<TJunction>` generic constraint is structural, not witnessed.** Removing it reds nothing (the only call site's junction already derives from `AbstractModel`), so it is pinned by reflection in `Through_constrains_its_junction_type_to_AbstractModel` — stated as defensive per § TASK-261 rather than claimed as evidence.
- step 7 — respecced `data-sync` (the area's globs cover all five changed files). Requirements changed: **"Create is applied only under Download or Upload direction" → "Create is applied to whichever side is missing the entity"** (retitled: the old title asserted the defect) with 4 scenarios rewritten; **"Conflict resolution is applied only when both items are materialised" → "Conflict resolution writes on the winning side, including one-sided conflicts"** (retitled, 3 scenarios); **new "The knowledge row describes the state after the action was applied"** (3 scenarios); *Batching, item capping and cancellation* — the async half of "Cancellation mid-run keeps recorded errors visible" plus a new scenario; **"Tenant-scoped knowledge stores filter differently in CosmosDB and RavenDB" → "…treat a null tenant as \"every tenant in the scope\""** (retitled, 3 scenarios); *Collection expansion inserts unkeyed children unconditionally* narrowed to `OneToMany`, plus a **new "Many-to-many expansion emits junction rows, never the child entity"** (3 scenarios). Provenance restamped and `CosmosSyncKnowledgeQuery.cs` added to `sources`. **Diff reviewed — nothing unintended; every change is a requirement the code now contradicts, and four of the six were titled as the defect.** Verdicts for all 7 findings written into `SPEC-HARVEST-FINDINGS-2026-07-30.md`.

## Outcome

**What the fix was.** Birko's data sync silently lost data on its **default** setting. `SyncOptions.Direction`
defaults to `Bidirectional`, and the batch processor's "create this entity" branch only knew how to handle
`Download` and `Upload` — so under the default, a brand-new entity was counted as processed, given a
bookkeeping row, and **never written anywhere**. That bookkeeping row then recorded the destination as
*deleted*, so the *second* sync read it as a deletion and, under `RemoteWins`, deleted the user's original
row. The conflict path that should have rescued it could not: every arm required both copies of the entity to
exist, and a conflict is only ever raised when one of them does not. Four findings, one interlocking chain,
fixed together. Three more, independent: a cancelled async run threw away the bookkeeping for everything it
had already written; the CosmosDB knowledge store's tenant filter matched *no* documents whenever no tenant
was in scope; and removing a category from a product emitted "delete the Category" instead of "delete the
association", so a caller applying the operation destroyed a row shared with every other product.

**Verdicts: 6 confirmed, 1 confirmed-narrower, 0 refuted outright.** Full evidence per finding is in
`SPEC-HARVEST-FINDINGS-2026-07-30.md`; the step-3 log above carries the trace.

**Step-6 split (7 disjoint mutations, each reverting one root cause):** H008 -> 7 of 64 . H011 -> 3 of 64 .
H010 -> 4 of 64 . H012 -> 3 of 64 . H013 -> 4 of 14 . H014 -> 4 of 59 . H014-refusal -> 1 of 59.
Fix-dependent tests and contract pins are named individually in the step-6 entry. **220/220 green across 10
suites after restore**, 38 new tests.

### Judgement calls, and why the stricter option was rejected

- **`SH-H013` was narrowed rather than accepted as filed, and that is the most consequential call here.** The
  finding says RavenDB and CosmosDB are both wrong "in opposite directions". Measured against the sole
  caller, only Cosmos is. `TenantSyncProvider.ResolveTenantScope` produces a null tenant in exactly two
  places — an explicit `IsAllTenantsScope`, and an entity with no tenant property — and both mean *do not
  filter by tenant*. So Raven's conditional predicate is correct, and its scope-wide delete under a null
  tenant is the framework's sanctioned all-tenants path, not a fail-open. "Fix both" was the stricter option
  and it would have **broken** the all-tenants path on the one backend that had it right. Raven's files are
  untouched.
- **The Create arm dispatches on presence, not on `Direction`.** The narrower fix — add a `Bidirectional`
  arm beside the other two — was rejected because the direction is *already* applied upstream by
  `DetermineSyncAction`, so a third arm would be a second place where direction is decided, which is the
  drift shape § Conventions keeps recording. Dispatching on which side is missing is behaviour-preserving for
  Download and Upload (verified: all 43 pre-existing tests stayed green) and is the only thing that also
  covers the initial-sync branch.
- **The one-sided conflict arms copy `DetermineSyncAction`'s own shortcuts rather than inventing semantics.**
  `LocalWins` on a local-only / remote-deleted entity already means "re-create on remote" in that method; the
  conflict path now means the same thing. Anything else gives one feature two answers (§ TASK-274).
- **`CancellationToken.None` on knowledge persistence, not "skip persisting when cancelled".** The stricter
  reading — a cancelled run should write nothing — is wrong here: the entity writes have *already
  happened*, so discarding their bookkeeping leaves the next run to re-decide them blind, which is how
  `SH-H009` destroys rows. The synchronous twin has always done it this way; this makes the two halves of one
  contract agree, and that twin's behaviour is pinned as a contract pin rather than claimed as evidence.
- **A keyless many-to-many child is refused, not half-written.** The `OneToMany` path inserts an unkeyed child
  unconditionally (CR-H041), and copying that here would emit a child insert with **no association** — the
  exact silent half-write the finding is about. A junction row cannot reference a child with no key, so it
  throws and the message names the remedy (§ SH-H037). The `OneToMany` behaviour is unchanged and pinned.
- **`Through<TJunction>` gained a `where TJunction : AbstractModel` constraint.** A breaking API change,
  affordable because it is the loud direction (a compile error) and reach is **0 non-test call sites** across
  the framework and all 16 consumer repos. Without it the builder accepts a junction the mapper must then
  refuse at runtime — two answers for one question. It is structural: removing it reds nothing, so it is
  pinned by reflection and labelled defensive-not-witnessed (§ TASK-261) rather than claimed as proven.
- **The Cosmos predicate moved to one producer (`CosmosSyncKnowledgeQuery.ApplyScope`) rather than being
  fixed inline four times.** Four inline copies of one rule is what this codebase repeatedly pays for — and
  the extraction is also what made the defect **offline-testable**.

### Things worth carrying

- **`CosmosSyncTenantScopingTests` asserts, in prose, that "the LINQ filter itself needs a live Cosmos DB".
  Measured false for the filter's *text*.** Microsoft.Azure.Cosmos 3.63.0 renders query SQL with no account
  and no network — `ToQueryDefinition().QueryText` on a container built from an unreachable connection
  string. So the defect was directly assertable all along:
  `SELECT VALUE root FROM root WHERE ((root["Scope"] = "s") AND (root["TenantId"] = null))`. Only *executing*
  a query needs a server. That belief is why this area had no filter coverage at all.
- **Nothing was asserting the old many-to-many expansion.** The 50-test aggregates suite stayed green through
  the entire `SH-H014` fix. A defect that destroys shared rows had zero coverage.
- **`SH-H009` cannot be reproduced from `SH-H011` alone**, and the mutation proves it: with the Create arm
  fixed but the hashes stale, run 1 really uploads, so run 2 sees both sides and takes the Update path. A
  consequence-finding needs its cause broken to fire, which is why the H011 flags are asserted directly too.
- **`SH-H008`'s drop moved no counter at all** — not `Created`, not `Skipped`, only `Processed`. The finding
  did not note that, and it is why nothing ever noticed. There is now a test asserting
  `Created + Updated + Deleted + Skipped == TotalProcessed`.
- **Every "silent loss" claim is asserted as observed state**, per this task's own criterion: rows counted in
  the destination store, knowledge fields read back, the rendered SQL, the emitted operation's `EntityType`
  — never "no exception was thrown".

### Flagged, not fixed

- [[TASK-445]] (P3, spawned here) — `ConflictResolution.Merge` is a silent no-op: no `case`, no write, no
  counter, no error, so a resolver asking for a merge is indistinguishable from a dropped item. Same
  silent-drop family, but implementing a merge is a **feature**, not this fix, and inventing merge semantics
  was outside scope. Documented on `ApplyConflictResolution` and specced as *"Merge resolution is inert"*
  meanwhile, so it cannot be believed fixed.
- **`GetVersionHash`'s random fallback** (`Guid.NewGuid()` when an entity has no `UpdatedAt`) makes knowledge
  hashes non-comparable across runs. Pre-existing, already specced as deliberate shipped behaviour with its
  own scenario, and not raised by this area's findings — left alone rather than widened into.

- step 8 — closed done; production a527109 / 1f8ef41 / a1d475d, tests 3ce26b7 / 645b7af / 085acfc, aggregator this commit
