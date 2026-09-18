---
id: TASK-148
parent: STORY-046
feature: FEATURE-017
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: blocked
priority: P3
assignee: ai
created: 2026-08-08
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `ScopeRestorationBehavior` for the distributed-consumer dispatch path

> **Blocked 2026-08-08 — on an external condition, not on another task.** No distributed transport
> currently dispatches these handlers: the in-memory outbox path is the one in use, and it already
> restores scope (`Birko.EventBus.Outbox@ec4ceb9`). Unblock with `/tasks unblock TASK-148` when a
> distributed / MQTT transport is actually adopted. Held out of the ready pool deliberately so
> `fix-next` does not pick work that cannot be verified against a real dispatch path.

## Context

The last outstanding item in [[STORY-046]], recorded there under *"Not done yet (framework follow-up)"*
and filed as a task on 2026-08-08 — previously it existed only as a story bullet, which is invisible to
`pick`, the snapshot and `fix-next`.

**What already works.** Under `TenantIsolationMode.Strict`, async event handlers used to throw, because
the outbox processor and MQ consumer dispatch outside the request's async flow and so carry no ambient
tenant. STORY-046 closed that for the paths in use:

- `IEventScopeAccessor` + `NullEventScopeAccessor` in `Birko.EventBus` (`e2eab6c`) — a transport-agnostic
  seam with a no-op default, so `Birko.EventBus` stays tenant-agnostic.
- `OutboxProcessor` re-establishes scope from `entry.TenantGuid` before re-publishing (`ec4ceb9`),
  red→green verified by temporarily reverting the wrap.
- `Birko.EventBus.Tenant` bridges both halves (`8ac24f4`, `6967992`): a consume-side accessor and a
  publish-side `TenantEventEnricher`, so `OutboxEntry.TenantGuid` is correct for every publish flow, not
  only authenticated HTTP.

**What is missing.** A `ScopeRestorationBehavior : IEventPipelineBehavior` for the **distributed-consumer**
path. The outbox fix restores scope inside the processor; a distributed transport (MQTT, or any
out-of-process consumer) dispatches handlers through the pipeline instead, and nothing there re-establishes
the ambient tenant. Under Strict those handlers would throw exactly as the outbox ones did before `ec4ceb9`.

**Why it is blocked rather than todo.** The gap is unreachable today — it needs a transport that does not
yet dispatch these handlers. Building it now would mean writing a guard with no way to prove it works
end-to-end, which is the shape of fix this family has repeatedly found to be worse than none. The seam it
plugs into already exists, so the work is small when the trigger arrives.

## Acceptance criteria

- [ ] `ScopeRestorationBehavior : IEventPipelineBehavior` restores ambient scope from the event's
      `TenantGuid` before the handler runs, mirroring `OutboxProcessor.ProcessBatchAsync`
- [ ] It uses the existing `IEventScopeAccessor` seam rather than taking a dependency on
      `Birko.Data.Tenant` — the layering STORY-046 established (proven there by a test using an
      AsyncLocal stand-in) must not be broken by this
- [ ] Registering it is opt-in and its absence changes nothing, matching `AddOutbox`'s pattern: resolve
      the accessor from DI if present, no-op otherwise
- [ ] Red→green proven against a **real** distributed dispatch, not a simulated one — the reason this
      task waits is that a simulated proof is worth little here
- [ ] The null/`Guid.Empty` tenant decision matches the consume-side accessor's existing behaviour
      (`null` → all-tenants, `Guid.Empty` → that tenant per `6967992`), or diverges deliberately with the
      reason recorded — two answers to "what is an unattributed event" is its own defect
- [ ] `AddEventTenantScope()` registers it alongside the accessor and enricher, so adopters get all three

## Out of scope

- Consumer adoption (`AddEventTenantScope()` + flipping to `Strict`) — per-consumer work in each
  consumer's own `tasks/`, per the polyrepo split; for Symbio that is its `TASK-156`.
- The outbox path ([[STORY-046]], landed) and the publish-side enricher.

## Human test plan

- [ ] With a distributed transport wired and `TenantIsolationMode.Strict` enabled, publish an event from
      tenant *t* and confirm the out-of-process handler observes *t* — not "no tenant" and not another
      tenant. This is the assertion the task exists for and the one that cannot be made today.

## Implementation plan

_Populated by `/tasks plan TASK-148` — leave empty until then._
