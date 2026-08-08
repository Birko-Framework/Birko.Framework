---
id: TASK-147
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: human
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

- [ ] **Decide** between: validate on attach (and how, without reintroducing the CR-M172 N+1 — e.g. one
      batched ownership check per `SetEntityTagsAsync` call rather than one per tag); leave creation
      unvalidated and rely on the read-side guard; or refuse only the *cross-tenant* case while still
      tolerating a dangling id
- [ ] The reasoning is recorded **including the per-call cost of the rejected option** — CR-M172's
      measurement is the baseline and must not be silently undone
- [ ] If validation is added: `AttachTagAsync`, `AttachTagByNameAsync` and `SetEntityTagsAsync` behave
      consistently, and a foreign `TagId` is refused with `CrossTenantTagAccessException`, for symmetry
      with the read paths
- [ ] A **dangling** (non-existent) `TagId` is decided explicitly too — it is the same hole and is today
      equally uncaught; deciding only the tenant half would leave the task half-done
- [ ] Either way a test pins the chosen behaviour, and the `entity-tagging` spec scenario above is
      regenerated to match

## Out of scope

- The tenant guard on the read/delete paths ([[TASK-126]], closed).
- Auditing consumer implementations of `TagServiceBase`.

## Human test plan

- [ ] If validation is added, attach several tags at once through `SetEntityTagsAsync` and confirm the
      number of tag lookups did not grow per tag. The regression this task must not cause is a
      *performance* one, and it will not show up in any correctness assertion.

## Implementation plan

_Populated by `/tasks plan TASK-147` — leave empty until then._
