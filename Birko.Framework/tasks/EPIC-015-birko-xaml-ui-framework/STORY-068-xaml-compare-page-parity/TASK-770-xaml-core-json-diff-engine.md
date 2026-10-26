---
id: TASK-770
parent: STORY-068
feature: FEATURE-020
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P1
assignee: ai
created: 2026-10-26
depends-on: [TASK-769]
blocks: [TASK-772, TASK-773]
findings: []
pr: null
github-issue: null
jira-key: null
---

# JSON diff engine in Birko.Xaml.Core — a port of `diffJson`

## Context

FEATURE-020 D1 + D2. The FinStat desktop tester (ClientApi.CSharp, session clientapi-csharp-d1) is building its compare
window against a placeholder and swaps this in. **It is the consumer's blocker.**

Port `Birko.Web.Core/src/diff/json-diff.ts` (323 lines) to `Birko.Xaml.Core` (namespace `Birko.Xaml.Core.Diff`), on
`System.Text.Json.Nodes` — no new package; Core stays Avalonia-free (its csproj constraint). The web file is the
specification: same path format (`Items[2].Ref`, `["a.b"]`, root `''`), same pattern grammar (`*`, `[*]`, `**`, whole-path
match), same change kinds, tree statuses, `oldPath` / `oldSegments` placement under keyed/unordered arrays, and summary
counting (children of an added/removed container repeat its status, not counted again).

The tester uses `normalize` on `**` to replace hosts with `{sk}` / `{cz}`, and folds `[\d+]` to `[*]` in change paths to
count "fields that differ" across IČOs.

## Acceptance criteria

- [ ] Public API: `JsonDiff.Diff(JsonNode? a, JsonNode? b, JsonDiffOptions? options = null) → JsonDiffResult`
      (`Equal`, `Changes`, `Tree`, `Summary`); `JsonChange`, `JsonChangeKind`, `JsonDiffNode`, `JsonDiffStatus`,
      `JsonDiffSummary`, `JsonNormalizer` (`Path`, `Func<JsonNode?, string, JsonNode?> Normalize`), `JsonArrayRule`
      (`Path`, `Match` index/unordered, `Key` string or `Func<JsonNode?, object?>`), plus `JsonPath.Format(segments)` and
      `JsonPath.Matches(pattern, segments)`. Names may be adjusted in implementation; the shape follows the web types.
- [ ] Old/new values are reported as received (before normalizing), as on the web.
- [ ] An invalid pattern throws, as the web one does.
- [ ] `tests/Birko.Xaml.Core.Tests` runs every JSON case in TASK-769's vectors, read from the sibling `Birko\Web` checkout
      (resolved like `BIRKO_SRC`; skips with a stated reason when absent, fails under `BIRKO_REQUIRE_LIVE=1`-style opt-in).
      All pass.
- [ ] C#-only tests for what the vectors cannot express: function normalizers and function keys.
- [ ] As soon as the public types are stable, send clientapi-csharp-d1 the public types and a usage snippet (diff call
      with ignore/normalize/arrays, reading changes/summary). Record the send in this file's Review log.
- [ ] Recent Updates entry in `CLAUDE.md`; `Birko.Xaml.Core/README.md` + `CLAUDE.md` document the Diff namespace.

## Out of scope

- XML conversion (TASK-771). The viewer (TASK-772, TASK-773).
- A non-UI home for the engine (FEATURE-020 D3, deferred).

## Human test plan

N/A — fully covered by automated tests; the consumer's swap-in is its own check.

## Implementation plan

## Review log
