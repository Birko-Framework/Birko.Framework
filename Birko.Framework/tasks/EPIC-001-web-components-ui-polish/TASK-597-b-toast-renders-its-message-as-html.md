---
id: TASK-597
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: in-progress
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: ai
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

Drafted at pick, 2026-10-14.

**Survey (criterion 3):** of the consumers only Symbio calls toasts (536 calls; FlowerFurStudio, Presenter and DraCode
none). No call passes markup: the three Symbio locale strings that carry HTML (`dashboard.noWidgets*Hint`,
`warehouse.slowMoversDefinition`) render into page templates, never a toast, and nothing passes `icon`. Untrusted
data does reach toasts: tenant names and module labels (`sse-client.ts`), notification titles
(`notification-store.ts`) and server error text. So text by default breaks nothing, and **no HTML opt-in is added**:
nothing would use it (the confirm dialog's `message-html` stays the precedent if one is ever needed).

1. `b-toast.ts`: escape `message` and `icon` with `escapeHtml` in `render()`. `icon` becomes a text glyph (emoji /
   character), which is how its docs describe it.
2. Playground `toast-escape-smoke.ts`: `<img onerror>`, `<script>`, `<b>` in message and icon through `show` and
   `notify` — the text is visible, no element is parsed, the handler never runs. Register in `app.ts` / `verify.mjs`.
   Prove red by reverting the escape.
3. Docs: Components README / API toast section — message and icon are text.
