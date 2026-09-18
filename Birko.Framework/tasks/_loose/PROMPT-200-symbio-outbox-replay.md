# Hand-off prompt — Symbio: an outbox replay duplicates a create

Paste the block below into an agent session **rooted in `C:\Source\Birko\Consumers\Symbio`**.
Tracking: this repo's [[TASK-200]]. Framework half already shipped as [[TASK-199]].

---

A framework review of `Birko.Web.Core`'s `SyncManager` found three defects that reach Symbio. The framework
half has already shipped (`Birko.Web.Core` commits `1905988` + `37f1ece`, 2026-08-11); what follows is
Symbio's half. **Verify each claim against the code before acting on it** — one claim in the original review
was wrong about which repo it applied to, and this write-up is the corrected version, not the original.

## What shipped upstream, and what it means for you

`SyncManager` now names a third replay outcome, `applied`, beside `conflict` and `failed`:

- A `DELETE` whose replay returns **404** drains from the outbox instead of being marked `failed`. Because
  `getPending()` includes `failed`, such an entry used to be re-sent on **every sync forever**. This needs no
  opt-in and is already live for you. Escape hatch: `deleteMissingIsApplied: false`.
- A `POST` whose replay returns the conflict status drains **only if the queued action declared
  `metadata.idPinned`**. That flag asserts the write named its own target id, so a clash means *this very
  create* already landed.

`idPinned` is opt-in per write, not inferred, precisely because of the difference between you and Reps —
see below.

## Defect 1 — a replayed create makes a second row (the important one)

Symbio **mints no client-side entity ids**: `randomUUID` appears nowhere in `src/Frontend/Symbio.UI`, and
creates are `service.CreateAsync(request)` with a server-assigned id (see
`src/Modules/Business/Symbio.Module.Accounting/Endpoints/AccountEndpoints.cs:38-41`, which is
representative). So a queued `POST` that the server **already applied** — the response or its body timed
out, the client saw `status: 0`, `_sendWrite` diverted it to the outbox — creates a **duplicate row** when
`SyncManager` replays it.

This predates the 20s request timeout (`Birko.Web.Core` `79fe552`); that change only widened the window.

`idPinned` cannot help you as things stand: with no client-minted id there is nothing to clash on, and your
409s mean something else entirely (see defect 3). Two candidate shapes, and **the choice is yours to make
and to record**:

1. **Mint ids client-side and pin them** — what Reps does. Closes the hole properly. Requires each create
   endpoint to accept a caller-supplied id, plus a decision per resource on whether an id clash is a 409
   (strict REST) or an upsert. This is the larger change.
2. **Keep server-minted ids** and treat a queued create as at-least-once, surfacing that honestly. Does not
   fix the duplicate; only stops it being invisible.

Whichever you pick, **do not set `idPinned` on any endpoint whose conflict status can also mean a business
rejection** — that rejection would be drained as a success and the write silently discarded.

## Defect 2 — `conflict-modal.ts` is a dead end

`src/Frontend/Symbio.UI/src/shell/conflict-modal.ts`. `_keepMine()` — the **primary** button — re-issues the
identical request. On a replay that fails again (409 or 404), so it lands on
`toast.error(t('conflicts.retryFailed'))` and the queue entry stays. Nothing in the framework returns a
`conflict` entry to `getPending()` or removes it, so:

- **"Keep server" is the only button that clears the entry** — and its toast tells the user their change was
  discarded, when after a successful-but-misread replay it was actually saved.
- Otherwise the pending badge never returns to zero.

The upstream fix removes the *false* conflicts, so this modal will be reached less often. It is still a dead
end for a genuine conflict, and its messages still assert outcomes the code did not verify.

## Defect 3 — TASK-151 scoped its own cause out of itself

`tasks/EPIC-029-pwa-offline-sync/STORY-063-sync-reliability/TASK-151-sync-backoff-deadletter.md`, created
2026-07-08, still `todo`. It describes the retry-forever loop **exactly**:

> A permanently-failing action … retries every cycle forever, burning requests and hiding the failure from
> the user.

…but its **Out of scope** section lists both *"409 conflict resolution (already handled by
`conflict-modal.ts`)"* and *"Server-side idempotency changes"* — the two places the cause actually lived —
and it frames the loop as a **rate** problem to be capped rather than a **misclassification**. A dead-letter
cap built on that premise would have presented a delete that **succeeded** to the user as a failure to
inspect, retry or discard.

TASK-151's remaining ask (exponential backoff, max-retry cap, dead-letter UI, persisted retry state) is
**still valid** and still wanted upstream — now correctly narrowed to genuinely permanent failures.
Re-scope it: drop the two stale Out-of-scope lines, and note that `applied` is now a distinct outcome so a
cap can no longer slander a successful write.

## Acceptance criteria

- [ ] A queued `POST` replayed after the server already applied it does **not** create a second row
- [ ] `conflict-modal.ts` can always clear its entry, and no button's message asserts an outcome the code
      did not verify
- [ ] TASK-151 re-scoped as above (not closed)
- [ ] `idPinned` set only where the endpoint's conflict status has exactly one meaning, with a comment
      saying why wherever it is deliberately **not** set
- [ ] Regression coverage in Symbio's own suite, **red-verified** — revert the fix and confirm the new
      checks fail, and that any back-compat checks stay green

## Notes on getting this right

- **Assert the shape you want positively.** A check that asserts the *absence* of the broken behaviour tends
  to pass for the wrong reason. For the duplicate: assert the row count is exactly 1 after a replay, not
  that "no error was shown".
- **One representative is not a suite.** Pick a create whose endpoint actually exercises the path; a
  resource whose server happens to upsert will pass with the fix reverted.
- **Check the premise before building on it.** TASK-151's "already handled by `conflict-modal.ts`" is the
  kind of statement that reads as settled and was not. Re-read the file.
