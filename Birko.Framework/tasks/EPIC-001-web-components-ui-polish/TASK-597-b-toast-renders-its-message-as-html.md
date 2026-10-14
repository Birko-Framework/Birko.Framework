---
id: TASK-597
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-035]
pr: null
github-issue: null
jira-key: null
---

# b-toast renders its message (and icon) as HTML, and server error text is passed to it

## Context

Found while building [[TASK-545]] (2026-10-14). `BToastItem.render()`
(`Birko.Web.Components/src/feedback/b-toast.ts`) interpolates `this.attr('message')` and `this.attr('icon')` into
the shadow root's markup without escaping, so the message is parsed as HTML.

Callers pass text a server or a network layer wrote: `toast.error(apiErrorMessage(resp.data))` in
`base-crud-page.ts` (delete, save, open-edit, restore) and `base-form-modal.ts`, and the pattern is the documented
example in `Birko.Web.Core/src/http/http-utils.ts`. A ProblemDetails `detail` that echoes user input
(a name, a code) containing `<img src=x onerror=…>` would run in the page. TASK-545's own restore path also shows
`err.message`.

The action label added by TASK-545 is escaped; the message and icon are not.

## Acceptance criteria

- [ ] The message renders as text: markup in it is shown, not parsed (`<b>x</b>` reads literally)
- [ ] `icon` is decided explicitly: escaped as text, or an allowlisted icon form (glyph / known icon name). It must not
      become an HTML sink
- [ ] If any caller relies on HTML in a message today (search Birko.Web and the consumers: Symbio, FlowerFurStudio,
      Presenter, DraCode), there is a separate, named opt-in for it. The default stays text
- [ ] Playground smoke: a message and an icon carrying `<img onerror>` / `<script>` render inert, and the text is visible

## Out of scope

- Escaping in other components. Survey them as a separate task if this one finds the pattern elsewhere

## Human test plan

N/A — covered by automated tests (the rendered text and the absence of parsed elements are asserted in-page).

## Implementation plan

_Populated by `/tasks plan TASK-597` — leave empty until then._
