---
id: TASK-486
parent: EPIC-001
feature: FEATURE-001
status: done
priority: P2
assignee: ai
created: 2026-09-24
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# The select-all header rendered as literal HTML, so "select all" was unusable

> **Moved here from Symbio `TASK-499` (2026-09-24).** The task was filed in the consumer's tree
> because the reporter saw it on a Symbio page, but the defect and its fix are entirely inside
> `Birko.Web.Components`. It now lives with `EPIC-001`, where the component's other tasks are, and
> Symbio's file is kept as a `cancelled` pointer. Reported 2026-08-20; fixed 2026-09-24.

## Context

`<b-data-table>`'s header showed the text

```
<input type="checkbox"  class="select-all" aria-label="Select all rows" />
```

where a checkbox should be. The cause was an escaping asymmetry **inside one component pair**, with
exactly one emitter of the string in the whole codebase.

`b-data-table` built its selection column by putting the markup in a column **label**:

```ts
// Birko.Web.Components/src/data/b-data-table.ts (before)
label: `<input type="checkbox" ${allSelected ? 'checked' : ''} class="select-all" aria-label="…" />`,
```

and `<b-table>` escaped every header label:

```ts
// b-table.ts (before)
: escapeHtml(String(c.label ?? ''))
```

⚠ **The asymmetry was the bug.** Cells had an HTML opt-in (`render`) and headers had none, so the
**per-row** checkboxes worked while the header one became text. The feature half-worked: rows could be
selected one at a time, "select all" did nothing, and `_wireTableInteractions` went on querying
`.select-all` for a node that never existed.

**Blast radius at report time was one page** — `selectable: true` appears exactly once in Symbio
(`modules/products/list/list-page.ts:386`), and `b-data-table.ts` is the only emitter of the string.
Any future selectable table would have inherited it.

✅ **Route confirmed by the reporter: `#/products/list`.** It was first reported as
`#/products/categories`, which cannot produce the string — that page renders a `b-tree-menu` with no
`b-data-table` and no `selectable`. Two independent signals settled it: `selectable: true` appears
exactly once in Symbio, and `b-data-table` was the only emitter; and the reporter, on the same screen,
referred to *"Ľubovoľný stav"* — `products.storefrontAll`, a filter that exists only on the products
list.

## The fix — `headerRender`, the header-side counterpart to `render`

`TableColumn` gained an explicit, code-only opt-in:

```ts
headerRender?: (column: TableColumn) => string;
```

`label` stays escaped; only a column that supplies a **function** can emit markup. A raw string field
(`labelHtml`) was rejected for exactly that reason — it is indistinguishable from data at the call
site, whereas a function is unambiguously code, the same contract `render` already carries.
`b-data-table` now sends its header checkbox through `headerRender`.

*(The old task note said "Birko.Web.Components **is** its own git repository". That is stale: TASK-457
consolidated the frontend into the `Birko-Framework/Birko.Web` monorepo. The fix is `7e26fba` **in that
repo**, and it commits and diffs normally.)*

## Commits

- **`Birko-Framework/Birko.Web` `7e26fba`** — the fix, in `b-table.ts` (`TableColumn.headerRender` +
  the escaped-by-default render path), `b-data-table.ts` (selection column switched to it; the
  interpolated `id` / label values now go through `escapeAttr` too), plus `README.md` / `API.md`.
- **`Birko-Framework/Birko.Web` `0e88d87`** — review-gate cleanup: comments the commit already carried
  were trimmed, and `Birko.Web.Components/CLAUDE.md` gained the rule (register-on-introduce).
- **`Birko-Framework/Birko.Web.Playground` `7b605ad`** — `table-header-smoke` (16 checks) and its
  wiring into `?smoke=1` / `verify.mjs`.
- **`Birko-Framework/Birko.Web.Playground` `726a9e4`** — the smoke header trimmed to a TASK pointer.

## Acceptance criteria

- [x] The header renders a working checkbox, and clicking it selects and clears the page's rows.
      → `table-header-smoke`; select-all selects 3/3, emits `selection-change` with the count, a
      second click clears, and one row selected leaves the header `indeterminate`.
- [x] The **asymmetry** is fixed, not the symptom — header labels are not blanket-un-escaped, so no
      new stored-XSS route. → default path is still `escapeHtml`; `headerRender` is the opt-in.
- [x] A header label containing `<` from ordinary data is still escaped. → asserted twice: an
      element-free `th` and `&lt;b&gt;` in the markup. **Mutation-proven**: dropping `escapeHtml` from
      the default path fails exactly those 3 checks.
- [x] Committed upstream and referenced from this task. → commits above. **Mutation-proven the other
      way**: restoring the old label-markup shape fails the select-all checks and reproduces the
      literal `<input>` text.

## Out of scope

- The row-level checkboxes; they always worked. (Their interpolated values were escaped as a
  drive-by hardening, not a fix.)
- `b-data-table` paging / sorting behaviour.
- Pinning the component library by version instead of by path — deferred to **TASK-487**. A real,
  larger gap: Symbio aliases `birko-web-components` to
  `${BIRKO_SRC}/Birko.Web.Components/src/index.ts`, so a local edit reaches its bundle before either
  commit is pushed and nothing records which build carries which commit. Found while doing this task;
  spawned rather than widened into it.

## Human test plan

- [x] On `#/products/list`, select all rows, clear them, then select one row by hand and confirm the
      header reflects the **mixed** state sensibly. The header checkbox is a three-state affordance in
      practice (none / some / all); `indeterminate` is asserted, but whether it *reads* right is a
      judgement, not an assertion.

✅ **Run by the user on Symbio, 2026-09-25 — OK.** Closes the task: code and automation were
already green (`table-header-smoke` 16/16, full `verify.mjs` exit 0), and this visual judgement was the
one step left.

## Review gate (2026-09-24)

Verdicts, one per pass that ran — never merged or reranked:

- **Standards ([[verify-conventions]])** — rulebook `Birko.Web.Components/CLAUDE.md`. 1 ⚠
  register-on-introduce: `headerRender` introduced a header-side HTML opt-in with no rule recorded.
  **Fixed in `0e88d87`** (§ *Header content is escaped by default*). No other findings.
- **Intent ([[verify-intent]])** — all four acceptance criteria met against the diff; none ticked from
  a summary. ✅
- **Correctness ([[code-review]])** — ✅ no correctness issues. Read the callers: `label` is used only
  in the header, the select column is not `sortable`, and the render path is otherwise unchanged.
- **Security ([[security-review]])** — ran, because the diff inserts HTML and handles consumer-supplied
  labels. ✅ No exploitable path: the raw sink is a **function** (code, not data), `label` stays
  escaped, and `escapeAttr` closed the pre-existing unescaped `data-id` / `aria-label` interpolations.
- **Comments ([[review-comments]])** — rule: none recorded in `Birko.Web.Components`, so the universal
  floor (rung 3). 4 ⚠ findings — an inline comment restating its own JSDoc, a defect-history paragraph
  whose destination is this task and `7e26fba`, the same in `b-data-table.ts`, and the smoke header.
  **Fixed in `0e88d87` / `726a9e4`**; the comments that carry only what the code cannot (“why the node
  is re-queried”, “off-screen so layout runs”) were left alone.
- **Out-of-scope sweep** — 2 boundaries, 1 spawned (**TASK-487**), 0 declined.

**Conventions extension:** `.claude/skills/verify-birko-conventions/SKILL.md` examined — it lints the
.NET `Birko.Framework` rulebook, and this diff is TypeScript in the `Birko.Web` repo, so its checks
1–10 are N/A. The applicable rulebook is `Birko.Web.Components/CLAUDE.md` (the TS project's own).