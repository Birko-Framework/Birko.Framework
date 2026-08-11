---
id: TASK-200
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-11
depends-on: [TASK-199]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Symbio: an outbox replay duplicates a create, and TASK-151 scoped the cause out of itself

## Context

Consumer-side follow-up to [[TASK-199]]. **The work is in the Symbio repo**
(`C:\Source\Birko\Consumers\Symbio`); this file exists because the finding came out of a framework review
and the framework half has already shipped. Hand-off prompt: `PROMPT-200-symbio-outbox-replay.md`.

Symbio **mints no client-side entity ids** — `randomUUID` appears nowhere in `Symbio.UI`, and its creates
are `service.CreateAsync(request)` with a server-assigned id (e.g. `AccountEndpoints.cs:38-41`). So a
queued `POST` that the server already applied — the response or its body timed out, the client saw
`status: 0`, the write went to the outbox — **creates a second row** when `SyncManager` replays it.

This predates the timeout fix ([[TASK-198]]); any offline POST replayed after a partial success duplicates.
The timeout fix widened the window.

**Two other defects in the same area, both consumer-side:**

- **`conflict-modal.ts` is a dead end.** `_keepMine()` — the primary button — re-issues the identical
  request. On a replay it fails again (409 or 404), hits `toast.error(t('conflicts.retryFailed'))`, and the
  entry stays. `getPending()` never picks a `conflict` entry back up, so **"Keep server" is the only button
  that clears it** — and its toast tells the user their change was discarded when it may have been saved.
- **TASK-151 (`EPIC-029/STORY-063`, created 2026-07-08, still `todo`) diagnosed the symptom and scoped the
  cause out of itself.** It describes the retry-forever loop exactly — *"a permanently-failing action
  retries every cycle forever, burning requests and hiding the failure from the user"* — but lists both
  *"409 conflict resolution (already handled by `conflict-modal.ts`)"* and *"server-side idempotency
  changes"* under **Out of scope**, and frames the loop as a rate problem to be capped. A dead-letter cap
  built on that premise would have presented a delete that **succeeded** to the user as a failure to
  inspect, retry or discard.

## Approach

Not settled — the shape is a consumer decision. Two candidates, and the choice matters:

1. **Mint ids client-side** (what Reps does) and set `idPinned` on those writes, so the framework drains a
   replayed create. Requires the create endpoints to accept a caller-supplied id, i.e. a server change per
   resource, and a decision on whether an id clash is 409 (strict REST, as Reps chose) or an upsert.
2. **Keep server-minted ids** and accept that a queued create is at-least-once, surfacing it honestly —
   which does not fix the duplicate, only makes it visible.

Option 1 is the one that actually closes it, and it is the larger change. Note that `idPinned` must **not**
be set on any endpoint whose conflict status can also mean a business rejection — see [[TASK-199]].

TASK-151 stays valid after [[TASK-199]], narrowed to genuinely permanent failures. It should be re-scoped
rather than closed: remove the two out-of-scope lines that are no longer true, and note that `applied` is
now a distinct outcome so a cap can no longer slander a successful write.

## Acceptance criteria

- [ ] A queued `POST` replayed after the server already applied it does **not** create a second row
- [ ] `conflict-modal.ts` can always clear its entry, and no button's message asserts an outcome the code
      did not verify
- [ ] TASK-151 re-scoped: the two stale Out-of-scope lines removed, `applied` acknowledged
- [ ] Whichever option is chosen, `idPinned` is set only where the endpoint's conflict has one meaning
- [ ] Regression coverage in Symbio's own suite, red-verified

## Out of scope

- The framework classification itself — shipped in [[TASK-199]].
- Backoff / cap / dead-letter — that is TASK-151's own remaining scope.

## Human test plan

- [ ] Create a record offline → go online → confirm exactly one row exists.
- [ ] Force a body-read timeout on a create the server commits (throttle to stall the response body) →
      confirm one row, and the outbox drains to zero.
- [ ] Reach the conflict modal and confirm every button leaves the queue in a consistent state.
