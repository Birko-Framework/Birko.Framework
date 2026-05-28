---
id: EPIC-009
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.Communication.gRPC, Birko.Security.OAuth.Server]
---

# Birko.Communication — Remaining protocols

## Area of concern

Two remaining communication subsystems: gRPC support (client + server) and an OAuth2 authorization server (the client-side OAuth flows already exist in `Birko.Communication.OAuth`).

## Success criteria

- `Birko.Communication.gRPC` sibling project with client + server primitives
- `Birko.Security.OAuth.Server` exposes token endpoint, authorization endpoint, client registration, consent management
- OAuth server persists clients + tokens via `Birko.Data.Stores`
