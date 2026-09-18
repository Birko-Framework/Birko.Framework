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

## ⚠ Measured correction, 2026-08-11 — the mechanism is right, the ROUTE is not reachable in Symbio

The guide asked for each finding to be confirmed in code before acting. Doing that confirmed all three —
and then **falsified the consequence**. Reproduced against the running Testing stack, not reasoned about.

**All three findings hold.** No `randomUUID`/`uuidv4`/`getRandomValues` anywhere in `Symbio.UI`;
`AccountEndpoints.cs:38` returns a server-assigned id. `_keepMine()` re-issues the identical request and,
on failure, hits `toast.error(retryFailed)` **without** calling `actionQueue.remove` — only `_keepServer()`
clears the entry. TASK-151 is still `todo` and lines 47–48 are verbatim as quoted.

**But the duplicate does not reproduce, because no write ever reaches the outbox.**
`_sendWrite` diverts only `if (meta && !res.ok && res.status === 0)`. A depth-aware scan of every write
call site in `Symbio.UI/src` — counting arguments by nesting, not by regex, since the bodies contain
commas, template literals and nested objects:

| | |
|---|---|
| `api.post` / `api.delete` / `api.put` call sites | 94 / 61 / 29 = **184** |
| …passing an `ActionMeta` argument | **0** |
| `actionQueue.enqueue` call sites | **1** (`shared/api.ts:39`, reachable only from `onQueueAction`) |

`api` is a bare `new ApiClient({...})` — no wrapper can inject `meta` behind the call sites, so the third
argument is the only source and it is never supplied.

**Reproduction** (stall the create's response against a server that commits it, exactly as step 1 asks):

```
POST intercepted, forwarded to server → server COMMITTED (visitors 8 → 9)
[ApiClient] Timed out: POST api/facilities/visitors → client saw {ok:false, status:0}
outbox 'actions' store                → 0 entries
rows created                          → 1, NOT 2
```

So Symbio's whole offline-write path — `ActionQueue`, `SyncManager`, `conflict-modal.ts` — is wired and
**never fed**. That also explains why the modal's dead end has never been reported in Symbio: it is
unreachable.

### What IS reachable, and it is live today

The create is committed and the user is told it failed. `visits-page.ts:320` is
`if (r.ok) { toast.success(...); close(); reload(); } else showFormError(form, 'fullName', r.data)` — and
`r.data` is `null` on a timeout. Measured after the 20s timeout, with a before/after control:

| | |
|---|---|
| modal still open | **true** (control: open before; the success path calls `close()`) |
| save button | **re-armed** — no `disabled`, no `loading` |
| field value | **still holds the user's input** |
| success toast | **none** |
| rows on server | **1, already created** |

The form sits open with their text and an armed Save. One more click is the second row. Same end state as
the guide describes, reached entirely through the user rather than the outbox. Filed as **Symbio TASK-390**.

### Consequences for the Approach below

- **Both options address a route that does not exist yet.** `idPinned` was already unsafe here — Symbio
  maps to 409 from `AlreadyExists`/`AlreadyAssigned`/`AlreadyLinked`/`AlreadyMember`/`AlreadyInitialized`
  and any `Duplicate*` prefix (`ResultExtensions.cs:132-141`), all business rules, and
  `sync-manager.ts:177` classifies on **HTTP status only**, never the body code. It is now also moot.
- **If Option 1 is taken, prefer the upsert branch over the 409 branch.** With an idempotent create the
  replay returns 2xx and drains on `_classifyReplay`'s `if (ok) return 'applied'` — `idPinned` is never
  needed and the business-rule ambiguity never arises. The 409 branch would first require relocating every
  `Duplicate*`/`Already*` off 409 across every module: a breaking contract change before it is safe anywhere.
- ⚠ **Ordering is inverted from the guide's.** Today's safety is accidental. The moment anyone passes
  `ActionMeta` on writes to make offline queueing work, this duplicate becomes automatic rather than
  user-driven. **Id-idempotency must land before writes are allowed to queue**, not after.

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
