---
id: TASK-470
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-280, TASK-315, TASK-316]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Three lessons never reached the rulebook, and one entry claims a promotion that never happened

Found while splitting `CLAUDE.md` on 2026-09-19. § Recent Updates and § Conventions have a division of
labour — the **measurement** goes in the entry, the **rule** it earns goes in the rulebook — and the
promotion is manual, so it can be skipped silently. Measured across the 38 entries then present:

| | |
|---|---|
| Distinct TASK ids in § Recent Updates | 98 |
| …whose rule is stated in § Conventions | 58 |
| …that are `[[links]]` to other tasks, not lessons | 29 |
| …distilled into a different section (§ Task tracking, § Skills) | 3 |
| …consumer ids (Symbio's TASK-657, TASK-690) | 2 |
| **…framework lessons that appear nowhere but their own entry** | **7** |

Of those 7, four were checked and are fine — TASK-195, TASK-312 and TASK-321 have their rule in the
rulebook without citing the task id, and TASK-038 is a playground/tooling lesson rather than a code
convention. **Three are genuinely absent.**

## The three

1. **[[TASK-280]] — and this is the one worth doing first, because the entry ASSERTS the promotion.**
   It reads: *"a test was asserting the defect — third consecutive task … it is now a standing rule:
   **when a defect survives a well-covered area, check whether a test is holding it in place**"*. That
   rule is **not in the rulebook**. The nearest neighbour is a different statement (*"a suite can
   ENCODE the defect"*, on the `IsNullOrEmpty` rule), which is the same family but not the same claim.
   So an entry announced a rule into existence and nothing carried it across — which is exactly the
   failure mode this task is about, stated by the tree about itself.
   ⚠ Note it is cited as *third consecutive*: TASK-284's `[InlineData("")]` and TASK-279's
   `..._DefaultsOrderByTime_...` are the other two, so the evidence for the rule is already assembled.

2. **[[TASK-315]] — `WorkflowInstanceOwnership`.** An instance is keyed by `InstanceId` alone across
   every workflow and `TData` type, so a save could relabel and overwrite another workflow's instance;
   the fix refuses at one producer and the refusal names both doors. Nothing in the rulebook states it,
   and the shape is general — a shared table keyed by less than its identity.

3. **[[TASK-316]] — `MapToModel` may now receive a POPULATED target.** A ViewModel update maps onto a
   detached copy of the stored row rather than a fresh instance, so an implementation that accumulates
   rather than assigns is now wrong. That is a **contract change a consumer meets**: it was checked
   against all nine consumer implementations at the time (0 of 9 accumulate), but nothing in the
   rulebook tells the tenth.

## Acceptance criteria

- [ ] Each of the three is either written into `CLAUDE-conventions.md` in the rulebook's own form
      — the statement, then the measurement that earned it — or explicitly rejected with the reason
- [ ] TASK-280's is the one to settle first, and settling it means deciding whether it is a **new**
      rule or a widening of the existing *"a suite can ENCODE the defect"* bullet. Do not add a second
      bullet saying nearly the same thing; that is the duplication the rulebook keeps warning about
- [ ] The index in `CLAUDE.md` § Conventions is regenerated so the count and the numbering match
- [ ] ⚠ Do **not** treat "the id is absent from the rulebook" as the test. Three of the seven have
      their rule stated without the citation, and a sweep keyed on ids would have re-filed all three.
      The test is whether the *statement* is there

## Out of scope

- The other 35 entries. Measured as either already distilled, `[[links]]`, or consumer ids.
- A mechanism to stop this recurring (a close-gate check that a Recent Updates entry's rule landed).
  Worth considering, but it is a process change and this is three paragraphs of writing.
- Rewriting existing rulebook entries. Only additions here.
