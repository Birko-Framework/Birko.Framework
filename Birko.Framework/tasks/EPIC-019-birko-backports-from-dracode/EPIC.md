---
id: EPIC-019
status: planned
created: 2026-10-07
owner: ai
affects: [Birko.Security.OAuth.Server, Birko.Security.AspNetCore, Birko.Communication.OAuth, Birko.Communication.OAuth.Providers, Birko.Data.SQL.SqLite, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell]
---

# Birko framework backports from DraCode

## Area of concern

Building the **DraCode / KoboldLair** consumer (`Consumers/DraCode`) left generic capability written app-side because
the framework did not provide it yet. A review on 2026-10-07 sorted DraCode's own auth code (and, separately, its web
client) into "DraCode policy — stays" and "generic — belongs in Birko". This epic is the framework-side home for the
generic part, as [[EPIC-016]] is for Reps.

The consumer side — deleting DraCode's local copy once the framework version ships — stays in DraCode as its
`EPIC-018` (adopt Birko framework upstreams); each task here names the DraCode task that adopts it.

## Stories

- [[STORY-058]] Security / OAuth backports from DraCode
- [[STORY-059]] Web backports from DraCode
