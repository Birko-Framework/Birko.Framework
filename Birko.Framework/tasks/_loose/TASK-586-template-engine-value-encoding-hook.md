---
id: TASK-586
parent: null
feature: null
status: in-progress
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

## Decision (2026-10-13, user)

**A per-call encoder with raw paths the calling code declares.** The template syntax is unchanged.

```csharp
var md = new TemplateRenderOptions { Encoder = new MarkdownValueEncoder(), RawPaths = new HashSet<string> { "TrackingUrl" } };
var body = await engine.RenderAsync(tpl.Body, model, md, ct);
```

- **It fails closed:** with an encoder set, every placeholder is encoded except the `RawPaths`, so a new customer field
  is escaped without anyone remembering to. With no encoder, the output is byte-identical to today.
- **Trust lives where it is known:** the code that built the tracking URL declares it raw, not the template author.
- **Rejected:**
  - *per-placeholder `{{X|md}}`*: fails open (every tenant-authored template must mark every customer field); a
    marker copied into the plain-text subject shows backslashes;
  - *model attribute*: fails open; one treatment per property cannot serve a plain-text subject and a Markdown body
    rendered from the same model; domain event records passed as models would need wrapper types;
  - *raw opt-out in the template `{{{X}}}`*: fails closed, but every existing tracking link must be rewritten, and
    any template author can write `{{{Note}}}` and switch the protection off.

**Measured** in Symbio's pipeline (Markdig 1.4.0, `DisableHtml`, autolinks with `AllowDomainWithoutPeriod`, soft line
break as hard):
- **Raw today:** a name `# Ján
- Novák
1) Third
> quote` becomes a list, a numbered list and a blockquote that
  swallows the rest of the mail; a note becomes bold text, links and a remote image.
- **Escaped (every ASCII punctuation character backslash-escaped), with the tracking link raw:** all literal, and the
  tracking link still autolinks.
- **Tracking link escaped too:** plain text, not a link, which is why escaping cannot be global.

**Caveat for Symbio:** it sends the filled Markdown as `PlainTextBody`, so escaped values would show their backslashes
in the text part. It can render twice (with the encoder for HTML, without it for text) or derive the text from the
rendered Markdown. That is Symbio's choice.

## Implementation plan

1. `Templates/TemplateRenderOptions.cs` (`Encoder`, `RawPaths`), `ITemplateValueEncoder.cs`, `MarkdownValueEncoder.cs`
   (backslash-escapes every ASCII punctuation and symbol character; CommonMark 2.4 allows it for all of them); register
   them in `Birko.Messaging.projitems`.
2. `ITemplateEngine` gains `RenderAsync(string, object, TemplateRenderOptions, ct)` and the `IMessageTemplate` twin,
   as default interface methods: no encoder → delegate to today's overload; an encoder → `NotSupportedException`. So
   `RazorTemplateEngine` refuses an encoder rather than ignoring it (Razor HTML-encodes on its own).
3. `StringTemplateEngine` implements both: encode each substituted value unless its exact path is in `RawPaths`;
   `{{#if}}` presence is decided on the raw value; null and empty stay empty.
4. Tests (Markdig 1.4.0 as a test-only package): no encoder = today; values encoded and template text not; raw paths;
   sections unaffected; every printable ASCII character round-trips through Markdig as literal text; the Symbio
   pipeline case (escaped name and note, raw tracking link autolinks) in one template; the default method refuses an
   encoder and delegates without one; the `IMessageTemplate` overload.
5. Docs: Messaging and Razor CLAUDE.md/README, the tests' docs, Recent Updates (roll if over budget).
