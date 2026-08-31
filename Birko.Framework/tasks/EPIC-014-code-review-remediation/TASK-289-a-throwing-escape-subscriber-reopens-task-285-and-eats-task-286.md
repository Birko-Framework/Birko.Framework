---
id: TASK-289
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: unassigned
created: 2026-08-31
depends-on: []
blocks: []
related: [TASK-283, TASK-285, TASK-286, TASK-287, TASK-288]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.SQL]
---

# A throwing `OnSchemaEscapeDetected` subscriber reopens TASK-285 and destroys TASK-286's annotation

Found at [[TASK-288]]'s close, by asking whether that work had spawned anything — i.e. by running the gate
that should have run before it closed. Same family as [[TASK-283]] (`OnIndexCreationFailed`), on the event
[[TASK-287]] added a commit earlier, with a **different and larger** blast radius.

⚠ **P1 rather than P2, unlike its sibling**, for one reason: TASK-287 and TASK-288 are not observable in
production until consumer Symbio subscribes to this event, so a Symbio task is about to be opened that
tells someone to write exactly the handler that triggers this. The hole is being actively walked into.

## Measured (2026-08-31, SQLite, forced condition)

Subscribe `_ => throw new InvalidOperationException("host escalated")` to `OnSchemaEscapeDetected`, create
the table, drop it beneath the connector, then:

| path | result | contract |
|---|---|---|
| `SelectCount` | **threw `InvalidOperationException`** | ⚠ **TASK-285 reopened** — the count of a missing table must be `0` |
| `Insert` | threw `InvalidOperationException`, **annotation absent** | ⚠ **TASK-286 destroyed** — the escape diagnostic is replaced by the handler's exception |
| `SchemaEscapes` after | **1** entry | intact |
| `SchemaGeneration` after | **1** | intact |
| next write | **OK**, table recreated | ✅ TASK-288's heal survives |

## Mechanism

`RecordSchemaEscape` raises the event, and `EnsureSchemaAndReport` calls it **between** building the
annotated exception and throwing it:

```csharp
var reported = new Exception(DescribeSchemaEscape(ex, commandText), ex);
RecordSchemaEscape(reported, CreatedTablesNamedIn(commandText).Select(kvp => kvp.Key));  // ← raises
throw reported;                                                                          // ← never reached
```

So the subscriber's exception **replaces** `reported`. Two consequences, and the second is the one that is
easy to miss:

1. The annotation never reaches the caller, so the instrument TASK-286/287/288 exist to provide is
   destroyed by the handler that was written to read it.
2. On the count path the replacement no longer satisfies
   `catch (Exception ex) when (IsMissingTableExceptionChain(ex))` — the **exception filter does not match**,
   the catch never runs, and `SelectCount` throws instead of returning `0`. A host subscriber can therefore
   reopen TASK-285's intermittent 500 without touching the framework.

## Why the heal survives, and why that matters

`Interlocked.Increment(ref _schemaGeneration)` happens **before** the event is raised, and the
`SchemaEnsureFailureLog.Record` call happens before it too. So both the record and the invalidation are
already done when the subscriber throws, and TASK-288's healing is unaffected — measured, write #2
succeeded and the table came back.

That is luck of ordering rather than design, and it should become design: the ordering is currently
implicit and a later edit that moves the `Invoke` above the increment would silently take the heal with it.

## Why this is separable from [[TASK-283]] — and cheaper

TASK-283 deliberately did **not** fix the index channel because `IndexCreationFailures` /
`OnIndexCreationFailed` are consumed by Symbio in production code, its host, two test files, its
`CLAUDE.md` and its specs — so changing whether a handler's exception propagates is a behaviour change on
consumed surface, needing the measurement that epic has three precedents for (TASK-248, TASK-256,
TASK-254).

**`OnSchemaEscapeDetected` has zero consumers.** It shipped hours ago and nothing subscribes to it in the
framework, its tests, or any consumer repo. That is exactly the situation TASK-254 was in when it hardened
the *new* hypertable channel for free — so this half can be fixed now, and should be, before the first
subscriber exists.

⚠ **That window closes the moment Symbio subscribes**, which is imminent. Same shape as TASK-214's "a
migration objection can be measured away — and check whether the cost is about to become real".

## Acceptance criteria

- [ ] A subscriber that throws cannot change what the caller sees. `SelectCount` still returns `0`; the
      write still throws with TASK-286's annotation intact.
- [ ] The subscriber's own exception is **not** silently discarded — the whole point of the channel is
      reporting, and swallowing a handler defect in the reporting channel is the same failure one level up.
      ⚠ Decide where it goes and write the reason down. Candidates: `AggregateException` as an inner of the
      reported exception (loses the count path's `IsMissingTableExceptionChain` match unless nested
      carefully), or a separate, documented last-resort sink. **Measure the count path's exception filter
      against whatever is chosen** — that filter is what silently broke here.
- [ ] The ordering that saves TASK-288's heal becomes explicit rather than incidental: record and increment
      before any subscriber runs, with a test that fails if an edit reverses it.
- [ ] Mutation-proven: restore the raw `Invoke` and watch a forced anomaly with a throwing subscriber take
      down the count path, while the same suite with no subscriber stays green.
- [ ] ⚠ Assert the **no-subscriber** and **non-throwing-subscriber** paths too, unchanged. A guard that
      hardens by swallowing everything would pass a throwing-subscriber test and break the channel.

## Out of scope

- **[[TASK-283]]'s index channel.** Same defect, consumed surface, its own measurement first. This task
  should be cited there as a third instance and as evidence for the eventual shared answer — but do **not**
  change `OnIndexCreationFailed` here, and do not "unify" the two from symmetry before TASK-283 has run its
  consumer re-measurement.
- **The `OnIndexCreationFailed` / hypertable precedent set by TASK-254** — already hardened, nothing owed.

## Human test plan

- [ ] N/A — mechanical. The verdict is what `SelectCount` returns and whether the thrown message still
      carries `schema-ensure escape`, both assertable.

## Implementation plan

_Populated by `/tasks plan TASK-289` — leave empty until then._
