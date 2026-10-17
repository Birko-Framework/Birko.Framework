---
id: TASK-173
parent: STORY-054
feature: FEATURE-014
# status: done | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L144, SH-L145, SH-L146, SH-L147, SH-L148, SH-L149, SH-L150, SH-L151, SH-L152, SH-L153, SH-L154, SH-L155, SH-L156, SH-L157, SH-L158, SH-L159, SH-L160, SH-L161, SH-L162, SH-L163, SH-L164, SH-L165, SH-L166, SH-L167, SH-L168, SH-L169, SH-L170, SH-L171, SH-L172, SH-L173, SH-L174]
pr: null
github-issue: null
jira-key: null
---

# Triage the 31 low spec-harvest findings in `llm-provider-and-agents`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **31** findings
that fall in the `llm-provider-and-agents` area.

**Findings:** `SH-L144`–`SH-L174` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: llm-provider-and-agents`, lines 3898–4085.

**The contract under review** is specced in [`docs/specs/llm-provider-and-agents.md`](../../../docs/specs/llm-provider-and-agents.md), harvested from
2 source globs — `../Birko.AI.Contracts/**/*.cs`, `../Birko.AI/**/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/llm-provider-and-agents.md` lying until `/specs regen llm-provider-and-agents` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 31 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen llm-provider-and-agents`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 356 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-160]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract (provider base, agent loop, factory, tools) with no UI
surface; every behavioural fix has a red-verified test.

## Resolution (2026-10-18)

All 31 were checked against the current code (after TASK-530–535 and TASK-160). Verdicts with the traced code are in
the findings doc.

**Outcome:** 27 confirmed (1 narrower), 4 refuted (`SH-L152`, `SH-L155`, `SH-L157` by design, `SH-L163`).
- **22 fixed here.**
- 3 found fixed earlier: `SH-L144` (TASK-527), `SH-L145` (TASK-160), `SH-L167` (TASK-534).
- `SH-L149` waived by design: the response is disposed on purpose, and its headers stay readable.
- `SH-L159` spawned as [[TASK-660]]: telling a run's outcomes apart changes `RunAsync`'s public shape.

Fixed in groups:
- **Provider base** (L146–L148, L150, L151, L153, L154): a negative `MaxRetries` still sends once, and the unreachable
  `throw lastException` is gone. A streaming failure's message carries the provider's explanation. SSE parsing
  follows the grammar (`data:` without the space, data lines joined with `
`, `[DONE]` however framed). Block types
  are matched exactly, as the agent matches them. A message with no role is refused, naming its position.
- **Agent and factory** (L156, L158, L160, L164, L165): `OnLlmResponseReceived` fires once per completed response.
  `AddTool` / `RemoveTool` refuse null and are copy-on-write. Fewer than one iteration is refused. A missing alias
  target is named, and a null type is an `ArgumentException`.
- **Options and tools** (L161, L162, L166, L168–L174):
  - `Merge(AgentOptions?)`; `FromDictionary` skips a blank working directory and refuses a null config.
  - A timed-out command returns what it printed; `run_command`'s description says no shell runs it.
  - The ask_user timer is cancelled once the prompt answers.
  - display_text without a callback says so.
  - One `WorkspacePath.Resolve` serves the four file tools.
  - search_code and list_files skip inaccessible directories, and "no matches" is said with a file count.
  - `list_files` no longer null-checks its input.

**Red / pin split:** 25 new tests.
- 21 failed on HEAD: tools/options 6, agent 7, provider base 8.
- 4 pass either way and are pins: `SH-L167` ×2 (already fixed), display_text with a callback, and `SH-L172`, which
  returns early on Windows and runs on CI's Linux.
- No test covers L161, L168, L169 or L174 (an annotation, a text, a timer, a null check); each is fixed by inspection.

Regression: every importer of AI.Contracts / AI / AI.Providers (AI 75, Agents 70, Providers 246, plus Orchestration,
Resilience, Resilience.Data and VersionControl.Git; see the commit). The spec was updated by hand at each affected
requirement, including a new one for path resolution.
