---
id: TASK-283
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-25
depends-on: []
blocks: []
related: [TASK-204, TASK-254, TASK-276, TASK-289]
findings: []
pr: cb9773a (Birko.Data.SQL) + bf045f4 (Birko.Data.TimescaleDB) + 9eb46a7 (SqLite.Tests) + 0102408 (TimescaleDB.Tests)
github-issue: null
jira-key: null
affects: [Birko.Data.SQL, Birko.Data.TimescaleDB]
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

## Third instance, 2026-08-31 — [[TASK-289]]

[[TASK-287]] added `OnSchemaEscapeDetected` and it has the identical hole, measured rather than reasoned:
a throwing subscriber replaces the reported exception, so the write loses TASK-286's annotation and the
count path's `catch … when (IsMissingTableExceptionChain(ex))` **filter stops matching**, making
`SelectCount` throw instead of returning `0` — i.e. a host subscriber can reopen [[TASK-285]] without
touching the framework. Filed as [[TASK-289]] and **P1**, not because it is worse in kind but because that
channel has **zero** consumers, so it is in TASK-254's free-to-harden position rather than this one's.

⚠ **That makes three channels and two policies, which is the thing to resolve here rather than per event.**
Whatever this task measures for the consumed index channel should become the single answer for all of them;
do not let TASK-289 and TASK-254 set a de-facto convention by being the cheap ones. But equally, do not
"unify" `OnIndexCreationFailed` from symmetry before the consumer re-measurement this task's own first
acceptance criterion requires.

---

## Closed 2026-09-03 — and criterion 1 inverted the task's own premise

### Step 0: the blast radius that justified leaving this alone was stale

The task's central claim — and TASK-254's reason for hardening the hypertable channel and **not** this one
— was that `OnIndexCreationFailed` is *"consumed by Symbio in production code, its host, two test files,
and as a documented contract"*, so changing whether a handler's exception propagates would be a behaviour
change on consumed surface.

Re-measured across all 16 consumer repos, which criterion 1 exists to demand:

| the claim | measured 2026-09-03 |
|---|---|
| consumed in production code (`UniqueIndexDataCheck.cs`) | a **doc comment** |
| consumed by the host (`Program.cs:766`) | a comment saying *"**Not** read from `AbstractConnector.IndexCreationFailures`"*, with its reason |
| two test files | one reads the **collection**; the other mentions the event in a doc comment |
| **`OnIndexCreationFailed +=` subscriptions, all 16 repos** | **0** |

**There is no handler.** Nothing can throw from a channel nobody subscribes to, so this sat in exactly
TASK-254's position — free to harden — and had done since it was filed. Criterion 2 ("establish whether any
consumer's handler can actually throw today") answers itself, and criterion 4's decision has no
consumer intent to respect.

⚠ **The distinction that survives the re-measurement, and that the fix must not touch:** the *collection*
`IndexCreationFailures` **is** consumed — one real read, `Symbio.Tests.Unit/V1InertnessTests.cs:332`. The
event is not. Those are different contracts and the stale count conflated them.

### The fix

`RecordIndexCreationFailure` now raises through `AbstractConnector.RaiseDiagnostic` (TASK-289's helper)
instead of a bare `Invoke`.

⚠ **And the hypertable channel moved with it, narrowing this task's own "out of scope" bullet
deliberately.** That bullet says TASK-254 fixed it — true, it was not broken. But it was fixed
*differently*: a single `try { Invoke } catch { }` that swallows without recording. Leaving it there would
have left two policies side by side, which is precisely the silent divergence **criterion 6** forbids, and
TASK-289 had already recorded that the general helper exists so channels "adopt it rather than add a third
variant". Measured free: **0** consumer subscriptions to `OnHypertableCreationFailed` either; the only
subscribers are this project's own tests. All three diagnostic channels now share one implementation.

Two behaviours change on the hypertable channel, both corrections:

- **per subscriber, not one `try` around the multicast** — a single `try` stops at the first delegate that
  throws, so a host with a logger and a metric loses the metric to a bug in the logger;
- **swallowed now means recorded**, on `SubscriberFailures`. TASK-254's comment argued the opposite
  ("their own handler failing is their concern, not a second schema failure"); TASK-289 overturned that
  reasoning on the grounds that a broken handler and an event that never fired look identical from
  outside. That overturning is now applied where it was first argued.

### Acceptance

- [x] Consumer surface re-measured first — and it inverted the premise.
- [x] Whether a consumer's handler can throw: **no consumer handler exists**, on any of the 16 repos.
- [x] The degrade is unconditional: a throwing subscriber does not leave the store uninitialised.
- [x] Swallowed **and recorded** on `SubscriberFailures`, keyed by (channel, exception type). The sink is
      deliberately a collection and not another event — announcing a subscriber failure through a
      subscriber is the same hole one level up.
- [x] Proven able to fail, with disjoint mutations.
- [x] The two channels end up **consistent** — one implementation, not a documented divergence.

### Measurements

`BIRKO_REQUIRE_LIVE` set, live PostgreSQL 16 / MySQL 8.4 / SQL Server 2022 / TimescaleDB 2 and on-disk
SQLite: **1,619 tests, 0 failed, 0 skipped** across eleven suites — SqLite **341** (337 → 341),
TimescaleDB **56** (55 → 56), `Birko.Data.SQL` 667, PostgreSQL 98, MySQL 101, MSSql 111,
Migrations.SQL 54, Migrations.TimescaleDB 81, InMemory 69, JSON 23, XML 18.

**Mutations, disjoint:**

| mutation | red |
|---|---|
| index channel back to a bare `Invoke` | **4 of 13** — the degrade, the recorded subscriber failure, the per-subscriber isolation, and the collection contract |
| hypertable channel back to TASK-254's single swallowing `try` | **2 of 56** — the recorded failure and the per-subscriber isolation; the degrade itself stayed green, which is the point: that half was already right |

### ⚠ A fixture trap worth recording

The TimescaleDB suite first reported **15 of 17 failing**, and the cause was the container rather than the
change: `pg_isready` answers **during initdb**, before the server restarts for real, so a readiness loop
built on it hands back a database that is about to go away. Rerunning against a genuinely-up server gave
56/56 twice. The fix is to wait on an actual query (`psql -c "SELECT 1"`), which the later sweeps do —
and it is the same class as § TASK-259's "I fell into the skip-as-failure trap I had documented one task
earlier".

This sweep also ran with a **trx logger**, which is the correction [[TASK-276]] asked for after the
previous run lost an MSSql failure's identity to a summary-only grep.

### Deliberately not done

- **`SchemaEnsureFailureLog<T>` still does not raise.** The invoke stays in each caller so every channel
  keeps its own event type and public surface — this task's own out-of-scope note, and it still holds.
- **No consumer change.** Symbio subscribes to neither event; the collection it does read is untouched.
