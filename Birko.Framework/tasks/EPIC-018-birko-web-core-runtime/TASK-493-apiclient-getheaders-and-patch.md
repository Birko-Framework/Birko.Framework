---
id: TASK-493
parent: EPIC-018
feature: FEATURE-018
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-25
completed: 2026-09-25
depends-on: []
blocks: []
findings: []
pr: 872a6ed (Birko.Web.Core), 81791fe (Birko.Web.Playground)
github-issue: null
jira-key: null
---

# `ApiClient` had no generic request-header hook and no `patch()`

## Context

`ApiClient` could add exactly two request headers — `Authorization: Bearer` via `getToken` and
`X-Tenant-Id` via `getTenant`. A consumer that needed any other per-request header had to drop
`ApiClient` and hand-roll `fetch`, losing the timeout, the offline outbox and token-refresh handling. It
also had no `patch()`.

The concrete case is **FlowerFurStudio TASK-067**, whose shared `/bff` client must send `X-CSRF-TOKEN` on
every unsafe method (ASP.NET Core antiforgery). That task is `blocked` on this, which in turn blocks the
cart (FlowerFurStudio TASK-006) — and it was blocked with **no framework task to name**: this work existed
only as [Birko-Framework/Birko.Web#1](https://github.com/Birko-Framework/Birko.Web/issues/1) plus a prose
"Blocked on" note, so TASK-067's `depends-on:` was empty and the blocker was invisible to every scheduler.
That is the exact shape EPIC-018 was created for — see its § *Why the absence is not cosmetic* — and the
reason this file now exists.

## Change (Birko.Web.Core)

- `ApiClientOptions.getHeaders?: () => Record<string, string> | Promise<Record<string, string>>`, applied
  in `_send` **after** the request's own `Content-Type` and **before** `Authorization` / `X-Tenant-Id`, so a
  custom header cannot clobber the framework's auth or tenant header. It is applied to the same `Headers`
  bag the post-refresh retry reuses, so the header rides that retry too.
- `patch<T>(path, body?, meta?)`, mirroring `put` — queueable offline like the other writes.
- The write-method unions widened from `'POST' | 'PUT' | 'DELETE'` to include `'PATCH'`:
  `_sendWrite` / `_queue` / `onQueueAction` (`api-client.ts`) and `QueuedAction.method`
  (`offline/action-queue.ts`).
- `SyncManager`'s replay dispatch gained a `PATCH` arm. **This is load-bearing, not tidiness:** the chain's
  final arm is `delete`, so a queued `PATCH` with no arm of its own would replay as a `DELETE` — a partial
  update silently destroying the row.

## Acceptance criteria (the issue's own)

- [x] `getHeaders` headers are sent on GET and on writes, including the retry after a token refresh.
- [x] Async `getHeaders` is supported, so a consumer can fetch its token lazily.
- [x] `patch()` exists and queues offline like `put()`.
- [x] Regression coverage exists for all of the above.

Coverage lives in `Birko.Web.Playground` `src/backport-smoke.ts` (the frontend's regression harness — there
is no in-framework unit runner), where the `ApiClient` fetch mocks already live. 10 checks, all prefixed
`TASK-493`.

## Evidence

- Playground `node verify.mjs`: **backport-smoke 293/293**, full run 9/9 suites, 0 failing checks.
- `tsc --noEmit` (via the consumer's TypeScript) clean.
- **Proven able to fail.** Two mutations, both rebuilt and re-run:
  - *hook declared but ignored, `patch` sending `PUT`* → 6 of the 10 checks red
    (`getHeaders` on GET / first attempt / retry / async; `patch(): issues a PATCH`; `SyncManager` PATCH
    replay).
  - *hook applied last, so it can override auth* → the 2 precedence checks red
    (`cannot override Authorization` / `X-Tenant-Id`).

## Out of scope

- FlowerFurStudio TASK-067's `/bff` client itself. It is unblocked by this and picks the hook up; its
  `depends-on:` should be updated to name this task (a consumer-repo commit).
- Any other API shape on `ApiClient`.

## Progress log

- 2026-09-25 — Implemented in `Birko.Web.Core` (`src/http/api-client.ts`, `src/offline/action-queue.ts`,
  `src/offline/sync-manager.ts`, `README.md`) and covered in `Birko.Web.Playground`
  (`src/backport-smoke.ts`).
- 2026-09-25 — Committed: Web `872a6ed` (fix), Playground `81791fe` (suite). Closing `done` — every
  acceptance criterion is met by automated coverage, with no human-only step outstanding.
- 2026-09-25 — FlowerFurStudio TASK-067 unblocked (`depends-on: [TASK-493]`, `blocked` → `todo`), which
  in turn releases TASK-006 (cart).