---
id: TASK-126
parent: STORY-051
feature: FEATURE-014
status: todo
priority: P1
assignee: ai
created: 2026-07-31
depends-on: []
blocks: []
related: [TASK-114, TASK-125]
pr: null
github-issue: null
jira-key: null
findings: [SH-H019]
---

# `TagServiceBase` states its tenant contract in a comment and enforces nothing

## Context

`../Birko.Data.Tagging/Services/TagService.cs:11` — **CONFIRMED by hand (2026-07-31)**.

The class opens with the contract written out in prose:

```
// TENANT-SCOPING CONTRACT: this base class stamps TenantGuid = GetCurrentTenantId() on every
// insert, but the read/delete hooks below receive NO tenant parameter — implementations MUST
// scope every one of them (including GetTagByIdAsync) to the ambient tenant themselves.
// A hook that skips the filter returns/deletes other tenants' data; the base class has no guard.
```

That comment is accurate, and it is the whole enforcement. All 12 abstract data-access hooks (lines 16–28)
take no tenant parameter, and `TagServiceBase` never compares a loaded record's `TenantGuid` to
`GetCurrentTenantId()` — which it has, at line 30. `Tag` carries `TenantGuid`.

**It is not only a read leak.** `UpdateTagAsync:70` and `DeleteTagAsync:82` both reach their target through
the same unguarded `GetTagByIdAsync`, and `DeleteTagAsync` then calls `DeleteAllEntityTagsForTagAsync(tagId)`.
So one implementation omitting one filter in one hook exposes cross-tenant **writes** and a cascade delete,
not just reads — across every implementation of the base, of which there may be N.

Same family as [[TASK-114]] (write guard trusted a caller-settable field) and [[TASK-125]] (read helper
skipped the decorator chain), one rung weaker: this one trusts the *implementor*.

## Why this is cheap

`GetTagByIdAsync` is a **single choke point** — get, update and delete all funnel through it. A post-load
assertion there turns a silent cross-tenant leak into a hard failure for every implementation at once,
without touching any of them. The list/search/link hooks need their own handling but are read-only.

## Approach

Add a base-class assertion on every record that comes back from a hook, comparing `TenantGuid` to
`GetCurrentTenantId()`. Decide two things explicitly:

1. **Fail-closed vs filter-out.** For the by-id path a **throw** is right — the caller named a specific id
   and getting silence would hide the misconfiguration that caused it. For `ListAllTagsAsync` /
   `SearchTagsByNameAsync` / the `GetEntityTagLinks*` batch hooks, throwing on one foreign row would take
   down a whole list; filtering is probably right there, but a filtered-out row is exactly the signal that an
   implementation's hook is unscoped, so it must at minimum be observable rather than silent.
2. **What "no tenant" means.** `GetCurrentTenantId()` returns a non-nullable `Guid`. Check what an unset
   tenant resolves to before writing the comparison — `Birko.Data.Tenant` learned the hard way
   (`ModelByTenant`) that `Guid.Empty` is *a tenant value*, not "unset", and that treating it as a wildcard
   makes reads fail open while writes fail closed. Do not repeat that.

The prose contract stays, but it should then describe an enforced invariant rather than ask implementors to
be careful.

## Acceptance criteria

- [ ] A hook returning a foreign-tenant `Tag` from `GetTagByIdAsync` causes `GetTagAsync`, `UpdateTagAsync`
      and `DeleteTagAsync` to **fail** rather than operate on it — asserted with a deliberately unscoped
      test implementation of the base
- [ ] `DeleteTagAsync` does not reach `DeleteAllEntityTagsForTagAsync` for a foreign tag
- [ ] The list / search / entity-link paths do not return foreign-tenant records, and whatever they do
      instead (filter or throw) is deliberate, documented, and not silent
- [ ] `Guid.Empty` behaviour is decided explicitly and pinned by a test — no wildcard-by-accident
- [ ] A correctly-scoped implementation is unaffected: no extra failures, no behaviour change
- [ ] Regression tests in `Birko.Data.Tagging.Tests`
- [ ] `/specs regen` for `entity-tagging`, spec diff reviewed
- [ ] `Birko.Data.Tagging/CLAUDE.md` updated — the contract is now enforced, not advisory

## Out of scope

- Auditing consumer implementations of `TagServiceBase` for missing filters. The point of this task is that
  the base stops depending on them being correct.
- `SH-H036` ([[TASK-125]]) and the tenant-wrapper work in [[TASK-114]].

## Human test plan

N/A — covered by automated tests. A deliberately unscoped test implementation stands in for the
misconfigured consumer.
