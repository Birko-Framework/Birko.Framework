---
id: TASK-769
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P1
assignee: ai
created: 2026-10-26
depends-on: []
blocks: [TASK-770, TASK-771]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Shared diff test vectors for `diffJson` / `xmlToTree`, run by the web playground

## Context

FEATURE-020 D4 + D5 (D5 changed 2026-10-26). A C# port of the web diff engine (`Birko.Web.Core/src/diff/json-diff.ts`,
`xml-tree.ts`) is about to be written (TASK-770, TASK-771). The web engine's checks are the Playground's browser smoke
suites (`json-diff-smoke`, 76 checks, and `xml-diff-smoke`, from TASK-725 / TASK-727) — code, not data, so a port cannot
run them. One vectors file, owned by the web repo (the engine of record), is run by both sides so the two engines
cannot drift. `xmlToTree` uses `DOMParser`, so the web side runs the vectors in the Playground's browser harness, as a
new `diff-vectors-smoke` suite, rather than in a Node runner (D5's change).

Three repos: the vectors and README in **Birko\Web**, the suite in **Birko\Consumers\Birko.Web.Playground**, this task
file here — one commit in each.

Function-valued options (`normalize.normalize`, `arrays.key` as a function) cannot be data. The vectors name a small
closed set of normalizer ops (`trim`, `lower`, `replace` with a regex, `round`) that each runner maps to a function;
what lies outside it (function keys, the path a normalizer receives) stays in each side's own suite.

## Acceptance criteria

- [x] `Birko.Web.Core/src/diff/diff-vectors.json` with four sections: `paths` (format / match / malformed pattern),
      `json` (a, b, options → equal, summary, changes with kind/path/oldPath/oldValue/newValue where present, and the
      tree as depth-first `status path` lines), `xmlTree` (xml, options → tree or null) and `xmlLines` (xml → lines
      with paths, or null).
- [x] Coverage: every change kind; every tree status (`equal/added/removed/changed/modified/ignored`); type change
      object↔array↔value; `ignore` (incl. inside unordered pairing); chained `normalize`; arrays by index, `unordered`
      (incl. duplicates) and `key` (incl. duplicate keys and items missing the key); globs `*`, `[*]`, `**` (incl.
      zero segments) and quoted keys (`["a.b"]`); root path `''`; root primitives; null vs missing; numbers equal
      across spellings (`1` / `1.0`); integer-like key order; XML repeated elements → list, single element,
      `xsi:nil` → null, attributes, text beside attributes, CDATA, options, not-XML → null.
- [x] Every existing data-expressible case of `json-diff-smoke` and `xml-diff-smoke` is carried over as a vector.
- [x] `diff-vectors-smoke` in the Playground runs every vector against the web engine, is listed in `verify.mjs`, and
      passes. A vector the web engine fails is a web bug: file it, do not bend the vector.
- [x] Documented in `Birko.Web.Core/README.md` (diff section): what the file is, who reads it (the Playground suite and
      TASK-770's C# suite), and that changing engine behaviour means changing a vector.

## Out of scope

- The C# side (TASK-770, TASK-771).

## Human test plan

N/A — fully covered by automated tests (the suite is the deliverable).

## Implementation plan

## Review log

- 2026-10-26 — Built. Birko\Web `061f66a`: `Birko.Web.Core/src/diff/diff-vectors.json` (9 format, 22 match, 4 malformed,
  39 json, 27 xmlTree, 10 xmlLines cases; every data-expressible case of `json-diff-smoke` / `xml-diff-smoke` carried
  over, plus integer-like key order, number spellings, key types, the empty pattern, quoted and non-ASCII keys, nil
  children, whitespace, entities, PIs) and the README section. Birko.Web.Playground `f656379`: `diff-vectors-smoke`,
  wired into `app.ts`, `build.js` (copies the file) and `verify.mjs`.
- Gate: `diff-vectors-smoke` 112/112 on the first run against the engine (expectations derived by hand from the
  source, not captured from it). **Proved it can fail:** three bent vectors in the built copy gave 109/112, each named.
  Full `verify.mjs`: 0 failing (json-diff-smoke 76/76, xml-diff-smoke 40/40 unchanged).
- No Birko.Web version bump: the shipped library code did not change.
