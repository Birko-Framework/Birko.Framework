---
id: TASK-943
parent: EPIC-001
feature: null
status: verify
priority: P1
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
related: [TASK-944]
findings: []
pr: null
github-issue: null
jira-key: null
---

# SECURITY: `BMarkdownEditor.renderMarkdown` let markdown run script

## Context

Reported by DraCode (its TASK-150, a read-only Spec tab; PR #14), 2026-11-21. `BMarkdownEditor.renderMarkdown`, the
editor's default renderer and a static method consumers call on their own, escaped only `& < >`:

- A link or image target and an image's alt text go into double-quoted attributes, so `[q](x" onmouseover="…)` and
  `![a" onerror="…](z.png)` closed the attribute and added a handler.
- Link and image targets were never checked for their scheme, so `[x](javascript:…)` ran on click (also `JaVaScRiPt:`,
  `java\tscript:`, `data:`).
- Found while fixing: a code fence's language went unescaped into `class="language-…"`, so
  ```` ```js" onmouseover="… ```` added a handler to the `<code>`.

Affected: the editor's own preview (`previewDiv.innerHTML = this._renderMarkdown(…)`) and any page that puts the output
into `innerHTML` from text it does not trust. Symbio's own renderer (`shared/markdown.ts`) closed the same two holes at
its TASK-277; DraCode wraps Birko's renderer in its own DOM sanitiser.

## Change (Birko.Web v0.4.20)

- Escape with `escapeHtml` from birko-web-core (quotes included), not `& < >` only.
- A link or image target is kept only if `safeHref` accepts it (http(s), mailto, tel, relative); a refused one stays as
  its source text, so the author sees it was refused.
- A fence language is kept only if it is a plain name (`[\w+#.-]+`).
- No new sanitiser API: the renderer emits only its own tags, so escaping first and checking targets closes it — the
  shape Symbio's TASK-277 took. DraCode's DOM sanitiser stays theirs.
- Docs: Components README (rendering untrusted text), CHANGELOG.

## Evidence

Playground backport-smoke, parsed with `DOMParser` (inert): 11 hostile inputs (DraCode's list plus the alt, tab, mixed
case, single-quote, fence-language and `data:` forms) each give no script, no `on*` attribute and no disallowed scheme;
a refused link is kept as text; 7 safe targets (https, relative, fragment, mailto, query with `&`, two images) are kept.
On Birko.Web main 9 of the hostile/refused checks fail; with the change all pass. Components `tsc` clean. Verify runs:
see TASK-944 (same branch).

## Acceptance criteria

- [x] No markdown input produces a script, an event-handler attribute or a link/image with a disallowed scheme.
- [x] Safe links and images render as before; a refused one is visible as text.

## Human test plan

1. In a page with a `b-markdown-editor` (the Playground gallery, or Symbio), type `[x](javascript:alert(1))` and
   `[q](x" onmouseover="alert(1))` into the source. The preview shows the first as text, and hovering the second shows
   no alert.
2. `[docs](https://example.com)` and `![p](https://example.com/p.png)` still render as a working link and an image.
