# Brief

## Origin

Adopted into the project layer on 2026-10-20 by `/adopt-project`. **No original brief exists**: the framework predates
the layer, and its opening request was never recorded verbatim. Its history lives in [README.md](../README.md),
[CHANGELOG.md](../CHANGELOG.md), the decisions under [docs/features/](features/) and [docs/adr/](adr/), and the commit
log.

## Recovered from session history

Backfilled 2026-10-21 from stored Claude Code sessions, which reach back only to 2026-09-20; earlier sessions are no
longer stored, so this is partial.

### 2026-09-30 — became TASK-508 (EPIC-018 / FEATURE-018)

> [pasted: a framework task brief, "Framework task: add a multipart upload method to Birko.Web.Core's ApiClient." —
> `postForm<T>(path, form, opts?)` with a per-call timeout, not queueable, sharing the 401 refresh; written for Symbio
> TASK-839's photo upload]

### 2026-09-30 — became TASK-509

> i was thinking if birko.models shoudl not be outside the frammework

> file is as a task for now

### 2026-10-13 — became EPIC-020 (Birko.Security.Grants)

> a qurstion to decide some of my projects like  reps,  presntation maybe other will  hang up on some login gating and acces so som part would not be  avaliabele for public (createm update delete operations etc) shoudl i buidl myself my oauth server for thise things?

> that why it was a question   ok whith for example an external login how can igrand access who is admin who is some partial owner?

> create a task for Birko.Security.Grants

### 2026-10-14 — became TASK-592 (EPIC-001 / FEATURE-001)

> 1-3 wor a question can the icons be easily changed?

> file a task for option 1, the icon slot

### 2026-10-16 — became TASK-652 (EPIC-015)

> [pasted: "Add a HideNullMembers option to Birko.Xaml.Avalonia's ObjectTree" — a `StyledProperty<bool>`, default
> false, that skips null named members (properties, dictionary and JsonObject entries) but never array items; written
> for DesktopFinstatApiTester]

### 2026-10-19 — became EPIC-021 (entity localization for catalogues)

Asked in a Symbio session (its STORY-081: English catalogue and per-language slugs for the first storefront).

> woudl liek to do it the framework way also add the methods we need to work with translations efectively

> yes, file it like that

## Amendments

<!-- Append-only. Each requirement-changing request made after adoption, verbatim, dated, naming the feature it became. -->

<!-- Backfilled 2026-10-21 from stored Claude Code session history. -->

### 2026-10-20 — became EPIC-022

> firsly  WorkoutTracker. Its 20 string HasPrecision  did you checked other projects like symbio or dracode if thy are safe? secondly we added some  improvement to localization for sql providers how about other porviders?

### 2026-10-22 — became FEATURE-019 (Office-like `b-ribbon`)

> can you look at the extension look hot the ribbon bar looks the  goups are not the same height for example and what is missing from office ribbon behaviour?

> gve me the list to confirm one by one

Answered one item at a time in the session's picker: the full layout fix (equal-height groups, aligned rows, labels at
the bottom), and every one of the eight Office behaviours (with the Quick Access Toolbar and rich tooltips, and with
toggle buttons, galleries and inputs). Web first, with the Avalonia ribbon's parity filed for later.

### 2026-10-26 — became FEATURE-020 (Birko.Xaml comparison engine, diff viewer, tag input, accordion look) and TASK-768

Asked in the ClientApi.CSharp session (the FinStat desktop tester) and relayed here by that session; the user's own words
stay in that session's history. The relay, as received:

> new Birko.Xaml parity components needed by the FinStat desktop tester — please run them through your feature lifecycle.
> The user (František) asked for this in my session and chose "full lifecycle" for the framework part (feature + tasks +
> tests in Birko.Xaml.Avalonia.Tests + Gallery demo + CHANGELOG)

It named the diff engine (`diffJson`, `xmlToTree` / `xmlToLines`), the `b-json-diff` viewer, `b-tag-input`, `b-accordion`
(an Expander look on Avalonia), and the `FieldType.Password` plain-text bug, which became TASK-768. Grill answers from the
user in this session: engine in Birko.Xaml.Core; shared test vectors, owned by Birko.Web; both viewer views as two tasks;
strict tag-input parity; Expander theme only.

### 2026-10-29 — became TASK-782 (Slovak and Czech translations shipped with Birko.Web.Components)

Asked while testing TASK-781's Playground on the published page:

> 5 do we have the tokens localized to sk/cz or it uses en fallback? i cannot see a component that shows sk or cz texts

Answered: English fallback — Birko.Web.Components ships only `locales/en.json` (200 strings); the Playground's own
partial cs/sk bundles (21 strings each) are almost all screen-reader labels. Offered the library-level fix, and asked
whether to start it or file it:

> fiel as a task

### 2026-10-29 — became TASK-783 (Birko.Xaml gallery: presentable, built by CI, linked)

> can we update the desktop app that it looks better for birko.xaml. some ci for buildin git on commit and add link into wiki and the  https://birko-framework.github.io/ page

Picked from previews: a ribbon layout like the web Playground; CI builds for Windows x64, Linux x64 and macOS (Apple
Silicon), plus screenshots.
