---
id: TASK-799
parent: null
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Sign-in state that survives a restart and works on several servers

## Context

`InMemoryOAuthStateStore` (TASK-521) is the only `IOAuthStateStore`. It holds sign-in states in one process:
- a restart loses every sign-in in progress;
- on a host with more than one server, a callback that reaches another server finds no state, and the user gets
  `InvalidState`.

`IOAuthStateStore`'s own remarks say a multi-node host must implement a shared store, but the framework gives it none.
Every web sign-in uses it (GitHub, and Google / Microsoft through TASK-778's `OpenIdSignIn`).

## Acceptance criteria

- [ ] A Redis store over `Birko.Redis`'s connection manager:
  - the state is the key, and expiry is a Redis TTL;
  - consumption is atomic (`GETDEL`, or a script on servers without it), so two concurrent callbacks with one state
    succeed at most once;
  - the binding is stored hashed and compared in constant time, as the in-memory store does.
- [ ] A store-backed implementation over any `IAsyncBulkStore<T>` (SQL, MongoDB, …), for hosts without Redis:
  - consumption is atomic on every backend: a conditional delete whose affected count decides the winner, never read
    then delete;
  - expired rows are swept;
  - placement (which project, and whether it needs a model) is decided and recorded.
- [ ] A shared contract test suite that every `IOAuthStateStore` runs:
  - create / consume, consumed only once, wrong binding (refused, and the state is burned);
  - expiry on the clock;
  - two concurrent consumes, at most one wins.

  The in-memory store runs it too.
- [ ] Live tests against Redis (and one SQL provider) under the `BIRKO_*` live-suite switches, skipped without them.
- [ ] Docs: `docs/communication.md` § web sign-in, the projects' README / CLAUDE.md, catalog and dependency flow if a
      project is added.

## Human test plan

N/A — fully covered by automated tests, including the live Redis and SQL suites. A consumer running two instances
behind a load balancer is the real-world check, when one exists.
