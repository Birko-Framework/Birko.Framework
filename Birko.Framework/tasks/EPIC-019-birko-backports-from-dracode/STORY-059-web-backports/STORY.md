---
id: STORY-059
parent: EPIC-019
status: planned
created: 2026-10-07
---

# Web backports from DraCode

## User story

As a **Birko.Web consumer**, I want request/response over the WebSocket client, OAuth sign-in helpers, reorderable tabs
and persisted split sizes, and a runtime-config loader **in the framework**, so that the next app doesn't hand-roll them
the way DraCode's client (`Consumers/DraCode/DraCode.KoboldLair.Client`) did or was about to.

## Where it came from

A 2026-10-07 review of the DraCode web client against Birko.Web. Most of that client already sits on Birko.Web
(`BAppShell`, `createAuthStore`, `createModuleStore`, `WsClient`, `SseClient`, Birko.Web.Testing); the gaps below are the
generic pieces it had to write or plan app-side. Consumer side: DraCode `EPIC-018 / STORY-039`.
