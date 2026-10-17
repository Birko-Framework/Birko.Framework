---
id: EPIC-020
# status — one of: planned, in-progress, done, cancelled
status: done
created: 2026-10-13
owner: ai
affects: [Birko.Security.Grants, Birko.Security.Grants.AspNetCore, Birko.Security.AspNetCore, Birko.Models.Users]
---

# Security — access control

> Reopened 2026-10-17 — TASK-654 is now todo (filed by /tasks spawn from TASK-641)

## Area of concern

How a consumer decides **what an authenticated caller may do**, as distinct from *who* they are. The
framework already authenticates (`Birko.Security.Jwt`, `Birko.Security.AspNetCore`, external logins via
`Birko.Models.Users.UserLogin`), and it authorizes **globally** (roles, permission strings,
`RequirePermission`, the `*` wildcard). It has no answer for **per-resource** authority ("owns this
presentation") or for granting access to someone who has not signed in yet.

Prompted 2026-10-13 by consumers (Reps, the presentation project) that need public reads and gated writes
behind an external login. The decision taken then was **not** to run `Birko.Security.OAuth.Server` as a
central login: it has no OIDC, and the risky part of auth is the login itself, which is the host's job.

Out of scope at the epic level: authentication / identity providers, OIDC in the OAuth server, and
admin UIs (consumer work).

## Success criteria

- A consumer can wire external login + global admins + per-resource owners/editors + invitations
  from framework parts, following one README, without writing its own grant model.
- Revoking access takes effect on the next request.
- Tenant-bound grants never match in another tenant.
