---
id: TASK-108
parent: STORY-051
feature: null
status: todo
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
findings: [SH-H039]
---

# `Pbkdf2PasswordHasher.Verify` returns `true` for any password against an empty-segment hash

## Context

`../Birko.Security/Hashing/Pbkdf2PasswordHasher.cs:73` — **CONFIRMED**, traced by hand and re-checked by
running both APIs.

A stored column of `PBKDF2-SHA512:600000::` splits into four parts and passes both the algorithm and the
iteration guards. Then:

1. `Convert.FromBase64String("")` returns `byte[0]` **without throwing**, so the `FormatException` guard
   added for CR-M233 never fires;
2. `storedHash.Length` is `0`, and it is what drives `outputLength`, so Pbkdf2 is asked for a zero-length
   key and returns an empty array;
3. `CryptographicOperations.FixedTimeEquals(empty, empty)` is **`true`**.

Any user row whose hash column was truncated, defaulted to `''`, or written by a half-finished migration
authenticates **with any password**. `Hash()` never emits this shape, so it takes a corrupted or
placeholder column to reach — but nothing in `Verify` treats that as a reason to fail closed.

**Two more defects found while verifying**, both in the same method and both in scope here:

- an iterations segment of `0` or negative throws `ArgumentOutOfRangeException` **out of** `Verify`, so a
  corrupt column raises an unhandled exception on the login path instead of returning false;
- `storedHash.Length` driving the derived length means a **1-byte truncated hash matches an arbitrary
  password roughly 1 in 256** — a weaker but non-empty version of the same bug, and the reason the fix
  cannot just be "reject empty".

## Approach

Fail closed on any stored hash that is not well-formed, before any comparison. The derived length must come
from the **algorithm**, not from the stored hash's length — that inversion is the root cause of both the
empty and the truncated case. Validate: 4 segments, known algorithm, iterations > 0, salt non-empty, hash
length equal to the algorithm's expected output size. Anything else → `false`, no exception.

## Acceptance criteria

- [ ] `Verify("anything", "PBKDF2-SHA512:600000::")` returns `false`
- [ ] A hash segment shorter than the algorithm's output length returns `false` (the ~1/256 case), not a
      probabilistic match
- [ ] An empty **salt** segment returns `false`
- [ ] Iterations of `0`, a negative number, or a non-integer returns `false` and **throws nothing**
- [ ] A malformed segment count, an unknown algorithm, and non-Base64 content all return `false`
- [ ] A genuine `Hash()` → `Verify()` round trip still succeeds, and a wrong password still returns `false`
- [ ] Derived length is taken from the algorithm, not from `storedHash.Length` — asserted directly
- [ ] Regression tests in `Birko.Security.Tests` covering each row above
- [ ] `/specs regen` for `security-and-authorization`, spec diff reviewed

## Out of scope

- `SH-H040` (`AuthenticationService.ValidateToken` fails open when authentication is disabled or expands to
  nothing) — same area, different mechanism, still unverified. Separate task.
- Rotating or repairing already-corrupted hash columns in consumer databases. This task makes them
  unusable-but-safe; a migration to detect them is a consumer concern worth flagging in the fix's notes.
- `Birko.Security.BCrypt` — a different hasher, already corrected under CR-C24.

## Human test plan

N/A — covered by automated tests. The defect is a pure function of its inputs and every case above is
assertable in-process.
