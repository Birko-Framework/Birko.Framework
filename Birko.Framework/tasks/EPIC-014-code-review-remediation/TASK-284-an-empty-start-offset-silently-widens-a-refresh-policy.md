---
id: TASK-284
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-26
depends-on: []
blocks: []
related: [TASK-281, TASK-260]
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Data.Migrations.TimescaleDB]
---

# An empty `startOffset` silently widens a refresh policy to all of history — and the escape hatch that relies on it is untested

Two findings from `code-review` at **[[TASK-260]]**'s close gate, both against
`AddContinuousAggregatePolicy` / `BuildContinuousAggregatePolicySql`, which **[[TASK-281]]** added. Out of
scope there (TASK-260 replaced the aggregate's projection and grouping, not the policy emitter), so they get
their own id rather than being folded in.

## 1 — `""` and `null` are treated identically, and only one of them should be

`BuildContinuousAggregatePolicySql` uses `string.IsNullOrEmpty(startOffset)` to decide between
`start_offset => NULL` and `start_offset => INTERVAL '…'`. `NULL` means **"refresh from the beginning of
time"**, so a configuration value that comes back as an **empty string** rather than null silently produces
a far heavier policy than the author intended — every chunk, on every run of the job.

**The asymmetry is the tell:** every neighbouring interval in this class — `endOffset`, `scheduleInterval`,
`compressAfterInterval`, `dropAfterInterval`, the chunk interval — fails **loudly** on an empty string,
because `EscapeLiteral("")` renders `INTERVAL ''` and PostgreSQL answers `22007`. Only this one converts an
empty string into a semantically different, much wider policy.

`TimescaleDBSettings`-style config binding, `LoadFrom`, and any JSON/env deserialisation can all produce
`""` where the author wrote nothing.

## 2 — the escape hatch the refusal names has no live coverage

`RefreshContinuousAggregate`'s refusal message tells the caller to *"pass a null startOffset to include
it"*. That path is asserted **only as a string** by
`ContinuousAggregatePolicy_EmitsABareNullStartOffset`; the sole live policy test
(`A_refresh_policy_can_be_attached_inside_the_default_transactional_runner`) passes `"30 days"`.

So an untyped bare `NULL` has never been sent to `add_continuous_aggregate_policy`, whose parameters are
declared `"any"` — precisely the kind of argument a server can reject for being untyped. If it does, an
author following the refusal message hits a **second, unrelated error**, which is exactly the failure
§ SH-H037 records: *a guard whose opt-out throws is a wall wearing a door's label*, and *the opt-out is part
of the fix and needs its own test*.

Note this is the same rule TASK-281 itself invoked when it gave `UseTransaction = false` an end-to-end test
— it simply did not apply it to the second door named in the same message.

## Acceptance criteria

- [ ] **Measure first:** send a bare untyped `NULL` as `start_offset` to `add_continuous_aggregate_policy`
      on live TimescaleDB and record whether it is accepted, and what it does. The whole of finding 2 rests
      on this being unknown — it may be fine, and saying so with a measurement is a valid outcome.
- [ ] If it is accepted, a **live** test covers it: a policy created with a null `startOffset`, asserted
      from `timescaledb_information.jobs`, so the door the refusal names is proven to open.
- [ ] If it is rejected, the refusal message stops naming it and the emitter either types the literal
      (`NULL::interval`) or refuses the combination — decided from the measurement, not from taste.
- [ ] `""` no longer means "from the beginning of time": the null check becomes `startOffset == null`, so an
      empty string fails like every other interval in this class (`22007`). State the behaviour change on
      the method.
- [ ] Proven able to fail: a mutation restoring `IsNullOrEmpty` reds a test that passes `""` and expects a
      refusal.
- [ ] The two behaviours — `null` means all history, `""` is an error — are stated on
      `AddContinuousAggregatePolicy`'s remarks, since the caller has no other signal that they differ.

## Out of scope

- The moving-window semantics themselves — **[[TASK-281]]** documented and pinned those; this task does not
  change what a non-null `startOffset` means.
- The aggregate's projection and grouping surface — **[[TASK-260]]** owns that.
- The other interval parameters, which already fail loudly on an empty string and need nothing.

## Human test plan

- [ ] N/A — mechanical; the proof is a live policy row for the null case and a loud failure for the empty
      one, with a mutation reding the latter.
