---
id: TASK-108
parent: STORY-051
feature: null
status: done
priority: P0
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: 2a19150   # Birko.Security; tests aeb9307 in Birko.Security.Tests
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

- [x] `Verify("anything", "PBKDF2-SHA512:600000::")` returns `false`
- [x] A hash segment shorter than the algorithm's output length returns `false` (the ~1/256 case), not a
      probabilistic match
- [x] An empty **salt** segment returns `false`
- [x] Iterations of `0`, a negative number, or a non-integer returns `false` and **throws nothing**
- [x] A malformed segment count, an unknown algorithm, and non-Base64 content all return `false`
- [x] A genuine `Hash()` → `Verify()` round trip still succeeds, and a wrong password still returns `false`
- [x] Derived length is taken from the algorithm, not from `storedHash.Length` — asserted directly
- [x] Regression tests in `Birko.Security.Tests` covering each row above
- [x] `/specs regen` for `security-and-authorization`, spec diff reviewed

## Outcome

One guard added to `Verify`, before any comparison: `iterations <= 0` joins the `TryParse` check, and
`salt.Length == 0 || storedHash.Length != HashSize` rejects the value outright. The derivation then asks
for `HashSize` rather than `storedHash.Length` — that one argument was the root cause of both the empty
and the truncated case.

**The suite splits, and the split was measured.** `Birko.Security.Tests` is 47/47 green with the fix.
Reverting only `Pbkdf2PasswordHasher.cs` and re-running gives **12 failures, exactly the fix-dependent
rows** — the 3 `AllEmptySegments` passwords, the 4 `TruncatedHashSegment` lengths, the crafted one-byte
column, and iterations `0` / `-1` / `-600000` (which threw rather than returned). Everything else,
including the whole pre-existing `PasswordHasherAndEncryptionTests` class, passed both ways.

Worth recording, because it corrects two assumptions in the analysis above:

- **`Verify_TruncatedHashSegment` is the direct assertion of criterion 7**, and it works because PBKDF2
  output is prefix-stable: the truncated segment *is* byte-for-byte what the correct password derives to
  at that length, so under the old code every one of those four rows returned `true` for the **correct**
  password. Nothing weaker demonstrates the length inversion — a wrong-password test would have passed
  either way.
- **Two of the new tests are guards, not proofs.** `Verify_EmptySaltSegment` and
  `Verify_OverlongHashSegment` pass with the fix reverted: an empty salt derives a *different* 32-byte
  key, and a 64-byte segment derives 64 bytes that don't match. They pin the contract; they were never
  evidence of the bug. Keeping them is right, calling them fix-dependent would not have been.

**Salt is checked for non-emptiness, not for `SaltSize`.** A short-but-present salt is weak salting, not a
bypass — the comparison still runs over all 32 bytes — so requiring exactly 16 would reject stored values
without closing anything, and this task's contract is fail-closed, not re-key. Same reasoning for leaving
the iteration floor at `> 0` rather than the constructor's 10,000: a low-iteration column is a rehash
concern, and rejecting it here would lock out every user written before an iteration bump.

**Flagged, not fixed:** an iteration segment of `2147483647` is well-formed and would burn CPU for minutes
on the login path. It needs DB corruption to reach and an upper bound is a policy call (it caps legitimate
future hardening), so it is not decided here. Worth a follow-up task if the login path is ever exposed to
untrusted stored values.

## Out of scope

- `SH-H040` (`AuthenticationService.ValidateToken` fails open when authentication is disabled or expands to
  nothing) — same area, different mechanism, still unverified. Separate task.
- Rotating or repairing already-corrupted hash columns in consumer databases. This task makes them
  unusable-but-safe; a migration to detect them is a consumer concern worth flagging in the fix's notes.
- `Birko.Security.BCrypt` — a different hasher, already corrected under CR-C24.

## Human test plan

N/A — covered by automated tests. The defect is a pure function of its inputs and every case above is
assertable in-process.
