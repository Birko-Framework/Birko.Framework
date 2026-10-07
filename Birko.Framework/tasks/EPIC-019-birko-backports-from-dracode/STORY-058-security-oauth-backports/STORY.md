---
id: STORY-058
parent: EPIC-019
status: planned
created: 2026-10-07
---

# Security / OAuth backports from DraCode

## User story

As a **Birko consumer running `Birko.Security.OAuth.Server` behind ASP.NET Core**, I want persistent stores, endpoint
mapping, federated sign-in token issuance and the client-credentials / scope gaps covered **by the framework**, so that I
don't rewrite the ~1,000 lines DraCode had to write around it.

## Where it came from

DraCode's `DraCode.KoboldLair.Server/Auth/` (about 1,500 lines). Kept in DraCode as policy: the role → permission map,
the GitHub allowlist + user provisioning, config classes. Everything filed below is generic.

## Done when

Each task's DraCode adoption task can delete its local copy.
