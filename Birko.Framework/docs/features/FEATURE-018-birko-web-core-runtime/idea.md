---
id: FEATURE-018
created: 2026-08-17
owner: ai
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: review
---

# Birko.Web.Core — the browser-side runtime

> Stakeholder-readable. Created 2026-08-17 alongside [EPIC-018](../../../tasks/EPIC-018-birko-web-core-runtime/EPIC.md)
> because four already-fixed defects had no feature to belong to — not because new scope was
> proposed. The decision ledger records what that means and does not claim more.

## Problem

`Birko.Web.Core` is the part of the framework that runs **in the browser**: it talks to the
server, keeps working when the network does not, and reconciles the two afterwards. Everything
users notice about an app feeling reliable offline lives here — a request that hangs forever, a
write replayed twice, an empty list that cannot say whether it means "nothing yet" or "we never
managed to ask".

It had no tracking home. Four defects of exactly that kind were fixed and then sat in the
loose pile, invisible to every stakeholder view. Two of them were only *discovered* by
comparing the code repository against the tracking repository — the fixes had shipped with no
record at all.

## Proposed shape

- A Web.Core defect has somewhere to be filed, so its fix gets the tracking commit the
  polyrepo model expects instead of shipping silently.
- The runtime's promises to a user are stated where a non-developer can read them: a request
  eventually gives up rather than hanging, a queued write is not applied twice, and "we have
  no data" is distinguishable from "we never synced".
- Nothing about the `b-*` component catalogue moves here. This is the layer underneath it.

## Open questions distilled from the grill

_None recorded._ No [[grill-me]] interview preceded this — it was created to close a tracking
gap found by a [[roadmap]] audit, so there are no `proposed` rows awaiting a verdict.
Questions raised from here on belong in [decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- `Birko.Web.Components` (FEATURE-001) and the catalogue-adoption gaps (FEATURE-016).
- `Birko.Web.Shell`, unless it starts producing homeless work of its own.
- The **Xaml** mirror of these capabilities (FEATURE-015).
- Theme colour-contrast gating (TASK-130), which spans Web.Components and Xaml.

## Prototype

**N/A.** This feature exists to track a runtime that already ships; there is nothing to
prototype. Any *future* scope added here takes the prototype decision explicitly, as a new
decision row.
