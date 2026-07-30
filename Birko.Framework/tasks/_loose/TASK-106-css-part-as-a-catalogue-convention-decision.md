---
id: TASK-106
parent: EPIC-016
feature: null
status: todo  # todo | in-progress | review | blocked | done | cancelled
priority: P3
assignee: human
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Decide whether `::part` is a catalogue convention or stays a one-off

**This is a decision, not an implementation.** Filed in `_loose` alongside [[TASK-059]] because it is a
framework-wide API-surface convention spanning the whole `b-*` catalogue — not work on any one component,
and larger than the story that surfaced it ([[STORY-052]], which fixes gaps *inside* components).

## Context

Surfaced by [[TASK-105]]: adopting `b-card` in Reps, every stacked card needed a light-DOM wrapper purely
to own a flex column, because `b-card`'s own flex container is inside its shadow root and slotted children
land in a plain padded body. TASK-105 rejected a `layout` / `gap` attribute — a card is chrome, and how its
contents stack is the contents' business.

But that rejection leaves the underlying complaint standing, and it is not about cards. The general form is
**"I cannot style into a component's shadow root"**, and the platform's answer to it is `::part`. Had
`b-card` exposed `part="body"`, the consumer could have written the column themselves against the real
container and no wrapper and no attribute would have been needed:

```css
b-card::part(body) { display: flex; flex-direction: column; gap: var(--b-space-md); }
```

The same shape recurs everywhere a consumer wants to reach one interior node — and the catalogue's current
answer is to add another `--b-*` custom property or another attribute each time.

## The state today: one component, not a convention

`part=` appears in **exactly one** place in the whole catalogue:

| File | Part |
|---|---|
| `Birko.Web.Components/src/nav/b-sidebar.ts` | `part="brand"` |

One usage is not a convention; it is a one-off that happens to compile. So the honest position is that the
catalogue **has not decided**, and the decision should not be made as a side effect of a card tweak — which
is why TASK-105 deliberately added no parts to `b-card`.

## The trade-off

**For committing to `::part`:**

- It is the standard, purpose-built mechanism. Custom properties can only carry *values*; a part can be
  given any declaration, which is exactly what the card-stack case needs (`display`, `flex-direction`,
  `gap` — three properties that would otherwise be three tokens or an attribute).
- It stops attribute/token sprawl. Today every new styling need costs a new `--b-x-y` or a new attribute,
  each of which is *also* permanent API, and each of which the component must interpret.
- It keeps the component's own job small — chrome stays chrome, and the consumer styles its own layout
  without the component growing opinions about it.

**Against:**

- **Every exposed part is API you cannot rename later.** A custom property can be aliased and deprecated
  quietly; a part is a selector consumers write in their own stylesheets, and renaming `part="body"`
  breaks them silently at *runtime* — no build error, just an unstyled card.
- It publishes internal structure. Once `part="body"` exists, restructuring the shadow DOM (an extra
  wrapper, a different element) becomes a breaking change, and the freedom to refactor a component's
  interior is a large part of why the interior is hidden.
- Half-adopting is the worst outcome: consumers cannot predict which components can be reached into, so
  they try `::part` first, find nothing, and fall back to asking for an attribute anyway.

## What a decision needs to settle

1. **Yes / no / narrow.** Commit catalogue-wide, decline and keep tokens+attributes as the only contract,
   or allow parts on a named short-list of container-ish components (card body, modal body, table wrapper).
2. **A naming + stability rule if yes** — which nodes are eligible (containers, not leaves?), a naming
   convention, and where parts get recorded so they are visibly API (`API.md` per component, presumably
   alongside the CSS custom properties already listed there).
3. **What happens to `b-sidebar`'s `part="brand"`** — grandfathered as the first instance of the new
   convention, renamed to fit it, or removed if the answer is "no".
4. **Whether a stack/cluster primitive is the better answer to the case that raised this**, since the
   card-stack need also appears in three *non-card* contexts in Reps (a transparent hero, settings blocks,
   list rows). If the catalogue grows a layout primitive, the `::part` question loses its most concrete
   motivating example and becomes a purely architectural call.

## Not in scope

- Adding parts to `b-card` — explicitly deferred by [[TASK-105]].
- Retrofitting parts to any component before the decision exists.
