---
id: TASK-258
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done  # scope narrowed: criteria 1-2 are unanswerable as shipped and move to TASK-305
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-242, TASK-243, TASK-256, TASK-257, TASK-305]
findings: []
pr: "Birko.Data.SQL.Tests@3a49fef"
github-issue: null
jira-key: null
---

# `retryWhenOwned` claims to preserve each provider's retry policy, and nothing asserts that it does

## Context — the last unverified claim left by TASK-242, and it was never a task

`RunBulkAsync` / `RunBulk` take `retryWhenOwned` (default `true`), which decides whether the
**own-connection** path of a bulk write is wrapped in `ExecuteWithRetryAsync`
(`AbstractAsyncConnector.cs:128-133`, sync twin `AbstractConnector.cs:394`). Its stated purpose is to
*preserve* what each provider already did rather than to change anything:

- **SQLite** takes the `true` default — CR-M144 made `SQLITE_BUSY`/`SQLITE_LOCKED` transient and retried,
  on the reasoning that the whole batch rolls back before the next attempt.
- **PostgreSQL, MySQL, MSSql** pass `retryWhenOwned: false` **explicitly at all 18 of their bulk call
  sites** — those paths never retried.
- The **participating** path never retries regardless of the flag: re-running statements inside a
  transaction whose earlier ones already succeeded can only fail differently, and retrying is the boundary
  owner's decision.

That structure is real and was measured from the call sites. What is *not* measured is the word
**"preserve"** — an equivalence claim against pre-TASK-242 behaviour, asserted by nothing.

⚠ **Why this is filed now rather than left as a checklist line.** It lived as an unticked criterion on
consumer Symbio's TASK-472 and as one descriptive bullet on this repo's TASK-242 — which is `done`, so
nothing revisits it. Per the tracking rule, *a checklist line is filed, not scheduled*: only a `todo`
**task** is ranked by `pick`, the `Next up` snapshot, or `fix-next`. Worse, the criterion sat in the
consumer tree while the work can only happen here, so the one repo able to close it had no ranked item for
it. Symbio's TASK-472 criterion now points at this id.

## Why P2 and not P1

A wrong `retryWhenOwned` degrades **resilience**, not integrity: a lost retry turns a transient lock into a
surfaced error, and a spurious retry re-runs a batch that already rolled back. Neither silently corrupts
data, which is what separates this from TASK-242 (a write escaping its boundary), TASK-256 and TASK-257.
It is the weakest of the family and the last one open — but "weakest" is not "verified".

## Acceptance criteria

- [ ] → **[[TASK-305]]. UNANSWERABLE as shipped, not unmeasured.** *SQLite still retries and converges.*
      Force a transient `SQLITE_BUSY`/`SQLITE_LOCKED` on the own-connection bulk path and assert the
      operation retried *and* ended with the correct row set — not merely that it did not throw. The retry
      only being safe *because the batch rolled back first* is the part to pin: assert no duplicate or
      partially-applied rows after a retried attempt. **There is no policy to retry under** (see the
      finding below), so this becomes a real question only when a default lands — which is TASK-305's
      subject, and TASK-305 carries this criterion verbatim.
- [ ] → **[[TASK-305]]. TRIVIALLY TRUE today, for a reason unrelated to the flag.** *The three server
      providers still do not retry.* Assert their own-connection bulk path makes exactly one attempt, so no
      consumer silently loses a retry it used to have — and none silently gains one. Asserting it now would
      pin the vacuous state and read as if it had verified the real one.
- [x] **The participating path never retries, on any provider, whatever the flag says.** **DONE** -
      `RetryWhenOwnedTests` (`Birko.Data.SQL.Tests/Connectors/`), sync and async, `retryWhenOwned: true`
      passed deliberately with a policy set. Provider-independent, so it is asserted offline rather than
      duplicated across four live suites: the participating branch returns before the flag is read, in both
      halves.
      Plus the half an attempt counter cannot see - **the participating path opens no connection of its
      own**. `CreateConnection` *throws* in those tests, so a pass proves it. A second connection is the
      original TASK-242 defect and an attempt count is blind to it.
- [x] Both halves — every assertion is written twice. `AbstractAsyncConnector` derives from
      `AbstractConnector`, so one probe connector covers both, and mutating either branch alone shows up
      as exactly one of each pair going red.
- [x] Proven able to fail: **4 mutations, each hitting exactly its intended targets** -

      | mutation | red |
      |---|---|
      | participating path routed through the retry loop | **2** - sync + async `ParticipatingPath_MakesExactlyOneAttempt` |
      | `retryWhenOwned` ignored on the owned path | **2** - the `false` case + the async both-ways discrimination |
      | `RetryPolicy.None` -> `RetryPolicy.Default` | **2** - both inertness tests, which is the alarm working |
      | retried attempt reuses one connection | **1** - the `ConnectionsOpened` assertion |

      Reverted by inverting each exact substitution; `Birko.Data.SQL` verified **byte-identical to HEAD**
      afterwards, and the suite is **597/597**. (Note for anyone repeating this: each Birko project is its
      own git repo - `/c/Source/Birko/Framework` is not one, so `git status` must be run from inside
      `Birko.Data.SQL`, not from the directory above it.)
- [x] Consumer Symbio's TASK-472 criterion updated to reference the outcome and the finding below.

## THE FINDING, 2026-08-22 — the flag is INERT as shipped, so criterion 1 is unanswerable rather than unmeasured

This task asked what "preserve" actually means. The answer turned out to be: **preserve a no-op.**

`AbstractConnectorBase.RetryPolicy` defaults to **`RetryPolicy.None`** (`MaxRetries = 0`), and
`ExecuteWithRetry[Async]` short-circuits to a bare call when `MaxRetries <= 0`. Grepped across the whole
framework **and** the Symbio consumer, including every `appsettings*.json`: **nothing assigns a
`RetryPolicy` anywhere**, except `RetryTests.cs`, which sets one explicitly in order to exercise the loop.

So as shipped, `retryWhenOwned: true` and `retryWhenOwned: false` produce **identical** behaviour on every
provider - **SQLite included**. The CR-M144 retry that SQLite is documented to get does not happen.

Consequences for this task's own criteria:

- **Criterion 1 ("SQLite still retries and converges") cannot be satisfied against the shipped
  configuration.** There is no policy to retry under. A test that sets one would be asserting a
  configuration nobody runs - true, and misleading, which is worse than absent.
- **Criterion 2 ("the servers still do not retry") is trivially true** today, for a reason unrelated to the
  flag they pass.
- **Criterion 3 is guaranteed twice over** - the participating branch returns before the flag is read, and
  even the owned branch would not retry under the default policy. It is asserted on the structure, which is
  the half that survives a policy being configured later.

⚠ **This is a decision, not a defect to fix here.** Whether a default `RetryPolicy` *should* be configured
is a resilience-policy question with a blast radius across every provider, and this task's own out-of-scope
says a wrong claim gets corrected by its own task with its own decision. **Filed as [[TASK-305]] on
2026-09-08** — it needs a product call first, not an implementation.

⚠ Two tests (`WithTheSHIPPEDDefaults_*`) exist to **fail** the day a default policy lands. That is the
alarm, not a regression: at that moment SQLite's bulk paths silently start retrying and criterion 1 becomes
a real question. Whoever makes that change should answer it rather than update these tests.

## Closed 2026-09-08 — scope narrowed to what is answerable, remainder owned by [[TASK-305]]

**What this task asked was answered.** The word `preserve` in `retryWhenOwned`'s contract turned out to
name a no-op, and that finding — with the two alarm tests that make it self-reporting — is the deliverable.
Four of six criteria are met and mutation-proven (4 mutations, each hitting exactly its intended targets,
`Birko.Data.SQL` verified byte-identical to HEAD afterwards). Committed as `Birko.Data.SQL.Tests@3a49fef`;
the `pr:` field had been left `null`, which is corrected in this pass.

**The two criteria that stay open are not unfinished work — they are questions this task's own answer made
vacuous.** Both now carry `→ [[TASK-305]]` above, which repeats them verbatim so nothing is lost. Closing
here rather than parking `in-progress` indefinitely, because the remainder is blocked on a **product
decision this task is explicitly not entitled to make**, and an `in-progress` task is one no scheduler
offers and no reader can act on.

### ⚠ The central claim was re-measured before the successor cited it, and it is wider than recorded

TASK-258's original sweep was 2026-08-22 and ~30 tasks old. Re-run 2026-09-08:

| where | `RetryPolicy =` assignments |
|---|---|
| framework production code (all `Birko.*`) | **0** |
| all 16 consumer repos — production **and** test | **0** |
| any `appsettings*.json` in any consumer | **0** |
| framework test code | **3 files** — `RetryTests.cs`, `RetryWhenOwnedTests.cs`, `RewrapClassificationTests.cs` |

The third file is new since the original count — it arrived with TASK-291/294, whose own fix (an exception
rewrap that had been defeating `IsTransientException`) is the *second* defect traceable to nobody ever
exercising this machinery. The conclusion is unchanged and the surface is one file larger. **Re-measure
before citing** (§ TASK-283) applied to this task's own number, and it is the reason TASK-305 opens with a
current count rather than an inherited one.

### Verified at close

`Birko.Data.SQL.Tests`: **686 passed, 0 failed, 0 skipped** (the suite has grown from the 597 recorded when
this work landed; nothing here changed).

## Out of scope

- **Changing any provider's retry policy.** This task asserts what is claimed; if an assertion shows the
  claim is wrong, the correction is its own task with its own decision.
- The transaction boundary itself (TASK-242/243), PostgreSQL's UTC `COPY` binding (TASK-256) and MSSql's
  `TEXT` mapping (TASK-257).

## Human test plan

- [ ] N/A — mechanical; the proof is the attempt count and the resulting row set under an induced transient
      error, per provider and per half.
