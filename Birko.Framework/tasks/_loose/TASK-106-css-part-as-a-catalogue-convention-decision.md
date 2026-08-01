---
id: TASK-106
# parent deliberately null: this is a decision ticket living in _loose/. Declaring a parent while
# sitting here makes it render in two places on the dashboard. Related to EPIC-016 in substance;
# see the body. Same convention as TASK-059 and TASK-127.
parent: null
feature: null
status: todo
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

Substantively it belongs to **EPIC-016** (framework backports from Reps), but `parent` is left null on
purpose, so the dashboard does not render it in two places. Same convention as [[TASK-059]] and
[[TASK-127]]. Move it under the epic directory if that is ever reversed.

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

**That snippet is the theory; it does not work as written.** A `b-card` on a real page sits inside the
page's own shadow root, two boundaries from the consumer's stylesheet, and `::part` pierces one — measured
below under *"The mechanism does not reach, in this architecture"*. The complaint is still real and the
motivation still stands; the mechanism just costs more than the snippet suggests.

## The state today: one component, not a convention

`part=` appears in **exactly one** place in the whole catalogue:

| File | Part |
|---|---|
| `Birko.Web.Components/src/nav/b-sidebar.ts` | `part="brand"` |

One usage is not a convention; it is a one-off that happens to compile. So the honest position is that the
catalogue **has not decided**, and the decision should not be made as a side effect of a card tweak — which
is why TASK-105 deliberately added no parts to `b-card`.

## The mechanism does not reach, in this architecture — measured 2026-07-30

Added after filing, and it is the most load-bearing fact in this document: **`::part` cannot reach a
catalogue component as Birko apps are actually assembled.**

`::part` pierces exactly **one** shadow boundary. But every Shell page is *itself* a shadow-DOM
component — `BasePage extends BaseComponent`, and `BaseComponent` calls `attachShadow()` in its
constructor — and pages render catalogue components **inside** that shadow root (`base-crud-page`,
`base-list-page` and `base-split-page` all render `<b-card>`). So a consumer's stylesheet sits two
boundaries away from the component it wants to style, not one.

Demonstrated in the playground against the catalogue's only existing part, with a custom property as
the control:

| Written in the document stylesheet | Reaches? |
|---|---|
| `b-sidebar::part(brand)` on a `b-sidebar` in the **document** (one boundary) | **yes** — `outline-style: dotted` applied |
| `b-sidebar::part(brand)` on a `b-sidebar` inside a wrapper's **shadow root** (two boundaries — the Shell page shape) | **no** — `outline-style: none` |
| `--b-sidebar-width: 12345px` read from inside that same doubly-nested shadow root | **yes** — `12345px` |

Reproduce by defining a wrapper element that `attachShadow()`s and renders `<b-sidebar>`, adding a
`::part` rule plus a custom property to `document.head`, and reading `getComputedStyle` off the
`.brand[part="brand"]` node in each case.

Two consequences:

1. **Adopting `::part` is not one decision, it is a forwarding convention.** Making it work needs
   `exportparts` on the component *at every intermediate layer* — the page must re-export the card's
   parts, and anything wrapping the page must re-export those. There are currently **zero**
   `exportparts` attributes anywhere in the codebase. Each forwarding declaration is itself permanent
   API with the same rename-breaks-silently property as the part it forwards, so the cost is not one
   name per part, it is one name per part *per layer*.
2. **The mechanism the catalogue already uses is the one that fits its composition model.** Custom
   properties inherit through shadow boundaries natively — that is *why* `--b-card-header-bg`,
   `--b-table-row-height`, `--b-card-shadow` and `--b-button-padding-y` all work without anyone
   thinking about nesting. `::part` fights the architecture; tokens go with it.

This does not settle the question by itself — a narrow adoption plus an `exportparts` convention is
still a coherent answer — but it moves the cost from "one selector name you can't rename" to "a
forwarding chain through every composition layer", and it should be weighed as that.

## The trade-off

> Read this section against the reachability finding above — several of the "for" arguments assume a
> consumer stylesheet can actually select the part, which in this architecture it cannot without a
> forwarding chain.

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
5. **The `exportparts` forwarding convention, if the answer is yes** — see the reachability finding. Which
   layers forward (does `BasePage` re-export its children's parts automatically, or per page?), what a
   forwarded part is named, and how a consumer discovers which parts survive the chain. Without this,
   "we support `::part`" is true of the component and false of every real page.

## Not in scope

- Adding parts to `b-card` — explicitly deferred by [[TASK-105]].
- Retrofitting parts to any component before the decision exists.
