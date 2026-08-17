---
id: TASK-202
parent: EPIC-018
feature: FEATURE-018
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-11
completed: 2026-08-11
depends-on: []
blocks: []
findings: []
pr: 79e786e (Birko.Web.Core, landed 2026-07-31), bec9e04 (Birko.Web.Playground)
github-issue: null
jira-key: null
---

# `ApiClient.get` corrupted any endpoint that already carried a query string

## Context

**Tracking backfill.** The fix landed in `Birko.Web.Core` on **2026-07-31** as `79e786e` with no aggregator
commit — no task, no `pr:` sha, no spec regen. Found by diffing sibling `git log` against this repo's HEAD,
the same sweep that produced [[TASK-196]]/[[TASK-197]]. Third instance of the missing-third-commit pattern.

`get()` appended `'?'` unconditionally, so a caller combining an endpoint that already carried a query
string with `params` produced two `?` in one URL:

```
api/warehouse/stock?warehouseConfigId=W1  +  { page: '1', pageSize: '20' }
-> api/warehouse/stock?warehouseConfigId=W1?page=1&pageSize=20
```

The server read `warehouseConfigId` as `W1?page=1` and returned an **empty list** — no error, no console
message, no failing request, just a list rendering "No data" while the API has rows. Latent for as long as
no caller did both; it became reachable from ordinary code the moment `b-data-table` began sending
`page`/`pageSize` on a first load, and a scoped list endpoint carrying its own query string is a normal
pattern.

## Approach

Join with `'&'` when the path already contains `'?'` — since superseded by `appendQuery` in `http-utils`
([[TASK-199]]), because `SseClient` and `WsClient` had been choosing the separator correctly all along,
three lines apart in the same folder.

## Acceptance criteria

- [x] An inline query param survives added params intact
- [x] The added params are readable alongside it
- [x] Exactly one `?` in the result
- [x] Back-compat: a bare path still gets its `?`; absent and empty params append no separator
- [x] Framework-side coverage, not only the consumer's

## Outcome

Fix landed 2026-07-31; **framework coverage added 2026-08-11** (`bec9e04`) — 6 checks, `backport-smoke`
270 (was 264). Red-verified: 3 of 6 fail on restoring the unconditional `'?'`, the 3 back-compat checks stay
green.

- **Its only guard lived in the consumer that reported it** — Symbio's
  `tests/ui-e2e/list-paging-consistency-check.spec.ts`. That is the wrong home for a defect in a client
  shared by every `Birko.Web` consumer: only the one that happened to notice was covered. Same shape as the
  `b-segmented` touch floor, whose sole coverage sat in a consumer for two months and was blessed there by a
  single-locale suite.
- **The assertion has to read the param back by name.** `searchParams.get('warehouseConfigId')` returns
  `'W1?page=1'` pre-fix — the server's own reading of it. Counting `?` would bless any fix that merely
  relocated the corruption, so the count check is support, not the assertion.

## Out of scope

Encoding of the path's own inline query string — the caller owns it.
