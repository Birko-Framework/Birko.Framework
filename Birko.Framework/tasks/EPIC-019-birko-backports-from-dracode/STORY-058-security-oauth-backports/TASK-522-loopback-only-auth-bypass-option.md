---
id: TASK-522
parent: STORY-058
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Optional "no token needed when listening on loopback only" for Birko.Security.AspNetCore

## Context

DraCode's daemon mode lets local tools call the server without a token when every bound address is loopback
(`DraCode.KoboldLair.Server/Auth/DaemonLoopback.cs`, 65 lines): off by default, fails safe to enforcing when the bindings are
unknown, and gives the request a principal with a wildcard permission.

Adopted in the consumer by DraCode TASK-111 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] An option on `AddBirkoSecurity` (off by default) with the same fail-safe rules
- [x] Tests: off never bypasses; all-loopback bypasses; any non-loopback or unknown binding enforces

## Out of scope

- Any other bypass rule

## Human test plan

N/A — library behaviour, covered by unit tests.

## Outcome (2026-10-18)

- **What it adds.** `BirkoSecurityOptions.LoopbackBypass` (default false), `LoopbackBypass.ShouldBypass`, a
  `LoopbackBypassMiddleware`, `UseBirkoLoopbackBypass()`, and a startup check that refuses to start the host when the
  option is on without the middleware (the tenant-header-guard pattern, otherwise the flag reads as on and does nothing).
- **Rules, kept from DraCode.** Off never bypasses. The bypass applies only while every address is loopback; unknown,
  wildcard (`0.0.0.0`, `[::]`, `*`, `+`) or other addresses enforce. A request with a valid token keeps its own
  principal. A bypassed one runs with `*` in the configured `PermissionClaim` and `Guid.Empty` as user id.
- **Defect fixed, measured on a real host.** DraCode decided from `app.Urls` / the `urls` setting at startup. With
  `--urls http://127.0.0.1:0` plus `ListenAnyIP`, it reported bypass=True while Kestrel was bound to
  `http://[::]:64192`, which is unauthenticated access from any interface. The framework version reads the server's
  actual `IServerAddressesFeature` per request.
- **Known limit, documented.** A reverse proxy on the same machine forwards remote callers over loopback; never enable
  the bypass behind one.
- **Tests.** `LoopbackBypassTests`, 19 on real Kestrel. The API is new, so instead of a red check on HEAD: deciding from
  the configured `urls` fails the bound-addresses test, and removing the startup check fails its test (2 of 19).
- **Regression.** Security.AspNetCore 136, Grants 184. Sandbox builds.
- **Spec and docs.** Spec `security-and-authorization`: a new requirement with 3 scenarios. Docs: `security.md` §
  Loopback-only bypass, and the project CLAUDE.md.
