---
id: STORY-061
parent: EPIC-019
status: planned
created: 2026-10-08
---

# KoboldLair server backports from DraCode

## User story

As a **Birko consumer serving WebSockets, SSE and background jobs from ASP.NET Core**, I want a safe WebSocket
connection, an auth flag that fails closed, a command router, hosted-service wiring for background jobs, an SSE result
and an owner-or-admin access check **in the framework**, so that I don't hand-roll them the way DraCode's server did —
and Symbio did again, independently, for the first three.

## Where it came from

A 2026-10-08 review of `Consumers/DraCode/DraCode.KoboldLair.Server` outside `Auth/` (~10.4k lines; `Auth/` is
[[STORY-058]]), plus `DraCode.KoboldLair.Client/Program.cs`, `DraCode.AppHost` and `DraCode.ServiceDefaults`. Two of the
tasks are Birko defects found on the way, not backports: [[TASK-536]] and the broadcast half of [[TASK-537]].

Kept in DraCode as policy: the Dragon / Wyrm / Wyvern / Drake processing services, command handlers, run-mode
handlers, domain endpoints, `DragonRequestQueue`, `ProjectNotificationService`, the Aspire boilerplate.

## Done when

Each task's DraCode adoption can delete its local copy, and the two Birko defects are fixed with regression tests.
