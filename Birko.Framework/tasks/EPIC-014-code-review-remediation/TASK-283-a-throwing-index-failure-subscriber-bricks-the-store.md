---
id: TASK-283
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-25
depends-on: []
blocks: []
related: [TASK-204, TASK-254]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# A throwing `OnIndexCreationFailed` subscriber defeats TASK-204's degrade and bricks the entity

Found by `code-review` at **[[TASK-254]]**'s close gate. That task fixed the identical hole in the *new*
hypertable channel and deliberately left this one alone — see *Why it was not fixed there* below.

## What is wrong

`AbstractConnector.RecordIndexCreationFailure` raises `OnIndexCreationFailed` **inside** the `catch` that
implements TASK-204's degrade:

```csharp
catch (Exception ex)
{
    RecordIndexCreationFailure(table.Name, index?.Name, ex);   // ← invokes the event
}
```

So a subscriber that throws propagates out of schema-ensure, and stores set `_initialized` only *after*
schema-ensure returns — leaving the entity's whole surface, **reads included**, throwing on every later
operation. That is exactly the failure TASK-204 exists to remove, reintroduced through the channel that
reports it.

**The trigger is realistic rather than theoretical.** The event's own summary invites a host to *"subscribe
to log or escalate"*, and escalating by rethrowing is an ordinary thing to write. A host that turns a
recorded index failure into a fatal startup error gets the failure it asked for — plus a permanently dead
entity it did not.

## Why it was not fixed in TASK-254

The hypertable channel added there had **zero consumers**, so hardening it was free. This channel does not:
`IndexCreationFailures` / `OnIndexCreationFailed` are consumed by Symbio in production code
(`Symbio.DataAccess/Sql/UniqueIndexDataCheck.cs`), its host (`Symbio.Api/Program.cs`), two test files, and
as a documented contract in its `CLAUDE.md` and `docs/specs/core-kernel.md`.

Changing **whether a handler's exception propagates** is therefore a behaviour change on consumed surface,
and this epic has three precedents for measuring that before acting (TASK-248, TASK-256, TASK-254 itself).
Swallowing silently could hide a handler defect a consumer currently relies on seeing.

## Acceptance criteria

- [ ] Re-measure the consumer surface first — the counts above were taken 2026-08-24/25 and § Conventions
      (TASK-259) is explicit that a stale blast radius is how a wrong claim reaches a commit message.
- [ ] Establish whether any consumer's `OnIndexCreationFailed` handler can actually throw today. If one
      deliberately escalates, the fix is a **decision** about which behaviour is correct, not a silent
      hardening.
- [ ] The degrade becomes unconditional: a throwing subscriber must not leave the store uninitialised.
      TASK-254's shape — `try { Invoke(...) } catch { }` around the invoke only — is the obvious candidate,
      and its reasoning is written on `TimescaleDBConnector.RecordHypertableCreationFailure`.
- [ ] Decide and record whether the handler's exception is **swallowed** or **surfaced somewhere else** (a
      separate channel, a log hook). Swallowing is what TASK-254 chose, on the grounds that the caller's own
      handler failing is not a second schema failure — but that was for a channel with no consumers.
- [ ] Proven able to fail: a subscriber that throws, asserted not to brick the store, with a mutation that
      reds it. TASK-254's `A_throwing_subscriber_does_not_defeat_the_degrade` is the model.
- [ ] The two channels end up **consistent, or explicitly and documentedly different** — a silent divergence
      between the index and hypertable channels is the drift this epic keeps recording.

## Out of scope

- The hypertable channel — **[[TASK-254]]** fixed it and its reasoning is recorded on the method.
- `SchemaEnsureFailureLog<T>` itself: the invoke happens in the *caller*, not the helper, deliberately so
  each channel keeps its own event type and public surface.

## Human test plan

- [ ] N/A — mechanical; the proof is a throwing subscriber leaving a usable store, with a mutation that reds
      the assertion.
