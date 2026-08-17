---
id: TASK-199
parent: EPIC-018
feature: FEATURE-018
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-11
completed: 2026-08-11
depends-on: []
blocks: [TASK-200, TASK-201]
findings: []
pr: 1905988 + 37f1ece (Birko.Web.Core), 01cb62a + bec9e04 (Birko.Web.Playground)
github-issue: null
jira-key: null
---

# `SyncManager` had no name for a write that had already landed

## Context

`../../Web/Birko.Web.Core/src/offline/sync-manager.ts`. Found reviewing [[TASK-198]]'s
request-timeout fix rather than reported from the field.

That fix folds a timeout into the `status: 0` network-error path so a write diverts to the outbox.
Correct — and it widens the window in which `SyncManager` replays a write the server may **already have
applied**. Two such cases were read as something else:

| Replay | Old reading | Consequence |
|---|---|---|
| `DELETE` → 404 | `failed` | `getPending()` includes `failed`, so it was re-sent on **every sync forever** — for a row that by definition can never answer anything else |
| `POST` → 409 for a client-minted id | `conflict` | The user was told their **successful** write conflicted, and the entry **wedged permanently**: a `conflict` entry is neither returned by `getPending()` nor removed by anything in the framework |

Neither is a duplicate row. The original review finding claimed a duplicate-write exposure; tracing the
consumers disproved it for Reps and relocated it to Symbio ([[TASK-200]]).

## Approach

`applied` is now a named outcome alongside `conflict` and `failed`, and `_classifyReplay` is the one place
the reasoning lives.

**`idPinned` is declared per write rather than inferred, and that is the whole design** — the two consumers
fail in *opposite* directions:

- **Reps** mints guids client-side (`api.ts:1412`, `1912`, commented *"so an offline replay lands under the
  id already shown"*) and its server answers 409 on a pinned-id clash (`MapOwnedCrud` + `RequestGuid` →
  `CreateClash`). It never duplicates; it got the false conflict.
- **Symbio** mints no ids at all — `randomUUID` appears nowhere in `Symbio.UI`, and `CreateAsync` assigns
  the id server-side. Its 409s are business rules **only**. Draining those would discard a rejected write
  and report success.

So "POST + 409 means already-applied" is true for one consumer and actively harmful for the other. Reps'
own suite proves inference cannot work even *within* one consumer: `PlanHierarchyCrudTests` records
**"slot uniqueness → 409"** on a POST that also carries a client guid. `idPinned` therefore asserts
something stronger than *an id is in the body* — that **this endpoint's conflict has exactly one meaning**.

`deleteMissingIsApplied` defaults to `true` with an escape hatch, because a correct default that can be
turned off protects everyone while a knob nobody enables protects nobody. `PUT` is excluded even when
pinned: it is addressed by id already, so its 409 is about the entity's *state*.

## Acceptance criteria

- [x] A `DELETE` whose row is already gone drains instead of being re-sent forever
- [x] An id-pinned `POST` whose id already exists drains, and raises **no** conflict
- [x] An **unpinned** `POST` 409 is still a conflict
- [x] A pinned `PUT` 409 is still a conflict
- [x] A `DELETE` 500 is still a retryable failure
- [x] `deleteMissingIsApplied: false` restores the previous behaviour
- [x] Two review follow-ups shipped alongside (below)
- [x] Revert split recorded by name

## Outcome

Landed 2026-08-11. Playground `backport-smoke` **283** (was 270); `verify.mjs` 0 failing;
`device-fix-check` 68/68; `tsc --noEmit` clean.

**Revert split:**

| Reverted | Failing |
|---|---|
| `_classifyReplay`'s two new arms | **4 of 8** in the new block |
| — | the **4 guards** stay green: unpinned POST 409, pinned PUT 409, DELETE 500, opt-out |
| the post-refresh retry log | **exactly 1** — the envelope assertion beside it stays green, because only the log was missing |

**Two follow-ups from the same review, shipped here:**

- **A timed-out post-refresh retry was the one completely silent failure in `ApiClient`.** That catch
  returned the status-0 envelope with no log at all, while the other two arms distinguish "Timed out" from
  "Network error" — and it shares **one** timeout budget with the first fetch *and* the refresh call, so it
  is the arm most likely to be the one that aborts.
- **The `'?'`-vs-`'&'` separator had three implementations and two of them were right.** `SseClient` and
  `WsClient` had been choosing it correctly for as long as `ApiClient.get` had been getting it wrong, three
  lines apart in the same folder. Extracted as `appendQuery` in `http-utils`, exported from the barrel in
  the same commit — deliberately, because `DEFAULT_REQUEST_TIMEOUT_MS` shipped unreachable through that
  explicit named list three days earlier.

Four things worth carrying past this class:

- **A review finding can be right about the risk and wrong about the repo.** The filed finding said a
  timed-out write could be replayed into a duplicate row. Tracing every queued write against its server
  handler showed Reps cannot duplicate — client-minted guids plus a server-side clash check — and that the
  exposure it described is live in **Symbio**, which mints no ids. Both halves of the original claim were
  wrong for the consumer it was written against.
- **The server-side fix was closed by a test, not by an argument.** Making `MapOwnedCrud` return 200 on an
  owned pinned-id clash is the obvious idempotency answer and would have overturned a deliberate decision:
  `ExerciseCrudTests:90` pins the 409 with `// create-only: never a silent update`. Grep the suite before
  proposing a semantic change to a shared endpoint.
- **No `Idempotency-Key` was needed, and proposing one would have been the expensive wrong answer.** The
  client already sends the key (in the body) and the server already checks it. The defect was entirely in
  how the *client* read the answer. A new wire contract would have been built alongside a working one.
- **The guards are the deliverable, not the fix-dependent checks.** The risk in this change is draining too
  much, so the four checks that must stay green carry more information than the four that must fail. Both
  sets were verified against a revert.

## Out of scope

- **Backoff / max-retry cap / dead-letter** for a genuinely permanent failure — Symbio's TASK-151 asks for
  exactly this as an upstream contribution and it remains valid *after* this fix, narrowed to real
  failures. See [[TASK-200]].
- Symbio's duplicate-POST exposure — [[TASK-200]].
- Reps adopting `idPinned` — [[TASK-201]].

## Human test plan

The checks stub `fetch`, so they prove the classification, not the platform behaviour that triggers it. To
re-confirm on hardware: queue a delete offline, let it sync, then replay the same outbox entry (or delete
the row server-side first) and confirm the pending badge reaches zero instead of retrying.
