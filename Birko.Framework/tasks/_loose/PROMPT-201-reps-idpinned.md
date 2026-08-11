# Hand-off prompt — Reps: declare `idPinned` on the client-minted creates

Paste the block below into an agent session **rooted in `C:\Source\Birko\Consumers\WorkoutTracker`**.
Tracking: this repo's [[TASK-201]]. Framework half already shipped as [[TASK-199]].

---

A framework review of `Birko.Web.Core`'s `SyncManager` found that it had no name for a write that had
**already landed**, so it read two such replays as something else. The fix shipped upstream
(`Birko.Web.Core` `1905988` + `37f1ece`, 2026-08-11). Half of it is live for Reps automatically; the other
half needs a per-call-site opt-in that only you can decide. **Verify each claim below against the code before
acting on it.**

## Already live for you, no change needed

A `DELETE` whose replay returns **404** now drains from the outbox instead of being marked `failed`. Because
`getPending()` includes `failed`, such an entry was previously re-sent on **every sync forever** — for a row
that by definition can never answer anything else. Your four queued deletes benefit directly:
`sets/{id}`, `sessions/{id}`, `sessions/{id}/exercises/{id}`, `schedule-periods/{guid}`.

**One consequence to sign off rather than inherit:** `OwnedCrudResults.RequireOwned` answers **404 for a
foreign row** rather than disclosing that it exists. So a queued delete aimed at someone else's entity now
*drains* rather than retrying. That is deliberate and documented on the `deleteMissingIsApplied` option — a
write the caller may not make will not start succeeding — but decide it is acceptable for Reps. If not, set
`deleteMissingIsApplied: false` and record why.

## Needs your opt-in — `idPinned`

Reps already does the hard part. It mints entity guids client-side (`Reps.Web/src/api.ts:1412`, `1912` —
your own comment: *"client-minted so an offline replay lands under the id already shown"*) and the server
detects a clash: `MapOwnedCrud` + `RequestGuid` → `OwnedCrudResults.CreateClash`, plus hand-rolled
equivalents at `Reps.Api/Api/PlanEndpoints.cs:64` and `PhaseEndpoints.cs:48`.

**So Reps cannot duplicate a create on replay.** What it got instead was a *false conflict*: the 409 from a
replayed create was reported to the user as a conflict against a write that had **succeeded**, and the outbox
entry wedged permanently — a `conflict` entry is neither returned by `getPending()` nor removed by anything.

`SyncManager` now drains that case, but **only when the queued action declares `metadata.idPinned`**. Until
you set it, the framework fix is inert for your creates.

## The trap — read this before sweeping

`idPinned` asserts something stronger than *"an id is in the body"*. It asserts that **this endpoint's
conflict status has exactly one meaning**. Your own suite documents a counter-example:
`Reps.Api.Tests/PlanHierarchyCrudTests.cs` records **"slot uniqueness → 409"** on a POST that *also* carries
a client guid. Pinning that one would drain a genuinely rejected write and report it as saved.

So this is per-endpoint judgement, not a find-and-replace over every `api.post` with a guid in the body.

Starting inventory — **verify each row**:

| Call site | Server | Likely |
|---|---|---|
| `api.post('body-measurements', { guid, … })` — `api.ts:1913` | `MapOwnedCrud` + `RequestGuid` | **pin** — confirm 409 has no other source on this route |
| `api.post(path, { guid, … })` plan hierarchy — `api.ts:1413` | hand-rolled clash **plus** slot uniqueness | **do not pin** for slots; decide per resource |
| `api.post('steps', { date, steps })` — `api.ts:1946` | `steps.Upsert(...)` by date → 200 | **not needed** — never 409s |

## Explicitly out of scope

Do **not** change the server's strict-REST 409 into an upsert. It is a deliberate decision pinned by
`Reps.Api.Tests/ExerciseCrudTests.cs:90`:

```csharp
dup.StatusCode.Should().Be(HttpStatusCode.Conflict); // create-only: never a silent update
```

Making `MapOwnedCrud` return 200 on an owned pinned-id clash is the obvious idempotency answer and was
rejected upstream for exactly this reason.

## Acceptance criteria

- [ ] Every queued `POST` classified: `idPinned` set, or explicitly reasoned as not eligible
- [ ] Any endpoint whose 409 is overloaded is **not** pinned, with a comment saying why — so it is not
      "fixed" later by someone reasoning from symmetry
- [ ] A replayed pinned create drains with **no** conflict raised to the user
- [ ] A replayed queued delete of an already-gone row drains
- [ ] The foreign-row 404 drain is confirmed acceptable, or `deleteMissingIsApplied: false` is set with a
      reason
- [ ] Regression coverage in Reps' own suite, **red-verified**

## Notes on getting this right

- **Assert that the entry left the queue**, not that the classifier decided something. "Did it drain" is the
  whole defect. A test that inspects a decision without checking the queue can pass with the fix reverted.
- **Assert the guards too.** The risk in this change is draining too *much*: include a check that an
  unpinned POST's 409 is still a conflict, so a later over-broad sweep breaks a test instead of silently
  swallowing rejected writes.
