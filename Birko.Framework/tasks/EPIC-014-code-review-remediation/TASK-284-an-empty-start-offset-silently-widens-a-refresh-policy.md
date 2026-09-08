---
id: TASK-284
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-26
depends-on: []
blocks: []
related: [TASK-281, TASK-260]
findings: []
pr: "Birko.Data.Migrations.TimescaleDB d381593 + .Tests a0f2b60"
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

- [x] **Measure first:** send a bare untyped `NULL` as `start_offset` to `add_continuous_aggregate_policy`
      on live TimescaleDB and record whether it is accepted, and what it does. The whole of finding 2 rests
      on this being unknown — it may be fine, and saying so with a measurement is a valid outcome.
- [x] If it is accepted, a **live** test covers it: a policy created with a null `startOffset`, asserted
      from `timescaledb_information.jobs`, so the door the refusal names is proven to open.
- [x] If it is rejected, the refusal message stops naming it and the emitter either types the literal
      (`NULL::interval`) or refuses the combination — decided from the measurement, not from taste.
- [x] `""` no longer means "from the beginning of time": the null check becomes `startOffset == null`, so an
      empty string fails like every other interval in this class (`22007`). State the behaviour change on
      the method.
- [x] Proven able to fail: a mutation restoring `IsNullOrEmpty` reds a test that passes `""` and expects a
      refusal.
- [x] The two behaviours — `null` means all history, `""` is an error — are stated on
      `AddContinuousAggregatePolicy`'s remarks, since the caller has no other signal that they differ.

## Out of scope

- The moving-window semantics themselves — **[[TASK-281]]** documented and pinned those; this task does not
  change what a non-null `startOffset` means.
- The aggregate's projection and grouping surface — **[[TASK-260]]** owns that.
- The other interval parameters, which already fail loudly on an empty string and need nothing.

## Human test plan

- [x] N/A — mechanical; the proof is a live policy row for the null case and a loud failure for the empty
      one, with a mutation reding the latter.

---

## Worked 2026-09-08 — both findings settled by measurement, one of them the opposite way round

### Step 0 — four probes against live TimescaleDB 2.29.2, before a line changed

| probe | result |
|---|---|
| **A** — bare untyped `NULL` as `start_offset` (the door the refusal names) | **accepted**; job created, config `"start_offset": null` |
| **B** — what the emitter produced for `""` | **`start_offset: null`** — silently all of history |
| **C** — what `""` produces after the fix | **`ERROR: invalid input syntax for type interval: ""`** (`22007`) |
| **D** — control, a real offset | bounded: `30 days` |

### Finding 2 was a false alarm, and that is a result

`add_continuous_aggregate_policy` declares its parameters `"any"`, so an untyped bare `NULL` is exactly
the shape a server can reject — which would have made `RefreshContinuousAggregate`'s refusal
§ SH-H037's *wall wearing a door's label*. **It is accepted.** The criterion allowed for this
(*"it may be fine, and saying so with a measurement is a valid outcome"*), so the fix is not to change the
message but to give the door a **live** test, since it had only ever been asserted as a string.

### Finding 1 confirmed and fixed

`string.IsNullOrEmpty` → `startOffset == null`. `""` now reaches `INTERVAL ''` and fails loudly like every
neighbouring interval in the class. Both behaviours are stated on `AddContinuousAggregatePolicy`'s remarks,
because the caller has no other signal that they differ and **the wrong one fails by working**.

### ⚠ The suite actively encoded the defect

`ContinuousAggregatePolicy_EmitsABareNullStartOffset` was a `[Theory]` with `[InlineData(null)]` **and
`[InlineData("")]`**, asserting both emit `start_offset => NULL`. So the empty-string behaviour was not an
oversight that testing missed — it was a documented, asserted contract. Split into two `[Fact]`s that
assert the two behaviours differ.

### Verified

`BIRKO_REQUIRE_LIVE` set against live **TimescaleDB 2.29.2 / PostgreSQL 16**, plus PostgreSQL 16, SQL
Server 2022, MySQL 8.4 and on-disk SQLite: **1,363 tests, 0 failed** across seven suites —
Migrations.TimescaleDB 84 (was 80), TimescaleDB 56, TimescaleDB.ViewModel 7, SQL 678, SQLite 344,
PostgreSQL 107, Migrations.SQL 87.

**Mutation:** restoring `IsNullOrEmpty` reds exactly two — the rendering test and its live twin — leaving
the null-acceptance test and the bounded-offset control green.

### ⚠ Two fixture faults of mine, both instructive

- The first version of the live helper asked for *"the only refresh policy in the database"*, and read a
  leftover from my own psql probe as the test's answer. Scoped to the view.
- The obvious catalogue join was **wrong**: a job's config carries `mat_hypertable_id`, so joining
  `jobs.hypertable_name` to `continuous_aggregates.materialization_hypertable_name` looks right. Measured:
  `jobs.hypertable_name` is the **view** name (`OffDailyStats`) while the materialisation hypertable is
  `_materialized_hypertable_16`, so the join matched nothing and every assertion read `<none>` — a green-
  looking shape that was actually measuring nothing. Filtering on the view name is correct and simpler.
