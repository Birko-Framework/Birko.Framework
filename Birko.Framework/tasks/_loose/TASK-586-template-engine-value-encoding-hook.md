---
id: TASK-586
parent: null
feature: null
status: todo
priority: P3
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `ITemplateEngine` has no way to encode a merged value for the template's target format

## Context

Filed 2026-10-13 from Symbio TASK-917, at its request. Symbio merges customer data (a name, an order note) into a
**Markdown** template with `StringTemplateEngine` (`Birko.Messaging/Templates/StringTemplateEngine.cs`), then renders the
Markdown to the mail's HTML. `{{Property.Path}}` substitutes the value verbatim. A merged value can therefore carry
Markdown formatting (`**`, `[text](url)`, a heading `#` at a line start) or an http(s) link, and the rendered mail shows
it as formatting or as a link the business never wrote. Symbio already neutralises raw HTML and unsafe link schemes on
its side; what stays open is Markdown syntax arriving through a merged value.

Neither `ITemplateEngine.RenderAsync(template, model)` nor `StringTemplateEngine` offers a hook to encode a value as it
is merged.

**The constraint that shapes the design:** escaping must not be global. The order tracking link is itself a merged
value, and Symbio relies on the renderer autolinking it as a bare URL. A Markdown escaper that escapes URL characters
(`_`, `.`, `:`) would break that link. So encoding has to be chosen **per call or per placeholder**, not per engine.

## Acceptance criteria

- [ ] A decision recorded on the shape. Candidates:
  - a per-call encoder, `RenderAsync(template, model, IValueEncoder? encoder, ct)`, with a per-placeholder opt-out
    (e.g. `{{{Raw.Path}}}`, Mustache's triple braces);
  - a per-placeholder opt-in (e.g. `{{Note|md}}`);
  - a per-property attribute on the model.
  Record why the others were not chosen, and which one keeps the tracking-link case working.
- [ ] Opt-in: with no encoder chosen, output is byte-identical to today (existing templates and tests unchanged).
- [ ] A Markdown escaper ships, covering at least `\ * _ [ ] ( ) # + - ! ` > |` where they would be interpreted, with
      tests that render the escaped text through a Markdown renderer and find no formatting in it.
- [ ] A test shows a bare URL merged without the encoder still autolinks while a neighbouring encoded value does not
      gain formatting, in one template.
- [ ] `IMessageTemplate` rendering gets the same choice, and the `Birko.Messaging.Razor` engine documents whether it
      needs it (Razor encodes HTML by default).

## Out of scope

- HTML escaping of merged values in Symbio: already done there.
- Switching Symbio to the encoder: a Symbio task once this ships (cross-reference Symbio TASK-917).

## Human test plan

N/A — the encoder's effect is asserted by rendering through a Markdown renderer in the tests.
