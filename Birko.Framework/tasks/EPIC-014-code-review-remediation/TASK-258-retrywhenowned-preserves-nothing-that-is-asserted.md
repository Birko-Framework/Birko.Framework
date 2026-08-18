---
id: TASK-258
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-18
depends-on: []
blocks: []
related: [TASK-242, TASK-243, TASK-256, TASK-257]
findings: []
pr: null
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

- [ ] **SQLite still retries and converges.** Force a transient `SQLITE_BUSY`/`SQLITE_LOCKED` on the
      own-connection bulk path and assert the operation retried *and* ended with the correct row set — not
      merely that it did not throw. The retry only being safe *because the batch rolled back first* is the
      part to pin: assert no duplicate or partially-applied rows after a retried attempt.
- [ ] **The three server providers still do not retry.** Assert their own-connection bulk path makes exactly
      one attempt, so no consumer silently loses a retry it used to have — and none silently gains one.
- [ ] **The participating path never retries, on any provider, whatever the flag says.** Pass
      `retryWhenOwned: true` while inside a boundary and assert a single attempt. This is the assertion worth
      having most: the flag's existence invites a future caller to thread it through, and that is the one
      combination that could double-apply statements inside somebody else's transaction.
- [ ] Both halves — sync `RunBulk` and async `RunBulkAsync` are separate code paths with separate branches.
- [ ] Proven able to fail: flip each default and watch the matching assertion go red. Revert by inverting the
      exact substitution — never `git checkout --` over uncommitted work, never `mv` a backup over the file
      (mtime makes MSBuild skip the rebuild and the run then tests a stale binary).
- [ ] Consumer Symbio's TASK-472 criterion updated to reference the outcome, since that task stays open on
      this item alone.

## Out of scope

- **Changing any provider's retry policy.** This task asserts what is claimed; if an assertion shows the
  claim is wrong, the correction is its own task with its own decision.
- The transaction boundary itself (TASK-242/243), PostgreSQL's UTC `COPY` binding (TASK-256) and MSSql's
  `TEXT` mapping (TASK-257).

## Human test plan

- [ ] N/A — mechanical; the proof is the attempt count and the resulting row set under an induced transient
      error, per provider and per half.
