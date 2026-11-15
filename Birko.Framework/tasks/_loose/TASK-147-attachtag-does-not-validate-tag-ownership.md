---
id: TASK-147
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `AttachTagAsync` validates neither a tag's existence nor its ownership

## Context

Filed from [[TASK-126]]'s Outcome, where it was flagged as remaining after the tenant guard landed.

[[TASK-126]] made `TagServiceBase` re-check the tenant of every record its data-access hooks return, so a
cross-tenant **read** now throws and a cross-tenant list is filtered. `AttachTagAsync` was left alone: it
creates an `EntityTag` stamped with the current tenant and a caller-supplied `TagId`, **without loading
the tag at all**. So a link owned by tenant *A* can still be created pointing at tenant *B*'s tag — or at
a tag id that does not exist.

After TASK-126 the consequence is *loud* rather than a leak: `GetEntityTagsAsync` resolves each link by
identity, and the guard throws when the resolved tag is foreign. So the bad link is creatable and then
poisons reads of that entity's tags. The spec records this precisely — `entity-tagging`, scenario *"A link
may still reference another tenant's tag while carrying this tenant's stamp"*.

**The trade is real, which is why this is a decision and not a defect fix.** Validating means an extra
load per attach, and `SetEntityTagsAsync` attaches in a loop where **CR-M172 deliberately removed** a
redundant per-tag query to avoid exactly that N+1. A naive fix reintroduces a cost that was already
measured and rejected once.

## Acceptance criteria

- [x] **Decide** between: validate on attach (and how, without reintroducing the CR-M172 N+1 — e.g. one
      batched ownership check per `SetEntityTagsAsync` call rather than one per tag); leave creation
      unvalidated and rely on the read-side guard; or refuse only the *cross-tenant* case while still
      tolerating a dangling id
- [x] The reasoning is recorded **including the per-call cost of the rejected option** — CR-M172's
      measurement is the baseline and must not be silently undone
- [x] If validation is added: `AttachTagAsync`, `AttachTagByNameAsync` and `SetEntityTagsAsync` behave
      consistently, and a foreign `TagId` is refused with `CrossTenantTagAccessException`, for symmetry
      with the read paths
- [x] A **dangling** (non-existent) `TagId` is decided explicitly too — it is the same hole and is today
      equally uncaught; deciding only the tenant half would leave the task half-done
- [x] Either way a test pins the chosen behaviour, and the `entity-tagging` spec scenario above is
      regenerated to match

## Out of scope

- The tenant guard on the read/delete paths ([[TASK-126]], closed).
- Auditing consumer implementations of `TagServiceBase`.

## Human test plan

- [x] If validation is added, attach several tags at once through `SetEntityTagsAsync` and confirm the
      number of tag lookups did not grow per tag. The regression this task must not cause is a
      *performance* one, and it will not show up in any correctness assertion.

## Implementation plan

_Populated by `/tasks plan TASK-147` — leave empty until then._

## Decision (2026-11-15, the user)

**Validate on attach, one query per call** — chosen over a new batch hook (`GetTagsByIdsAsync`, cheaper for a
tenant with very many tags but needing an override in every implementation, Symbio's included), refusing only the
cross-tenant case, and leaving creation unvalidated. Both halves are refused: another tenant's tag
(`CrossTenantTagAccessException`, symmetric with the reads) and a nonexistent id (`InvalidOperationException`
`"Tag {id} not found."`, the message `UpdateTagAsync` already gives; now one producer, `TagNotFound`).

**Cost, against CR-M172's baseline** (CR-M172 removed one link re-query per added tag from `SetEntityTagsAsync`):
- `AttachTagAsync`: +1 `GetTagByIdAsync` per call.
- `SetEntityTagsAsync`: +1 `ListAllTagsAsync` per call **when it adds anything**, however many tags; 0 per-tag
  lookups on the happy path (an id the listing lacks is loaded alone, only to word the error). The link query is
  still once per call. Rejected per-tag validation would have cost N lookups for N added tags.
- `AttachTagByNameAsync`: +0 (its tag is already proven; it links without a second load).
- A remove-only `SetEntityTagsAsync`: +0.

## Done (2026-11-15)

`TagServiceAttachValidationTests` (8): foreign and missing refused on attach and on set, set refuses **before**
changing anything (the extra link is not deleted, nothing added), 5 tags added with ListAllTags 1 / GetTagById 0 /
link query 1, remove-only lists nothing, by-name does no second lookup. 6 red before the change, 28/28 tagging tests
after. Spec `entity-tagging` regenerated (scoped, diff reviewed: every change is this decision; unmapped 0;
`shaped-by` unchanged, no new evidence since c50ed580). `Birko.Data.Tagging/CLAUDE.md` and Recent Updates updated.

**Consumer note:** Symbio's `TagEndpoints` passes a client-supplied `TagGuid` to `AttachTagAsync` /
`SetEntityTagsAsync`. A foreign or missing id now throws before storing anything (before: it stored the poisoned
link, then the follow-up read threw). Symbio maps neither exception to a status, so it reaches its global handler;
a 404 mapping (rule 65) is Symbio's to add — told to the Symbio session.
