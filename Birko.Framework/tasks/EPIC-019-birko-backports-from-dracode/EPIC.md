---
id: EPIC-019
status: in-progress
created: 2026-10-07
owner: ai
affects: [Birko.Security.OAuth.Server, Birko.Security.AspNetCore, Birko.Communication.OAuth, Birko.Communication.OAuth.Providers, Birko.Data.SQL.SqLite, Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell, Birko.AI, Birko.AI.Contracts, Birko.AI.Orchestration, Birko.AI.Resilience, Birko.Data.EventSourcing, Birko.EventBus, Birko.Security, Birko.Helpers, Birko.Communication.WebSocket, Birko.Communication.SSE, Birko.Communication.AspNetCore, Birko.BackgroundJobs]
---

# Birko framework backports from DraCode

## Area of concern

Building the **DraCode / KoboldLair** consumer (`Consumers/DraCode`) left generic capability written app-side because
the framework did not provide it yet. A review on 2026-10-07 sorted DraCode's own auth code (and, separately, its web
client) into "DraCode policy — stays" and "generic — belongs in Birko". This epic is the framework-side home for the
generic part, as [[EPIC-016]] is for Reps.

A second review on 2026-10-08 covered the KoboldLair engine itself — the core library (`DraCode.KoboldLair`) and the rest
of the server — and produced STORY-060 and STORY-061. Two of its tasks are Birko defects found on the way, not
backports ([[TASK-536]], and the broadcast half of [[TASK-537]]).

The consumer side — deleting DraCode's local copy once the framework version ships — stays in DraCode as its
`EPIC-018` (adopt Birko framework upstreams); each task here names the DraCode task that adopts it. STORY-060/061 are
adopted by DraCode `EPIC-018 / STORY-040` (TASK-125 – TASK-140).

## Stories

- [[STORY-058]] Security / OAuth backports from DraCode
- [[STORY-059]] Web backports from DraCode
- [[STORY-060]] KoboldLair agent-runtime and data backports from DraCode
- [[STORY-061]] KoboldLair server backports from DraCode
