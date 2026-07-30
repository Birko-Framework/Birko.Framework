# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `../Birko.{ProjectName}/CLAUDE.md` with project-specific details.

## Architecture

### Store Hierarchy (Template Method Pattern)
```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

Stores use lazy-init: CRUD methods auto-call `Init()`/`InitAsync()` before first use (via `EnsureInitialized`/`EnsureInitializedAsync` with double-checked locking). Concrete stores override `*Core` methods (e.g., `CreateCoreAsync` instead of `CreateAsync`). Public methods are `virtual` on the base class.

### SQL Stores
```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
  -> Settings (Location, Name)
    -> PasswordSettings (+Password)
      -> RemoteSettings (+UserName, +Port, +UseSecure)
        -> SqlSettings (+CommandTimeout, +ConnectionTimeout, abstract GetConnectionString)
          -> MSSqlSettings (+MultipleActiveResultSets, +TrustServerCertificate)
          -> MySqlSettings (+BulkInsertBatchSize)
          -> PostgreSqlSettings (+UseBinaryImport)
          -> TimescaleDBSettings (+TimeColumn, +ChunkTimeInterval)
    -> SqLiteSettings (+CommandTimeout, Path, GetConnectionString) — extends PasswordSettings
    -> CosmosDB Settings (+PartitionKeyPath, +RequestTimeout, +AllowBulkExecution, GetCosmosClientOptions)
    -> RavenDB Settings (+RequestTimeout, CreateDocumentStore)
    -> MongoDB Settings (+AuthDatabase, +ReplicaSet, GetConnectionString) — already existed
    -> RedisSettings (+Database, +KeyPrefix, GetConnectionString) — already existed
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
    -> Birko.Data.Stores (store interfaces, imports Configuration)
      -> Birko.Data.Repositories

Birko.Models.Contracts (zero deps: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
  -> Birko.Models (AbstractPercentage, AbstractTree, ValueData + Value Objects: Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
    -> Birko.Models.Inventory / .Pricing / .Customers / .Users / .Product / .Category / .SEO (clean, no SQL attrs)
    -> Birko.Models.SQL (ModelMap<T>, IModelMapping<T>, ModelMapRegistry — fluent SQL mapping framework only, no canonical mappings)
      -> Birko.Models.Users.SQL / .Customers.SQL / .Inventory.SQL / .Pricing.SQL / .Product.SQL
         (one optional sibling per domain — pre-built IModelMapping<T> for User/Role/Tenant, Address/Customer,
          StockItem/StorageLocation/InventoryDocumentLine, Currency/Tax/PriceGroup, MeasureUnit/UnitConversion/ProductPartnerCode)

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)

Birko.Data.Patterns + Birko.Data.Tenant + Birko.Time.Abstractions
  -> Birko.Data.Composition (StoreWrapperBuilder — runtime decorator chains)

Birko.Data.Core
  -> Birko.Data.Tagging (ITaggable, Tag, EntityTag, ITagService, TagServiceBase)

Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, ICollectionBuilder, IIndexBuilder, IIndexManager, IndexDefinition, ISoftDeletable, IAuditable, ISpecification, IUnitOfWork, PagedResult)
  -> Birko.Data.Migrations (IMigrationContext, IDataMigrator, IContextualMigration, IMigration, IMigrationRunner, IMigrationStore)
    -> Birko.Data.Migrations.SQL (SqlMigrationContext — reuses AbstractConnector), .MongoDB, .ElasticSearch, .RavenDB, .CosmosDB, .InfluxDB, .TimescaleDB

Birko.AI.Contracts (zero deps: ILlmProvider, Message, ContentBlock, Tool, AgentOptions, LlmProviderFactory)
  -> Birko.AI (LlmProviderBase, Agent base, AgentFactory (registration-based), default tools)
    -> Birko.AI.Providers (Claude, OpenAI, Gemini, Ollama, AzureOpenAI, etc. + ProviderRegistration)
    -> Birko.AI.Agents (CodingAgent, language agents, media agents + AgentRegistration)
    -> Birko.AI.Orchestration (ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer)
  -> Birko.AI.Resilience (ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider)

Birko.Communication.OAuth (IOAuthClient, OAuthClient, OAuthSettings)
  -> Birko.Communication.OAuth.Providers (GitHubOAuthProvider — pre-configured device flow)

Birko.Communication.GraphQL (IGraphQLClient, GraphQLClient, GraphQLSettings — queries, mutations, subscriptions over HttpClient + ClientWebSocket)

Birko.Communication.gRPC (GrpcSettings, GrpcChannelPool, GrpcClientFactory, GrpcAuthenticationInterceptor, GrpcException — client over Grpc.Net.Client)
  -> Birko.Communication.gRPC.Server (GrpcServerSettings, AddBirkoGrpc, GrpcServerAuthenticationInterceptor — server over Grpc.AspNetCore; mirrors REST / REST.Server split)

Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy, JobProcessor, JobScheduler)
  -> 8 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .Redis, .CosmosDB

Birko.Workflow (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
  -> 7 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .CosmosDB
```

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage
- **XML** store — reference for file-based storage with `System.Xml.Serialization` (note: no native `Dictionary` support — use wrapper types)
- **InMemory** store (`Birko.Data.InMemory`) — simplest possible store (thread-safe `ConcurrentDictionary`, no persistence); the canonical test double / prototyping backend

## Usage in Consumer Solutions

When using Birko.Framework projects in your solution, create **one or more aggregator library projects** that bundle the `Birko.*` shared projects you need (e.g. `FisData.Birko`, `Symbio.Birko`, or split by layer like `{Solution}.Birko.Core` + `{Solution}.Birko.Edge` + `{Solution}.Birko.Ai`). Your other projects reference the aggregator(s) instead of importing `.projitems` directly. Default to a single aggregator; split only when concrete pain shows up — bloated binaries, leaky transitive deps, or unused-heavy-dependency pull-ins (camera, AI, hardware). This avoids compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

Use `$(BirkoSrc)` (resolved from a root `Directory.Build.props`) for all `Import Project="…\Birko.X\Birko.X.projitems"` paths instead of hard-coded absolutes. The property reads `/p:BirkoSrc=…` first, then the `BIRKO_SRC` environment variable, then defaults to the `Birko\Framework` checkout relative to the consumer repo (the recommended layout nests the framework under a `Birko\Framework` bucket with consumers under a sibling `Birko\Consumers`, so the default resolves `..\..\Framework`; flat-sibling checkouts would use `..`). TypeScript bundlers consuming `Birko.Web.*` sources resolve a **separate `Birko\Web` bucket** — the frontend libs (`Birko.Web.Core` / `.Components` / `.Shell`) live there, apart from the .NET `Birko\Framework`. Their `build.js` walks up to find `Birko\Web` (or honors `BIRKO_SRC`). So the two builds resolve sibling buckets: `Birko\Framework` for MSBuild, `Birko\Web` for esbuild. See [README — Usage in Consumer Solutions](README.md#usage-in-consumer-solutions) for the full pattern.

## Conventions
- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set, ES UpdateByQuery); use `Action<T>` for complex mutations
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance
- On a bulk store, `Read(filter)` returns the **collection** (`IEnumerable<T>`), not a single entity: the bulk `Read(filter, orderBy, limit, offset)` overload hides the inherited single-result `Read(filter)` from member lookup (C# only considers the most-derived type that declares the method name). Use `ReadFirst(filter)` / `ReadFirstAsync(filter)` (on `IBulkReadStore<T>` / `IAsyncBulkReadStore<T>`) for a single result, or cast to `IReadStore<T>` / `IAsyncReadStore<T>`
- Concrete stores override `protected *Core` methods (e.g., `CreateCoreAsync`, `ReadCore`), **NOT** the public CRUD methods. The base class handles lazy-init in the public wrapper
- Use protected setters for properties that derived classes need to modify
- `RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline

## Task tracking — this repo is the polyrepo family's aggregator

The Birko family is a **polyrepo** (every `Birko.*` sub-project is its own git repo); this
repo is its **aggregator** (the `.slnx`, the shared CLAUDE docs — and the cross-cutting plan).
This is the aggregator override the generic `tasks` skill's shape detection defers to:

- **Cross-cutting epics** (work spanning several `Birko.*` sub-projects) live in **this repo's
  `tasks/`**, with the affected sub-projects listed in the EPIC's `affects:` frontmatter
  (e.g. `affects: [Birko.AI, Birko.Data.Core]`).
- **Single-sub-project work** stays in that sub-repo's own `tasks/` (the default
  walk-up-to-`.git` rule already lands there) — don't track it here.
- Cross-cutting `docs/features/` and `docs/specs/` follow the same split: family-wide at this
  aggregator, per-project in each sub-repo (a cross-cutting story regens specs per affected
  project, driven by `affects:`).

## Skills shipped by this repo

`.claude/skills/` is the home of the Birko-specific skills. They **build on top of the generic
project-lifecycle-skills set** (never the reverse — the generic skills know only a "stack
scaffolder" hook, not Birko). Project-local ones (new-birko-subproject, new-store-backend,
verify-birko-conventions, the roll-changelog shadow) auto-load only inside this repo; the
consumer-facing ones (birko-new-project, new-birko-web-page, new-birko-web-component,
design-agent) are shared user-level via [install-skills.ps1](install-skills.ps1) (junctions —
edit here, live immediately).

## Code Style
- **Guard clauses:** Use early returns instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... }`.
- **No nullable warnings:** All new code must compile without CS8600–CS8605, CS8618, CS8625. Use proper null checks, `!` only when provably safe, or `?` annotations.

## Testing
- All test projects use **xUnit + FluentAssertions**
- Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`
- Test both success and failure cases; include edge cases and boundary conditions
- Each test project has its own `CLAUDE.md` describing scope and conventions
- See [CLAUDE-maintenance.md](CLAUDE-maintenance.md) for test requirements on new projects and health check patterns

## Recent Updates

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; when this section grows past ~5–8 entries, roll the oldest into CHANGELOG.md (the project-local `/roll-changelog` skill does this). Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.

### `b-card`: two additive options, one refusal, one deferred framework-wide decision (2026-07-30)

Second gap under STORY-052, from Reps adopting `b-card` (framework `TASK-105`). Shipped: a `padding="md"`
rung (the scale skipped it although `--b-space-md` exists and the card's own header pads with it) and
`--b-card-shadow` defaulting to `var(--b-shadow-sm)`, so a consumer can flatten one card without
neutralising `--b-shadow-sm` for everything else in scope. Both additive; Reps has opted into neither and
measures byte-identical.

The parts worth carrying past the component:

- **A gap can be correctly answered with "no", and the "no" is the deliverable.** A `layout`/`gap`
  attribute on `b-card` was rejected: a card is chrome, how its contents stack is the contents' business,
  and the need is not card-specific — the same stack is wanted in three *non-card* Reps surfaces, so a
  card-scoped answer helps none of them. The bar this repo's backports have met is *things a consumer got
  wrong or would rediscover the hard way*; a flex column is neither. Recorded with its reasoning in the
  task so it is not re-proposed as if unexplored.
- **Don't settle a framework-wide question as a side effect of a component tweak.** The refusal leaves a
  real complaint standing — "I cannot style into a shadow root" — whose platform answer is `::part`, which
  today appears in exactly one component (`b-sidebar`). Whether the catalogue commits to it is an API
  decision (an exposed part is a selector consumers write; renaming it breaks them silently at runtime),
  so it is filed as **TASK-106** in `tasks/_loose/` next to TASK-059, and no parts were added.
- **Assert against the live token, not a px literal.** The consumer check compared card padding to a probe
  carrying `var(--b-space-lg)`. Just as well: it computes to **14px**, not 16px, because the Birko reset
  sets `html { font-size: var(--b-text-base) }` = 14px, so every `rem` in the token scale resolves against
  a 14px root. A literal would have failed for the wrong reason — and would have kept passing if the scale
  were ever rescaled.
- **A back-compat check is only worth something if the fix-dependent ones can fail.** Removing both changes
  and re-running took the suite to 126/131: exactly the five fix-dependent checks broke, and the five
  back-compat ones stayed green. Without that split they would just be restatements of the fix.

### `b-chart` small-chart axis, and a new home for gaps consumers find in the catalogue (2026-07-30)

Reps reported that `b-chart` reads busy at 90–150px — the sizes its Progress surface is entirely made of.
Fixed in the component (`Birko.Web.Components`, framework `TASK-104`; per-component detail in that repo's
CLAUDE.md): the y-tick count now follows the plot height (`tickIntervalsForHeight`, capped at 5 so the default
300px chart is unchanged) or an explicit `yAxis.ticks`; tick values snap to 1/2/2.5/5×10ⁿ (`niceScale`);
`showLatestValue` is a top-level option instead of something you could only reach by opting into `realTime`.

Three things worth carrying past this component:

- **A "label overlaps X" report is a z-order question before it is a coordinates question.** The write-up
  named the label's x coordinate, a halo shipped on that reading, and the screenshot of the real surface then
  showed a bar painted straight *through* the text — the label was emitted with its line, before the bars.
  Placement was never wrong.
- **Only the real surface found it.** The playground proved the component correct and the smoke was green;
  the defect needed a bar tall enough to reach the threshold, in the consumer's own data. Finish verification
  in the surface that reported the problem, not in the harness.
- **Decouple what varies for different reasons.** Deriving the *band* from the height-derived tick count made
  a 90px chart round an 11 357 peak up to 20 000 and draw its bars at 57% of an empty plot. The band is now
  rounded at a fixed density and only the labels thin out, so the same data lands on the same band at every
  size — same shape as the ribbon lesson that the scaling decision must not read the applied layout.

Tracking: **STORY-052** under EPIC-016 is the new home for gaps a consumer hits while *adopting* an existing
component — distinct from STORY-037/038, which are closed ledgers of capabilities moved upstream. The two have
opposite acceptance tests: a backport is done when the consumer can delete its copy, an adoption gap is done
when no consumer needs a fork or a special case.

### `docs/specs/` exists: 25 capability specs harvested from code, and the 865 findings that fell out (2026-07-30)

`docs/specs/` now holds **25 cross-cutting capability specs** — 736 SHALL requirements and 2,426
Given/When/Then scenarios, generated from 648 source files at code HEAD `f3ac675`. **Spec bodies are
generated, not written**: `/specs regen` overwrites them, so a wrong statement is fixed by fixing the code or
the map, never by editing the `.md`. `docs/specs/.map.yml` (capability → source globs) is the only
human-owned file in that directory. Scope is deliberately **aggregator-only** — cross-cutting contracts
spanning several `Birko.*` sub-repos, with globs reaching out via `../Birko.X/...`; per-sub-repo
`docs/specs/` trees remain follow-up work, and 64 single-repo projects are named in the map's explicit
out-of-scope block so their absence is a decision rather than a silent gap.

**Areas include their backend implementors, not just the interface**, wherever the contract's whole point is
cross-backend conformance. That is a direct consequence of this family's bug history: the empty-`IN` and
dropped-ElasticSearch-clause defects were per-provider *divergence*, which a contract-only spec cannot see.
The uncapped sweep vindicated it — `RefreshAsync` validates its view name on ElasticSearch and silently
no-ops on the other four backends; MySQL inherits a `CREATE INDEX IF NOT EXISTS` and a table-less
`DROP INDEX` that it does not accept.

**Specs record actuality, defects included — which is what makes them find things.** Harvesting turned up
**865 findings (57 high)**, tracked as `tasks/EPIC-014-code-review-remediation/STORY-051` with `SH-*` ids
(distinct provenance from the `CR-*` audit, so nothing renumbers). Confirmed by hand: `Pbkdf2PasswordHasher.Verify`
returns **`true` for any password** against a `PBKDF2-SHA512:600000::` column (empty string is valid Base64,
so the CR-M233 guard never fires and `FixedTimeEquals` compares two zero-length spans); a null or
silently-dropped filter renders **`DELETE FROM "T"`**; `long`/`double`/`float`/`short`/`byte[]` properties map
to **no column and never persist** (`decimal` is mapped, so money escapes); the tenant write guard compares a
**caller-settable** `item.TenantGuid` instead of the stored row; and ORDER BY keys are interpolated verbatim,
reachable via `OrderBy<T>.ByName(string)`. Note the ordering constraint this creates: **the specs currently
document these defects as shipped behaviour**, so any fix must be followed by a `/specs regen` of its area
with the spec diff reviewed — that diff is the fix's evidence.

Three rules worth keeping beyond this exercise:
- **A spec that silently picks a side is wrong.** `store-crud-contract` specced `Destroy()` with its
  destructive implementation meaning while the interface doc says "releases all resources" — but the
  *disagreement* was the defect, and recording only one side hid it. Where code and its own documentation
  contradict each other, spec both.
- **Verify before filing.** Of the 15 high findings checked by hand, 12 held exactly and **3 needed their
  scope corrected** — one named the wrong trigger entirely (the `IsNegated` claim: the unresolved-field
  branch returns *before* the negation, so only the non-string path degrades to match-all). Filing all 57
  unverified would have put three misleading tickets into EPIC-014.
- **Structured-output bounds silently shape results, in both directions.** A `maxItems: 8` on the
  finding array made 22 of 25 areas return exactly 8 — hiding roughly 90% of what was found, and looking like
  data rather than a ceiling. Later a 600-character per-item limit made one area fail its schema five times
  and be dropped entirely. A bound that is too small does not truncate a list, it loses the agent; make
  agents report exhaustion explicitly and treat a suspiciously round number as a cap, not a count.

### Ribbon overflow: the ribbon body scales, it does not scroll — delivered in both skins (2026-07-29)

A field report — on a narrow window the ribbon shows fewer commands with no way to reach the rest — turned
out to be true in **both** skins, failing differently. `Birko.Xaml.Avalonia`'s `Ribbon` clipped its tab strip
*and* its groups row with no `ScrollViewer` at all. `b-ribbon` had working tab-strip chevrons but its panel
was `overflow-x: auto` with `scrollbar-width: none` and no buttons — scrollable in theory, invisible in
practice, so an overflowing group was unreachable by mouse. Its tab arrows also only re-evaluated on `scroll`
and on re-render, so narrowing the window left the arrow hidden while the tabs overflowed.

The **standing design rule** this establishes, recorded here because it constrains all future ribbon work:
the ribbon *body* **resizes, it never scrolls.** Office degrades each group `Large → Medium → Small → Popup`
in an author-declared priority order (`scalingPriority`), collapsing a whole group to one chunk button with a
full-size flyout rather than moving commands offscreen. A scroll offset destroys the spatial memory the ribbon
exists to provide ("Cut is top-left of Clipboard") — the exact failure the ribbon was invented to fix over
Office 2003's toolbars. The `»` overflow chevron *is* an Office pattern, but it belongs to **toolbars**
(Fluent `CommandBar`/`OverflowSet`), not the ribbon body. **Ribbon tabs are the deliberate exception** and do
scroll, as in Office Web / Fluent.

**Delivered (TASK-097 → TASK-100).** Both skins now scale: `Large` → `Medium` → `Small` → `Popup` (the whole
group as one chunk button with a flyout), plus a **compact** chunk that drops the group name at the extreme.
The groups row has **no scroller in either skin**; only the tab strip scrolls. `RibbonScaling` in
`Birko.Xaml.Core` owns the policy and `b-ribbon`'s `ribbon-scaling.ts` mirrors it, with the playground smoke
asserting the *same numeric table* as the C# unit tests so the two cannot drift. Default look is `Medium`
(what both skins already rendered, so no consumer's ribbon changed height); `Large`/`Small` are opt-in.

Two model-level decisions worth knowing beyond the ribbon:
- **`MinSize` is a preference, not a guarantee** — breached least-important-first rather than letting the row
  overflow. Unreachable commands are worse than a group being less legible than its author wanted.
- **The scaling decision must never be a function of the applied layout**, or it oscillates at a boundary.
  Proven the hard way: while the groups row still had a scroller, the scroll chevrons' hysteresis fed back
  into the width being scaled against and the same window width resolved differently depending on drag
  direction. Removing the scroller fixed it with no other change.

**The accessibility round found more than the layout round did (2026-07-30), and every defect was in a
control whose behaviour was already correct and tested.** The XAML skin was, in effect, unusable without a
mouse: **no ribbon button had an accessible name** (Avalonia derives one from `Content` only when it is a
*string*, and a ribbon item's content is a panel — so a screen reader was handed `"Avalonia.Controls.StackPanel"`,
or the bare glyph at `Small`); there is **no focus visual on `Button` anywhere** in the skin, which faked two
"Tab is broken" reports before anyone suspected styling; `Rebuild()` **destroyed focus**, so activating a tab
by keyboard threw the user out of the ribbon; and **arrow navigation did not exist at all**, while `b-ribbon`
had it in both the tab strip and the panel. `b-ribbon` had every one of these right — hence the rule now in
`Birko.Xaml.Avalonia/CLAUDE.md`: **when porting a web component, port its ARIA discipline too.** The XAML
equivalents (`AutomationProperties.Name`, `AccessibilityView.Raw`, an automation peer) all exist, and none of
them are automatic. The missing focus visual is framework-wide and is tracked as **TASK-103**.

Two lessons that generalise past the ribbon:
- **A non-empty accessible name is not a criterion.** `Content.ToString()` satisfies it, so the first version
  of that test passed with the fix removed. Assert the name is a string the *user* would recognise.
- **Some criteria cannot be signed off by hand, and saying so beats a false tick.** "An open flyout closes
  when its group is promoted" was ticked with no implementation behind it, and the manual step could never
  have caught that: dragging a window edge is a click outside the flyout, and snapping or moving it between
  monitors dismisses popups too. Three causes, one observation. Only a headless resize isolates it.

Review found five defects the automated suites had missed, four of them one species — **state that did not
survive a re-render** (an imperative CSS class wiped by a synchronous morph; chevron visibility that changed
*layout*, letting a tab swallow the click; a tab-strip scroll offset discarded by `Rebuild()`; flyout wiring
applied only in `onUpdated` while the measure pass re-rendered). The fifth was subtler and is now a documented
Avalonia gotcha: **measuring an `IsVisible = false` control yields zero**, so a panel that hid its
alternatives before measuring them under-degraded and clipped its last group — while the tests, which asserted
the *decision* rather than whether the row *fits*, stayed green. Per-skin detail lives in the two sub-project
CLAUDE.mds. Remaining Avalonia parity gaps are tracked as TASK-101 (pinned / temporary-reveal) and TASK-102
(narrow fallback — `b-ribbon` has a hamburger, the XAML ribbon has never had one).

### Per-theme AXAML dictionaries — Avalonia themes are opt-in like the web ones (2026-07-29)

Themes were opt-in on the web (one CSS file each, linked + `registerThemes`) but all-or-nothing on
desktop: one 36 KB `Tokens.axaml` held all four `ThemeDictionaries`, and `BirkoTheme.axaml` pulled the
lot. `Birko.DesignTokens` now emits **one file per theme** — `Themes/Tokens.{Light,Dark,Neon,Finstat}.axaml`
plus a shared `Tokens.Brushes.axaml`, with `Tokens.axaml` kept as a back-compat aggregate merging all
five (existing consumers unaffected). New `BirkoTheme.Core.axaml` ships light+dark; extra themes are
merged per file. Core+extras is ~43% lighter than all-in (23 KB vs 41 KB).

Three Avalonia behaviours were spike-verified first and are now pinned by `ThemeCompositionTests`,
because the design rests on them: `ThemeDictionaries` entries **do** resolve from *merged*
dictionaries (so the split is possible at all); an omitted custom variant degrades to its
`InheritVariant` (so Neon/Finstat are safely omissible); **`ThemeVariant.Dark` has no
`InheritVariant`**, so a light-only app resolves *nothing* under OS dark mode — which is why core is
light+**dark**, not light alone.

Themes are **detected, not listed twice.** Each generated dictionary declares
`<x:String x:Key="BThemeId">` naming itself, and `AvaloniaThemeManager.DetectThemes(IResourceNode)`
reads it, so `Available` is derived from what was actually merged and the switcher can never offer a
theme whose tokens are missing. Presence probing cannot do this — an omitted variant inherits
silently and would answer anyway; only a value that names its dictionary distinguishes the cases.
This is the fix for the drift that bit the web side, where CSS-linking and `registerThemes` are two
lists nobody reconciles.

Also, while regenerating: **`tokens.json` was stale and `CssParityTests` had been red on main.**
Generated CSS had been hand-edited — `--b-color-danger-text` (a WCAG contrast token, all four
sheets), an AA-darkened finstat `--b-text-secondary`, `--b-modal-width-xxl`, `--b-modal-full-inset`,
`--b-drawer-width-xxl`, `--b-input-font-size` — so `generate` would have silently deleted all of it.
Recovered with `extract` (folds the live CSS back into the source and self-checks the round-trip);
CSS is byte-identical again and the AXAML now carries those tokens too. `verify` gained an **AXAML**
drift gate (it was CSS-only, so six generated dictionaries would have been unguarded). Three
`ShellChromeTests` were also converted `[Fact]`→`[AvaloniaFact]`: they built an `AvaloniaThemeManager`
with no `Application` and only passed when an earlier test happened to leave `Application.Current`
set.

Both gaps are now closed at the mechanism level rather than just fixed:
- **`AxamlParityTests`** gates the six generated dictionaries from the *suite* (each must equal what
  tokens.json regenerates, plus a check that `Themes/` holds exactly the generated set so a dropped
  dictionary can't linger). The CSS always had a suite gate; AXAML had only the `verify` CLI verb, and
  nothing runs a CLI verb by itself — which is precisely how the CSS drift went unnoticed. Verified to
  actually fail by perturbing a generated file.
- **Every one of the 32 Avalonia test classes now passes in isolation**, so order-dependence can no
  longer mask a broken test. Swept the whole suite for the same ambient-`Application` pattern; the
  five remaining plain `[Fact]`s are genuinely app-independent (pure VM/service logic).

Tests: `Birko.DesignTokens.Tests` 42 (was 18 green / 12 red), `Birko.Xaml.Avalonia.Tests` 144,
`Birko.Xaml.Core.Tests` 41.

### X-Tenant-Id must agree with the JWT tenant claim (2026-07-28)

`Birko.Security.AspNetCore` gained **`TenantHeaderClaimGuardMiddleware`**, wired via
`UseBirkoTenantHeaderGuard()`. `HeaderTenantResolver` parsed `X-Tenant-Id` with no comparison to the
`tenant_id` claim — and *cannot* compare, because `TenantMiddleware` runs before `UseAuthentication()`, so
`context.User` is unpopulated there. In a typical app the header and the claim feed **different** consumers
(repository tenant scoping follows the header, permission resolution follows the token), so a caller could
authenticate in their own tenant, send `X-Tenant-Id: {victim}`, keep their home-tenant permissions and point
every tenant-scoped read **and write** at another tenant. Hence a separate post-authentication step, placed
after `UseAuthentication()`/`UseAuthorization()` and before anything that scopes by tenant; a mismatch returns
403 `Tenant.HeaderClaimMismatch`. **Secure by default** — `BirkoSecurityOptions.RequireTenantHeaderMatchesClaim
= true`; an opt-*in* guard was rejected because a check nobody knows to enable protects nobody. Deliberate
pass-throughs: no header (the claim is then the only source; SSE cannot set headers), unauthenticated
(login/register/setup), wildcard `*` holders (cross-tenant reach is intentional), unparseable header (resolves
to no tenant anyway). `BirkoSecurityOptions` is now registered as a singleton so middleware can read it.
Docs: [docs/security.md](docs/security.md#tenant-headerclaim-guard), [docs/tenant.md](docs/tenant.md).

### Empty-set and enum filter translation fixed across SQL + ElasticSearch (2026-07-27)

Three defects in the same family — an operand the parser mis-read, and an empty collection with no explicit
case — each of which made a filter match the **wrong rows** rather than fail:
- **SQL, empty `IN`** — `InConditionStrategy` had no empty-set case and emitted `Col IN ()`. SQLite's grammar
  permits it (always-false), which hid the defect from the SQLite-backed suites; PostgreSQL and MSSQL reject
  it as a syntax error. Now renders set-faithful constants: empty `IN` → `1 = 0`, empty `NOT IN` → `1 = 1`
  ("not in the empty set" is true of every row — always-false there would silently invert the predicate). All
  four providers share the one strategy, so the single change covers them. `ParseConditionExpression` also
  stopped degrading an empty materialization to `IsNull` (a different wrong answer: rows with a NULL column).
- **SQL, `enumSet.Contains(x.EnumColumn)` matched zero rows** — on .NET 9+ an array `Contains` binds to
  `MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)` when `T` isn't `IEquatable<T>` (every
  enum), and the trailing `null` comparer was parsed as a value, flipping the condition to `IsNull`.
  `IsNonOperandArgument` now skips comparer / `StringComparison` / `CultureInfo` arguments (same family as the
  earlier `Contains(q, StringComparison…)` bug). Plus `NormalizeParameterValue` unwraps enums to their
  underlying integer in all four provider connectors — Npgsql rejects an unmapped CLR enum.
- **ElasticSearch, empty `Contains` DROPPED the clause** — `ParseContains` returned null and `CombineBool`
  drops nulls, so `ids.Contains(x.Field) && x.Status == active` with an empty `ids` silently became
  `x.Status == active`. Now `MatchNoneQuery` for both the empty and null collection (negation via `MustNot`
  gives every document — the same asymmetry as SQL's empty `NOT IN`).

Also **CR-H047 is now enforced at every ES filter→query boundary**, not just in `ElasticSearchViewStore`: the
entity stores assigned the parser's output straight to their requests across 14 sites, and a NEST request with
`Query = null` reads as match-all — so an untranslatable filter turned reads into "return everything" and
reached `_delete_by_query`/`_update_by_query` unguarded. Two shared helpers own the invariant:
`ParseFilterQuery` (optional filter — null filter means read-everything on purpose, untranslatable throws) and
`ParseRequiredFilterQuery` (the four destructive paths — a null filter throws). Three outcomes stay distinct
and only one is an error: no filter, matches-nothing (`MatchNoneQuery` — a legitimate translation), cannot be
expressed. Details: `Birko.Data.SQL` / `Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".

### Doc-index registration is now a required, linted step (2026-07-21)

`Birko.EventBus.Tenant` shipped fully built and build-registered yet invisible in every human-facing
doc (README project table, `CLAUDE-projects.md`, `docs/event-bus.md`) — an audit found it was the only
project of 175+ missing from the doc index. Added it there, and closed the gap structurally so it can't
recur: new **`CLAUDE-maintenance.md` § "Documentation Index Registration"** makes doc-index membership a
required registration step distinct from build-file registration (`.slnx`/`.code-workspace`/`.csproj`
make it compile; the doc index makes it discoverable), and **`verify-birko-conventions` check #7b** lints
new `.shproj` projects against all three index locations — including a full-repo drift sweep that catches
pre-existing gaps, not just the current diff.

### Shared expression normalizer — ternary / `??` / column-arithmetic in SQL predicates (2026-07-19)

STORY-047 follow-up. Added `Birko.Data.Expressions.ExpressionNormalizer` (in `Birko.Data.Core`) — a
backend-agnostic pre-pass the hand-rolled store parsers run at the lambda boundary. It **funcletizes**
any parameter-free subtree to a constant (collapsing parameter-free ternary / `??` / arithmetic and all
closures) and **desugars** boolean-typed ternary `c ? t : f` → `(c && t) || (!c && f)` and boolean-typed
`a ?? b` → `(a == true) || (a == null && b)`, so predicate parsers only ever see AND/OR/NOT/comparisons.
Wired into `DataBase.ParseConditionExpression` (Where/Delete/Update predicates) and `DataBase.ParseExpression`
(value position). SQL parser additionally gained **value-expression operands in predicates** — column
arithmetic (`x.A + x.B > 5`, `x.Price * 2 >= 10`, `x.Total == x.A + x.B`, `x.Bonus % 2 == 0`),
null-coalescing (`(x.Score ?? 0) > 5`) and a value-position ternary compared to something
(`(x.Vip ? x.Premium : x.Score) > 100`, i.e. **CASE in WHERE**). The value side renders to a raw fragment
in `Condition.Name` (arithmetic / `COALESCE` / `CASE WHEN`), operator flipped when the value is on the left,
`IsField` for column-vs-column; fragment-internal constants are inlined as portable SQL literals (numeric /
bool→1/0 / enum→int / escaped string), with `NotSupportedException` for non-portable types (DateTime/Guid)
instead of a silent drop. Value position also gained `COALESCE` / `CASE WHEN` / `IS [NOT] NULL`.
**ElasticSearch adopted the same normalizer** (`ParseLambda` runs it first), so boolean ternary / `??`
desugar to AND/OR/NOT with no ES-specific code; ES **value-expression operands** (arithmetic / value-`??` /
value-CASE compared) now emit a guarded Painless `ScriptQuery` (`(existence-guard) ? (body) : false` so a
missing field excludes the doc, matching C#) instead of being dropped, throwing on non-scriptable shapes
(also fixed ES `ContainsParameter` to recurse into `ConditionalExpression`). The **live document-backend
matrices** (Mongo/Cosmos/Raven `FilterMatrixLiveTests`) gained ternary/`??`/arithmetic shapes to check the
driver LINQ translators against the oracle when a backend is present (env-gated no-op otherwise). Tests:
`ExpressionNormalizerTests` (Core, 12) + `SqlPredicateNormalizationTests` (SqLite, 24, oracle-compared) +
`ExpressionDivergenceTests` (+5 ES cases); existing `SqlExpressionParityTests` (23) and full
SQL/SqLite/Core/ES suites green (312 / 75 / 29 / 102).

### Composite UNIQUE index support in attribute-driven SQL DDL (2026-07-18)

`[IndexedField(name, order, IsUnique: true)]` now emits a composite `CREATE UNIQUE INDEX` — the storage-level backstop for per-tenant uniqueness such as `(TenantGuid, Number)` (two tenants may each issue `FV2026000001`; the pair must still be unique). Additive and provider-wide:
- `IndexedField` gained `IsUnique` (default false); `Tables.IndexDefinition` gained `Unique`.
- `LoadIndexes` sets `idx.Unique` when any contributing `[IndexedField]` for that name is unique (both the direct-cast and cross-assembly reflection paths).
- `AbstractConnectorBase.CreateIndexSql` (all providers — SQLite/PostgreSQL/MySQL) and the `MSSqlConnector` override emit `CREATE UNIQUE INDEX` when `index.Unique`.
- **Class-level `[CompositeIndex("name", nameof(A), nameof(B), IsUnique = true)]`** (added next; `AttributeTargets.Class`, `Inherited = false`, `AllowMultiple`) declares a composite index whose columns may be **inherited from a base class** — the only safe way to form `(TenantGuid, Number)` when the tenant discriminator lives on a shared base type (per-property `[IndexedField]` on a base would land on every subclass table and collide on the DB-global index name). `LoadIndexes` resolves each property → column via the same fields map (so `[NamedField]`/ModelMap remaps and inherited props work) and fails fast at table-load on an unmapped property name. Reuses the existing `IndexDefinition.Unique` + `CreateIndexSql` — no connector changes.
- **Decision on the draft-empty case:** only a **full** unique index is emitted — partial/filtered unique indexes (`WHERE Number <> ''`, to allow multiple empty-string drafts) are **not** supported, because they are not portable (SQLite/PostgreSQL partial vs MSSQL filtered vs MySQL neither). Composite-unique therefore fits **always-populated** columns; columns left empty on drafts must rely on an application-level guarded allocator. Tests: `Birko.Data.SQL.Tests` `CompositeUniqueIndexTests` (DDL) + `Birko.Data.SQL.SqLite.Tests` `CompositeUniqueIndexEndToEndTests` (enforced end-to-end). Consumer follow-up tracked in Symbio `TASK-170`.

### Filter-parser parity: SQL negated-group fix + ElasticSearch gaps closed + cross-backend tests (2026-07-18)

Audited the LINQ-`Expression` filter translators across backends. Fixed two hand-rolled parsers and added parity tests:
- **SQL correctness bug:** a negated **group** (`!(a && b)`, `!(a || b)`, or a negated comparison that became a sub-group) rendered as `(a AND b)` with the `NOT` **silently dropped** — the filter matched the opposite rows. Fixed in `AbstractConnectorBase.AppendSubConditionsTo` (prefix `NOT` + parenthesise negated groups).
- **ElasticSearch gaps** (separate commit): `EndsWith`/`ToLower`/IN-pattern threw, bitwise `&`/`|` was silently dropped, bare/const bool produced malformed queries — all brought to parity. See `Birko.Data.ElasticSearch` CLAUDE.md § "Filter translation".
- **Tests:** `SqlExpressionParityTests` (SQLite oracle) + `ExpressionDivergenceTests` (ES structure) cover comparisons, null, strings, IN, and complex nested grouping in-process; env-var-gated `*FilterMatrixLiveTests` (Mongo/Cosmos/Raven) verify the native LINQ/driver translators against a compiled-delegate oracle when a live backend is available (tracked in `tasks/EPIC-011 STORY-047`).
