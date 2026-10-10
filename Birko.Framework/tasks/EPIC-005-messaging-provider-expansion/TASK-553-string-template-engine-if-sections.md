---
id: TASK-553
parent: EPIC-005
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# StringTemplateEngine: optional `{{#if}}` sections

## Context

`StringTemplateEngine` only replaces placeholders. A value that is sometimes absent therefore renders as an orphaned
label: "Tracking number: " with nothing after it. Symbio hit this on its order e-mails (Symbio TASK-903).
- A guest order must carry a tracking link or code that an account order must not.
- `OrderShipped.TrackingNumber`, `DeliveryDispatched.DriverName/DriverPhone/City` and `PaymentReceived.PaymentMethod`
  are all optional and already print blank lines in tenant templates.

## Acceptance criteria

- [x] `{{#if Path}}…{{/if}}` keeps its body only when the value is present, and placeholders inside the body render.
  - [x] "Present" means not `null`, not an empty or whitespace-only string, and not `false`. `0` is present.
- [x] A missing property in a condition throws, like a missing placeholder does.
- [x] An unclosed, stray, nested or unknown `{{#…}}` / `{{/…}}` marker throws `TemplateRenderException`. The marker
      never reaches a recipient.
- [x] Tests in `tests/Birko.Messaging.Tests/Templates/StringTemplateEngineTests.cs`: 24/24 pass.
      Mutation-checked: treating every string as present fails 2 tests.
- [x] README and CLAUDE.md of Birko.Messaging document the syntax.

## Out of scope

- An `else` branch, nesting, loops. Add them when a template needs one.

## Note for consumers

A template that already contained a literal `{{#` or `{{/` used to render it verbatim. It now throws. No Symbio
template does.
