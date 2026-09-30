---
id: TASK-508
parent: EPIC-018
feature: FEATURE-018
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-30
completed: 2026-09-30
depends-on: []
blocks: []
findings: []
pr: 6afc4e6 (Birko.Web.Core), a5731d4 (Birko.Web.Playground)
github-issue: null
jira-key: null
---

# `ApiClient` cannot send a multipart upload

## Context

**Symbio TASK-839** (`tasks/EPIC-024-*/STORY-079-*/TASK-839-images-tab-upload-control.md`) adds a photo
upload to the staff Images tab. The server endpoint already exists:
`POST /api/products/{productId}/images/upload` takes multipart form data (`file`, `altText`, `sortOrder`,
`isPrimary`, `variantGuid`).

`ApiClient` cannot send it. `post` / `put` / `patch` always JSON-encode the body and set
`Content-Type: application/json`. A consumer also cannot work around it: `_options` (the `getToken` /
`getTenant` / `getHeaders` getters) and `_refreshPromise` are `private`, so a consumer-side `fetch` helper
would need its **own** token refresh.

**A second refresh flow logs the user out.** Refresh tokens rotate — the Symbio server overwrites the
redeemed token. Two refresh flows that start together redeem the same token, the second fails, and
`onUnauthorized` clears auth. `ApiClient`'s shared `_refreshPromise` is what prevents that, so the upload
method belongs in the framework, not in the consumer. (Symbio's plan records this decision, confirmed by
the user 2026-09-30.)

## Change (Birko.Web.Core)

- `postForm<T = unknown>(path, form: FormData, opts?: { timeoutMs?: number }): Promise<ApiResponse<T>>`.
  It is `_fetch(path, { method: 'POST', body: form })` and sets **no** `Content-Type`, so the browser adds
  the multipart boundary. `_send` builds the headers, so auth, tenant and `getHeaders` apply unchanged.
- **Not queueable.** It does not go through `_sendWrite` / `_queue` and never calls `onQueueAction`: a
  `File` / `Blob` body cannot go into the JSON outbox. A network failure or timeout returns the normal
  `{ ok: false, status: 0 }` envelope and the caller handles it.
- The 401 → refresh → retry path is unchanged. A `FormData` body can be sent again (a `ReadableStream`
  cannot), so the existing retry resends it.
- `_fetch` takes an optional per-call timeout that overrides `options.timeoutMs` /
  `DEFAULT_REQUEST_TIMEOUT_MS`. The 20 s default is too short for a 10–20 MB upload on a slow uplink.
  Only `postForm` passes it. The timer still covers the body read, and `<= 0` still disables it.

## Acceptance criteria

- [x] A `FormData` body is sent with no `Content-Type` set by the client, and `Authorization`,
      `X-Tenant-Id` and `getHeaders` headers are present.
- [x] A 401 refreshes once and the retry resends the same `FormData`.
- [x] Two concurrent 401s (one `postForm`, one `get`) share a single `onRefreshToken` call — proven able
      to fail by breaking the shared promise.
- [x] The per-call timeout aborts into `status: 0`; without it, the client default applies.
- [x] `onQueueAction` is never called, even when offline.
- [x] `Birko.Web.Core/README.md` documents `postForm`.

Coverage lives in `Birko.Web.Playground` `src/backport-smoke.ts` (the frontend's regression harness —
there is no in-framework unit runner), beside the TASK-493 `ApiClient` checks.

## Out of scope

- Symbio's upload UI, `list-api.ts` helper and e2e spec — Symbio TASK-839 builds those on its own branch.
- Upload progress (`fetch` has no upload progress events).
- `putForm` / `patchForm`, and any offline outbox for binary bodies.
- The `GET /api/media/upload-limits` read Symbio TASK-839 also plans.

## Evidence

- Playground `node verify.mjs`: **backport-smoke 313/313** (was 293/293), all 13 suites green.
- `tsc --noEmit -p .` on the Playground (TypeScript 5.9.3): 0 errors.
- The no-`Content-Type` check also builds a `Request` from the sent init and asserts the browser then
  produces `multipart/form-data; boundary=…` — the half a fetch mock alone cannot see.
- **Proven able to fail.** Two mutations, each rebuilt and re-run, then restored:
  - *refresh not deduplicated* (`if (!this._refreshPromise)` → `if (true)`) → **311/313**: the concurrent
    upload + GET ran `onRefreshToken` twice, and the rotation model refused the second redemption and
    logged the user out — the exact failure this method exists to prevent.
  - *per-call timeout ignored* (`timeoutOverride ??` dropped) → **309/313**: the shorter override, the
    longer override, `timeoutMs: 0` and the armed-timer check all went red.

## Progress log

- 2026-09-30 — Filed from Symbio TASK-839's implementation plan.
- 2026-09-30 — Implemented in `Birko.Web.Core` (`src/http/api-client.ts`, `README.md`, `CLAUDE.md`), Web
  commit `6afc4e6`. 20 `TASK-508` checks added to `Birko.Web.Playground` `src/backport-smoke.ts`
  (Playground `a5731d4`).
- 2026-09-30 — Committed: Web `6afc4e6` (feature), Playground `a5731d4` (suite). Closing `done` — every
  acceptance criterion is met by automated coverage, with no human-only step outstanding. Symbio TASK-839
  calls `api.postForm` from its own branch and should pass `{ timeoutMs: 120_000 }` for uploads.
