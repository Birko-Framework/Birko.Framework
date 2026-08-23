# Tasks — Birko.Framework

> ⚠ **Feature drift (2):** DV5 ×17 (every task in `_loose/` carries no epic **and** no feature, so none
> appears in a feature row) · specs DV7 ×3 (`filter-expression-translation`, `bulk-filter-operations`,
> `unit-of-work-and-transactions` — wide-surface areas, tracked as [[TASK-251]]) — run `/roadmap --check`.
>
> ℹ **DV11 and DV9 are cleared.** `shaped-by` is now derived on all 25 specs (`shaped-by-unresolved: 80`
> of 203 feature-linked tasks, stamped honestly rather than implied), so DV8 can fire again — it currently
> does not, because the one coarse-`done` feature carries the documented `no spec surface` carve-out. DV7
> went 9 → 3: two areas closed by measurement (no commits touching their sources since the spec was written)
> and four by harvest.
>
> ℹ **Backlog integrity: clear.** No duplicate ids, no non-`.md` files in the tree, so a `TASK-*` path count and
> the task count agree. The four findings raised by the 2026-08-18 regeneration are resolved — duplicate id
> `TASK-240` renumbered to [[TASK-250]], the spent `TASK-036-move.ps1` deleted, EPIC-018's status documented as
> deliberate, and the task-less-story count folded into [[TASK-149]] as evidence.
>
> **EPIC-018 will keep tripping the parent-contradiction check, and that is expected.** All four of its tasks
> are `done` while it stays `in-progress`, because it is an area-of-concern epic created to give
> `Birko.Web.Core` an owner — closing it would recreate the orphaning it exists to prevent. The reasoning is in
> its own `EPIC.md` (§ *Why this epic stays `in-progress` with no open tasks*); treat the flag as a known false
> positive there, not as work.
>
> **18 of the 56 stories hold zero task files**, and that splits two ways — only one is a problem. **Four are
> deliberate findings pools working as designed:** EPIC-014's STORY-024/025/026/027 each state *"Not
> pre-created — extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand"*, so the schedulable pool is
> the audit document and tasks are spawned as they are picked. `(0/0)` there means "nothing currently
> extracted", not "nothing tracked" — do **not** decompose them. **Thirteen are the real instance:** `done`
> stories whose shipped work lives only in the story body (the Xaml build-out STORY-029…038, STORY-043, and
> STORY-044/045). Tracked as [[TASK-149]]. STORY-042 is `planned` and probably a pool too; its wording differs
> so it was not matched mechanically.

_Generated 2026-08-19 (partial refresh at [[TASK-278]]’s close: counts, EPIC-014 rows, TASK-278 done; earlier at [[TASK-277]]’s close: TASK-277 done, 1 spawned ([[TASK-278]]); earlier at [[TASK-244]]’s close: TASK-244 done, 1 spawned ([[TASK-277]]); earlier at [[TASK-273]]’s close: TASK-273 done, 3 spawned ([[TASK-274]], [[TASK-275]], [[TASK-276]]); earlier at [[TASK-261]]’s close: counts, EPIC-014 rows; earlier at [[TASK-262]]’s close: 1 spawned; earlier at [[TASK-259]]’s close: 2 spawned; earlier at [[TASK-257]]’s close: 6 spawned; earlier partial at [[TASK-263]]'s close: counts, in-progress, EPIC-014 rows; the drift preamble is from the 2026-08-18 `/roadmap --check` and was not re-run). Run `/tasks triage` to refresh. **Do not hand-edit** — changes will be overwritten._

## Counts

| Status       | Epics | Stories | Tasks |
|--------------|-------|---------|-------|
| planned      | 10    | 24      | —     |
| todo         | —     | —       | 132   |
| in-progress  | 7     | 10      | 1     |
| review       | —     | —       | 10    |
| blocked      | —     | —       | 2     |
| done         | 1     | 22      | 102   |
| cancelled    | 0     | 0       | 1     |

Todo by priority: **P0 0 · P1 31 · P2 92 · P3 9**

<!-- Count note (2026-08-22, re-measured at TASK-273's close): 246 TASK files — todo 132, in-progress 1,
     review 10, blocked 2, done 99, cancelled 1; EPIC-014 alone is 139 files with 65 done. The EPIC-014 row
     had read 58/125 since before this session: three increments during TASK-273 were lost when a duplicate
     row was reverted with `git checkout --`, and the later edits matched a string that no longer existed and
     silently did nothing. Which is the reason for the rule below — a delta applied to a figure you did not
     measure fails silently, in both directions. -->
<!-- Earlier count note (2026-08-22): re-measured in one pass over the frontmatter while filing TASK-273, not
     derived by adding one to the previous figures. Two pre-existing errors were corrected rather than
     carried: the mix read 27+88+10 = 125 against a stated todo of 130, and in-progress read 1 while two
     tasks are in progress. Measure; do not add a delta to a figure you did not measure. -->


> Full regeneration 2026-08-18 — a fresh walk of every frontmatter in `tasks/`: 18 epics, 56 stories,
> **222 tasks** (117 + 1 + 10 + 2 + 91 + 1). The per-epic tree below is regenerated too, not just the counts;
> the previous run corrected the totals but left the body from the 2026-08-09 walk.
>
> The five-task index-DDL thread closed today — [[TASK-245]] → [[TASK-246]] → [[TASK-247]] → [[TASK-248]] →
> [[TASK-249]] — which is most of the `done` movement in EPIC-014.
>
> **Counts recounted 2026-08-18 (later, at [[TASK-253]]'s pick).** The walk above undercounted tasks by
> **8** — it reported 222 against a true 230, and todo 117 against 124. Six of the eight are tasks created
> after that walk ([[TASK-255]] and [[TASK-259]]/[[TASK-260]] from TASK-253's plan grill, plus
> [[TASK-256]]/[[TASK-257]]/[[TASK-258]] from another session); the remaining two predate it and were simply
> missed. Recorded rather than silently corrected, because "full regeneration" claimed a completeness the
> numbers did not have — the tree body below is **not** re-walked in this pass, only the counts and the two
> lists above.

## In progress now


- [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) — Birko.Web playground: component gallery + live token editor + theme-CSS export (P2, ai) · FEATURE-013

## In review (awaiting sign-off)

> Verification debt — code complete, human test plan not yet run. Close these before new scope.

- [TASK-118](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-118-tenant-header-guard-covers-only-x-tenant-id.md) — The tenant header/claim guard covers only the hard-coded `X-Tenant-Id` (P1, ai) · FEATURE-014
- [TASK-136](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-136-bform-validate-surfaces-control-validity.md) — `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()` (P1, ai) · FEATURE-001
- [TASK-135](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-135-b-input-decimal-comma-locale-mode.md) — `b-input type="decimal"`: comma-locale decimal entry, owned by the component (P1, ai) · FEATURE-016
- [TASK-228](EPIC-013-reference-consumers/TASK-228-track-birko-sandbox-in-git.md) — `Birko.Sandbox` is not a git repository — the smoke harness and the only dependency manifest exist on one disk (P1, ai) · FEATURE-013
- [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) — Add `bare` attribute to all form controls (P2, ai) · FEATURE-001
- [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) — Benchmark + migrate b-editable-table to bare components (P2, ai) · FEATURE-001
- [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) — Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai) · FEATURE-016
- [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) — `description` — a persistent help-text row on the form controls (P2, ai) · FEATURE-001
- [TASK-201](_loose/TASK-201-reps-declare-idpinned-on-client-minted-creates.md) — Reps: declare `idPinned` on the client-minted creates — and not on the one that must not have it (P2, ai)
- [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) — Make form controls form-associated via ElementInternals (P3, ai) · FEATURE-001


## Tree

- **EPIC-001** Birko.Web.Components — UI polish — in-progress (4/13 tasks done)
  - **STORY-001** bare attribute for inline form usage — in-progress (0/1)
    - [ ] [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add `bare` attribute to all form controls 🔍 review · FEATURE-001
  - **STORY-002** b-editable-table migration to bare components — in-progress (0/1)
    - [ ] [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table to bare components 🔍 review · FEATURE-001
  - **STORY-003** size attribute coverage — planned (0/1)
    - [ ] [TASK-003](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/TASK-003-size-on-pagination-dropdown-breadcrumb.md) size attribute on b-pagination, b-dropdown-menu, b-breadcrumb · FEATURE-001
  - **STORY-023** Form-associated custom elements (ElementInternals) — in-progress (0/5)
    - [ ] [TASK-136](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-136-bform-validate-surfaces-control-validity.md) `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()` 🔍 review · FEATURE-001
    - [ ] [TASK-132](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-132-bform-required-inert-on-unchecked-toggle.md) `b-form`: `required` on a checkbox / switch is inert — an unchecked toggle counts as filled · FEATURE-001
    - [ ] [TASK-133](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-133-bform-radio-value-never-collected.md) `b-form`: a `radio` field's value is never collected, and a `required` radio group can never validate · FEATURE-001
    - [ ] [TASK-134](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-134-bform-remaining-validity-flags-decision.md) Decide whether `b-form.validate()` adopts the remaining validity flags, starting with `typeMismatch` · FEATURE-001
    - [ ] [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals 🔍 review · FEATURE-001
  - **STORY-028** Display & disclosure components — done (3/3) (done)
    - [x] [TASK-040](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-040-b-accordion-component.md) Add a `b-accordion` (collapsible / disclosure group) component · FEATURE-001
    - [x] [TASK-041](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-041-shared-coerce-css-length.md) Extract a shared `coerceCssLength` helper and fix the unitless-length bug across components · FEATURE-001
    - [x] [TASK-039](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-039-b-chart-coerce-unitless-height.md) b-chart: coerce/validate a unitless `height` (avoid endless SVG stretch) · FEATURE-001
  - **STORY-050** Visible help text on form controls — in-progress (0/1)
    - [ ] [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) `description` — a persistent help-text row on the form controls 🔍 review · FEATURE-001
  - _tracked directly on the epic_
    - [x] [TASK-053](EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md) b-range: vertical orientation (equalizer-style slider) · FEATURE-001
- **EPIC-002** Birko.Data.Redis — planned (0/1 tasks done)
  - _tracked directly on the epic_
    - [ ] [TASK-004](EPIC-002-birko-data-redis/TASK-004-implement-birko-data-redis.md) Implement Birko.Data.Redis · FEATURE-002
- **EPIC-003** Birko.Caching.NCache — planned (0/1 tasks done)
  - _tracked directly on the epic_
    - [ ] [TASK-005](EPIC-003-birko-caching-ncache/TASK-005-implement-birko-caching-ncache.md) Implement Birko.Caching.NCache · FEATURE-003
- **EPIC-004** Birko.Storage — Cloud providers — planned (0/3 tasks done)
  - **STORY-004** AWS S3 storage — planned (0/1)
    - [ ] [TASK-006](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/TASK-006-birko-storage-aws.md) Implement Birko.Storage.Aws · FEATURE-004
  - **STORY-005** Google Cloud Storage — planned (0/1)
    - [ ] [TASK-007](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/TASK-007-birko-storage-google.md) Implement Birko.Storage.Google · FEATURE-004
  - **STORY-006** MinIO (S3-compatible) — planned (0/1)
    - [ ] [TASK-008](EPIC-004-storage-cloud-providers/STORY-006-minio/TASK-008-birko-storage-minio.md) Implement Birko.Storage.Minio · FEATURE-004
- **EPIC-005** Birko.Messaging — Provider expansion — planned (0/5 tasks done)
  - **STORY-007** Email providers (SendGrid + Mailgun) — planned (0/2)
    - [ ] [TASK-009](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-009-birko-messaging-sendgrid.md) Implement Birko.Messaging.SendGrid · FEATURE-005
    - [ ] [TASK-010](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-010-birko-messaging-mailgun.md) Implement Birko.Messaging.Mailgun · FEATURE-005
  - **STORY-008** SMS via Twilio — planned (0/1)
    - [ ] [TASK-011](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/TASK-011-birko-messaging-twilio.md) Implement Birko.Messaging.Twilio · FEATURE-005
  - **STORY-009** Push notifications (Firebase + APNs) — planned (0/2)
    - [ ] [TASK-012](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-012-birko-messaging-firebase.md) Implement Birko.Messaging.Firebase · FEATURE-005
    - [ ] [TASK-013](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-013-birko-messaging-apple.md) Implement Birko.Messaging.Apple · FEATURE-005
- **EPIC-006** Birko.MessageQueue — Provider expansion — planned (0/5 tasks done)
  - **STORY-010** RabbitMQ (AMQP) — planned (0/1)
    - [ ] [TASK-014](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/TASK-014-birko-messagequeue-rabbitmq.md) Implement Birko.MessageQueue.RabbitMQ · FEATURE-006
  - **STORY-011** Kafka — planned (0/1)
    - [ ] [TASK-015](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/TASK-015-birko-messagequeue-kafka.md) Implement Birko.MessageQueue.Kafka · FEATURE-006
  - **STORY-012** Cloud queue providers (Azure Service Bus + AWS SQS) — planned (0/2)
    - [ ] [TASK-016](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-016-birko-messagequeue-azure.md) Implement Birko.MessageQueue.Azure · FEATURE-006
    - [ ] [TASK-017](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-017-birko-messagequeue-aws.md) Implement Birko.MessageQueue.Aws · FEATURE-006
  - **STORY-013** MassTransit adapter — planned (0/1)
    - [ ] [TASK-018](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/TASK-018-birko-messagequeue-masstransit.md) Implement Birko.MessageQueue.MassTransit · FEATURE-006
- **EPIC-007** Birko.Telemetry — Additional exporters — planned (0/3 tasks done)
  - **STORY-014** Prometheus exporter — planned (0/1)
    - [ ] [TASK-019](EPIC-007-telemetry-exporters/STORY-014-prometheus/TASK-019-birko-telemetry-prometheus.md) Implement Birko.Telemetry.Prometheus · FEATURE-007
  - **STORY-015** Seq log exporter — planned (0/1)
    - [ ] [TASK-020](EPIC-007-telemetry-exporters/STORY-015-seq/TASK-020-birko-telemetry-seq.md) Implement Birko.Telemetry.Seq · FEATURE-007
  - **STORY-016** Grafana LGTM stack exporter — planned (0/1)
    - [ ] [TASK-021](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/TASK-021-birko-telemetry-grafana.md) Implement Birko.Telemetry.Grafana · FEATURE-007
- **EPIC-008** Birko.Health — Queue + cloud health checks — planned (0/4 tasks done)
  - **STORY-017** Message queue health checks — planned (0/2)
    - [ ] [TASK-022](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-022-rabbitmq-health-check.md) RabbitMqHealthCheck · FEATURE-008
    - [ ] [TASK-023](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-023-kafka-health-check.md) KafkaHealthCheck · FEATURE-008
  - **STORY-018** Cloud queue health checks — planned (0/2)
    - [ ] [TASK-024](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-024-azure-service-bus-health-check.md) AzureServiceBusHealthCheck · FEATURE-008
    - [ ] [TASK-025](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-025-aws-sqs-health-check.md) AwsSqsHealthCheck · FEATURE-008
- **EPIC-010** Birko.Data.RavenDB — Index ergonomics — planned (0/1 tasks done)
  - _tracked directly on the epic_
    - [ ] [TASK-028](EPIC-010-ravendb-index-ergonomics/TASK-028-attribute-driven-raven-indexes.md) Attribute-driven RavenDB index definitions (Option B) · FEATURE-010
- **EPIC-011** Birko.Framework — Test coverage gaps — planned (0/7 tasks done)
  - **STORY-021** Redis-dependent tests — planned (0/2)
    - [ ] [TASK-029](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-029-backgroundjobs-redis-tests.md) Birko.BackgroundJobs.Redis.Tests · FEATURE-011
    - [ ] [TASK-030](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-030-caching-redis-tests.md) Birko.Caching.Redis.Tests · FEATURE-011
  - **STORY-022** Phase 4 lower-priority tests — planned (0/3)
    - [ ] [TASK-031](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-031-models-validation-tests.md) Birko.Models.* validation tests · FEATURE-011
    - [ ] [TASK-032](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-032-viewmodel-crud-tests.md) Birko.Data.*.ViewModel CRUD tests · FEATURE-011
    - [ ] [TASK-033](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-033-configuration-contracts-tests.md) Birko.Configuration + Birko.Contracts DTO tests · FEATURE-011
  - **STORY-047** Review filter-parser behaviour on live document databases — planned (0/1)
    - [ ] [TASK-060](EPIC-011-test-coverage-gaps/STORY-047-null-filter-live-parser-review/TASK-060-run-and-review-live-null-tests.md) Run & review the live null-filter parser tests · FEATURE-011
  - _tracked directly on the epic_
    - [ ] [TASK-052](EPIC-011-test-coverage-gaps/TASK-052-birko-web-unit-test-runner.md) Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke) · FEATURE-011
- **EPIC-012** Birko.MessageQueue.MQTT — v5 features — planned (0/1 tasks done)
  - _tracked directly on the epic_
    - [ ] [TASK-034](EPIC-012-mqtt-v5-features/TASK-034-mqtt-v5-topic-aliases-user-properties.md) MQTT v5 topic aliases + user properties · FEATURE-012
- **EPIC-013** Reference consumers — integration smoke harness + Web playground — in-progress (1/3 tasks done)
  - _tracked directly on the epic_
    - [ ] [TASK-228](EPIC-013-reference-consumers/TASK-228-track-birko-sandbox-in-git.md) `Birko.Sandbox` is not a git repository — the smoke harness and the only dependency manifest exist on one disk 🔍 review · FEATURE-013
    - [x] [TASK-037](EPIC-013-reference-consumers/TASK-037-extract-backend-smoke-harness-consumer.md) Replace the TUI example with an extracted backend integration smoke-harness consumer · FEATURE-013
    - [ ] [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: component gallery + live token editor + theme-CSS export ← in-progress · FEATURE-013
- **EPIC-014** Code review — audit remediation — in-progress (68/141 tasks done)
  - **STORY-024** Critical findings — done (0/0) (done)
  - **STORY-025** High findings — done (0/0) (done)
  - **STORY-026** Medium findings — in-progress (0/0)
  - **STORY-027** Low findings — done (0/0) (done)
  - **STORY-042** Integration-test tier — the Docker-gated remediation findings — planned (0/0)
  - **STORY-043** Workflow backends — unify the serialization seam (ISerializer everywhere) — done (0/0) (done)
  - **STORY-051** Spec-harvest — high findings — in-progress (30/31)
    - [x] [TASK-108](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-108-pbkdf2-empty-segment-auth-bypass.md) `Pbkdf2PasswordHasher.Verify` returns `true` for any password against an empty-segment hash · FEATURE-014
    - [x] [TASK-109](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-109-sql-bulk-null-filter-whole-table-statement.md) A null or untranslatable filter renders `DELETE FROM "T"` — the whole table · FEATURE-014
    - [x] [TASK-110](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-110-order-by-identifier-unresolved-and-unquoted.md) ORDER BY identifiers reach SQL text unresolved and unquoted · FEATURE-014
    - [x] [TASK-112](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-112-unmapped-primitive-types-never-persist.md) `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist · FEATURE-014
    - [x] [TASK-113](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-113-tenantsyncprovider-scopes-only-saves.md) `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant · FEATURE-014
    - [x] [TASK-114](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-114-tenant-write-guard-trusts-caller-supplied-tenantguid.md) The item-level tenant write guard trusts the caller-supplied `TenantGuid` · FEATURE-014
    - [x] [TASK-116](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-116-rulespecification-leaves-degrade-to-match-all.md) `RuleSpecification` leaves degrade to match-all — on the destructive paths · FEATURE-014
    - [x] [TASK-128](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-128-view-order-by-identifier-unresolved.md) The view path's ORDER BY still interpolates caller text — the twin TASK-110 did not cover · FEATURE-014
    - [x] [TASK-111](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-111-rule-field-unresolved-in-where-clause.md) `rule.Field` reaches the WHERE clause unresolved and unquoted · FEATURE-014
    - [x] [TASK-115](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-115-nested-withtenant-does-not-narrow-reads.md) A nested `WithTenant` does not narrow reads inside an all-tenants scope · FEATURE-014
    - [x] [TASK-117](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-117-rediscache-clearasync-flushdb.md) `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set · FEATURE-014
    - [ ] [TASK-118](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-118-tenant-header-guard-covers-only-x-tenant-id.md) The tenant header/claim guard covers only the hard-coded `X-Tenant-Id` 🔍 review · FEATURE-014
    - [x] [TASK-125](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-125-readone-bypasses-store-decorators.md) `ReadOne` queries the connector directly, bypassing every store decorator · FEATURE-014
    - [x] [TASK-126](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-126-tagging-has-no-tenant-assertion.md) `TagServiceBase` states its tenant contract in a comment and enforces nothing · FEATURE-014
    - [x] [TASK-129](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-129-aggregate-view-ddl-double-alias.md) An aggregate view's generated DDL carries a double alias, so no persistent aggregate view can be created · FEATURE-014
    - [x] [TASK-209](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-209-persistent-view-nonaggregate-columns-quoting-mismatch.md) A persistent view's non-aggregate columns are created unquoted and read back quoted — every such view is unqueryable on PostgreSQL · FEATURE-014
    - [x] [TASK-212](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-212-mongodb-filter-that-reduces-to-everything.md) A MongoDB `Delete(filter)` guards only a NULL filter — a filter that *reduces* to everything is not refused · FEATURE-014
    - [x] [TASK-213](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-213-computed-operand-inside-contains-is-silently-rewritten.md) A COMPUTED operand inside `Contains` is silently discarded and replaced by a different predicate · FEATURE-014
    - [x] [TASK-214](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-214-mongodbmodel-cannot-be-class-mapped.md) A model deriving `MongoDBModel` cannot be serialized by the driver at all · FEATURE-014
    - [x] [TASK-218](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-218-array-contains-in-a-filter-does-not-translate-on-mongodb.md) An `IN` filter over a C# **array** does not translate on MongoDB — `NotSupportedException` · FEATURE-014
    - [x] [TASK-219](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-219-mongodb-has-two-contradictory-answers-for-what-id-is.md) `Birko.Data.MongoDB` has two contradictory answers for what `_id` is · FEATURE-014
    - [x] [TASK-220](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-220-cosmosdb-has-the-same-array-contains-defect.md) CosmosDB has the same array-`Contains` defect as MongoDB — audit the rest of the family · FEATURE-014
    - [x] [TASK-221](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-221-ravendb-cannot-translate-a-set-membership-filter.md) RavenDB cannot translate **any** set-membership filter — `Contains` is unsupported in every spelling · FEATURE-014
    - [x] [TASK-222](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-222-ravendb-diverges-on-six-filter-shapes.md) RavenDB diverges on 6 filter shapes — and one of them is a **silent wrong answer** · FEATURE-014
    - [x] [TASK-223](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-223-cosmos-connection-mode-cannot-be-selected.md) CosmosDB's connection mode cannot be selected — Gateway is unreachable, so the emulator is too · FEATURE-014
    - [x] [TASK-224](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-224-cosmos-date-renders-as-a-subproperty.md) `DateTime.Date` in a CosmosDB filter renders as a JSON sub-property and silently matches nothing · FEATURE-014
    - [x] [TASK-137](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-137-empty-not-in-renders-injection-lookalike.md) An empty `NOT IN` renders `1 = 1` — indistinguishable from `' OR 1=1--` in a query log · FEATURE-014
    - [x] [TASK-141](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-141-mongodb-null-filter-guards-are-untested.md) MongoDB's four null-filter guards have no regression test · FEATURE-014
    - [x] [TASK-207](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-207-viewaddfield-drops-duplicate-keys-silently.md) `View.AddField` still drops a duplicate field key silently — the general case behind TASK-129's second defect · FEATURE-014
    - [x] [TASK-215](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-215-wire-bounded-filter-guard-into-remaining-backends.md) Wire `RequireBoundedFilter` into the base wrappers, InMemory and ElasticSearch · FEATURE-014
    - [x] [TASK-225](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-225-mongodb-connection-string-has-no-escape-hatch.md) MongoDB's connection string is composed with no escape hatch — no driver option can be set · FEATURE-014
  - **STORY-053** Spec-harvest — medium findings — planned (0/22)
    - [ ] [TASK-151](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-151-triage-medium-views-and-aggregation.md) Triage the 36 medium spec-harvest findings in `views-and-aggregation` · FEATURE-014
    - [ ] [TASK-152](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-152-triage-medium-migrations.md) Triage the 33 medium spec-harvest findings in `migrations` · FEATURE-014
    - [ ] [TASK-153](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-153-triage-medium-filter-expression-translation.md) Triage the 29 medium spec-harvest findings in `filter-expression-translation` · FEATURE-014
    - [ ] [TASK-154](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-154-triage-medium-schema-index-and-ddl.md) Triage the 25 medium spec-harvest findings in `schema-index-and-ddl` · FEATURE-014
    - [ ] [TASK-156](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-156-triage-medium-validation-and-rules.md) Triage the 22 medium spec-harvest findings in `validation-and-rules` · FEATURE-014
    - [ ] [TASK-157](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-157-triage-medium-data-sync.md) Triage the 21 medium spec-harvest findings in `data-sync` · FEATURE-014
    - [ ] [TASK-159](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-159-triage-medium-store-decorator-composition.md) Triage the 20 medium spec-harvest findings in `store-decorator-composition` · FEATURE-014
    - [ ] [TASK-161](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-161-triage-medium-tenant-isolation.md) Triage the 18 medium spec-harvest findings in `tenant-isolation` · FEATURE-014
    - [ ] [TASK-162](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-162-triage-medium-repository-contract.md) Triage the 16 medium spec-harvest findings in `repository-contract` · FEATURE-014
    - [ ] [TASK-163](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-163-triage-medium-store-crud-contract.md) Triage the 15 medium spec-harvest findings in `store-crud-contract` · FEATURE-014
    - [ ] [TASK-165](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-165-triage-medium-security-and-authorization.md) Triage the 15 medium spec-harvest findings in `security-and-authorization` · FEATURE-014
    - [ ] [TASK-170](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-170-triage-medium-bulk-filter-operations.md) Triage the 13 medium spec-harvest findings in `bulk-filter-operations` · FEATURE-014
    - [ ] [TASK-171](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-171-triage-medium-specifications-and-paging.md) Triage the 12 medium spec-harvest findings in `specifications-and-paging` · FEATURE-014
    - [ ] [TASK-155](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-155-triage-medium-event-bus-and-messaging.md) Triage the 24 medium spec-harvest findings in `event-bus-and-messaging` · FEATURE-014
    - [ ] [TASK-158](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-158-triage-medium-background-jobs.md) Triage the 21 medium spec-harvest findings in `background-jobs` · FEATURE-014
    - [ ] [TASK-160](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-160-triage-medium-llm-provider-and-agents.md) Triage the 20 medium spec-harvest findings in `llm-provider-and-agents` · FEATURE-014
    - [ ] [TASK-164](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-164-triage-medium-settings-configuration-chain.md) Triage the 15 medium spec-harvest findings in `settings-configuration-chain` · FEATURE-014
    - [ ] [TASK-166](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-166-triage-medium-entity-tagging.md) Triage the 15 medium spec-harvest findings in `entity-tagging` · FEATURE-014
    - [ ] [TASK-167](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-167-triage-medium-serialization.md) Triage the 14 medium spec-harvest findings in `serialization` · FEATURE-014
    - [ ] [TASK-168](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-168-triage-medium-entity-localization.md) Triage the 14 medium spec-harvest findings in `entity-localization` · FEATURE-014
    - [ ] [TASK-169](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-169-triage-medium-caching.md) Triage the 14 medium spec-harvest findings in `caching` · FEATURE-014
    - [ ] [TASK-172](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/TASK-172-triage-medium-workflow-state-machine.md) Triage the 9 medium spec-harvest findings in `workflow-state-machine` · FEATURE-014
  - **STORY-054** Spec-harvest — low findings — planned (0/22)
    - [ ] [TASK-173](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-173-triage-low-llm-provider-and-agents.md) Triage the 31 low spec-harvest findings in `llm-provider-and-agents` · FEATURE-014
    - [ ] [TASK-174](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-174-triage-low-event-bus-and-messaging.md) Triage the 29 low spec-harvest findings in `event-bus-and-messaging` · FEATURE-014
    - [ ] [TASK-175](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-175-triage-low-background-jobs.md) Triage the 24 low spec-harvest findings in `background-jobs` · FEATURE-014
    - [ ] [TASK-176](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-176-triage-low-security-and-authorization.md) Triage the 23 low spec-harvest findings in `security-and-authorization` · FEATURE-014
    - [ ] [TASK-177](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-177-triage-low-data-sync.md) Triage the 23 low spec-harvest findings in `data-sync` · FEATURE-014
    - [ ] [TASK-178](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-178-triage-low-migrations.md) Triage the 22 low spec-harvest findings in `migrations` · FEATURE-014
    - [ ] [TASK-179](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-179-triage-low-workflow-state-machine.md) Triage the 21 low spec-harvest findings in `workflow-state-machine` · FEATURE-014
    - [ ] [TASK-180](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-180-triage-low-views-and-aggregation.md) Triage the 20 low spec-harvest findings in `views-and-aggregation` · FEATURE-014
    - [ ] [TASK-181](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-181-triage-low-validation-and-rules.md) Triage the 19 low spec-harvest findings in `validation-and-rules` · FEATURE-014
    - [ ] [TASK-182](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-182-triage-low-store-crud-contract.md) Triage the 19 low spec-harvest findings in `store-crud-contract` · FEATURE-014
    - [ ] [TASK-183](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-183-triage-low-settings-configuration-chain.md) Triage the 18 low spec-harvest findings in `settings-configuration-chain` · FEATURE-014
    - [ ] [TASK-184](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-184-triage-low-caching.md) Triage the 17 low spec-harvest findings in `caching` · FEATURE-014
    - [ ] [TASK-185](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-185-triage-low-tenant-isolation.md) Triage the 15 low spec-harvest findings in `tenant-isolation` · FEATURE-014
    - [ ] [TASK-186](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-186-triage-low-entity-tagging.md) Triage the 14 low spec-harvest findings in `entity-tagging` · FEATURE-014
    - [ ] [TASK-187](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-187-triage-low-store-decorator-composition.md) Triage the 13 low spec-harvest findings in `store-decorator-composition` · FEATURE-014
    - [ ] [TASK-188](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-188-triage-low-specifications-and-paging.md) Triage the 13 low spec-harvest findings in `specifications-and-paging` · FEATURE-014
    - [ ] [TASK-189](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-189-triage-low-filter-expression-translation.md) Triage the 13 low spec-harvest findings in `filter-expression-translation` · FEATURE-014
    - [ ] [TASK-190](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-190-triage-low-bulk-filter-operations.md) Triage the 13 low spec-harvest findings in `bulk-filter-operations` · FEATURE-014
    - [ ] [TASK-191](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-191-triage-low-serialization.md) Triage the 10 low spec-harvest findings in `serialization` · FEATURE-014
    - [ ] [TASK-192](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-192-triage-low-schema-index-and-ddl.md) Triage the 10 low spec-harvest findings in `schema-index-and-ddl` · FEATURE-014
    - [ ] [TASK-193](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-193-triage-low-repository-contract.md) Triage the 10 low spec-harvest findings in `repository-contract` · FEATURE-014
    - [ ] [TASK-194](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/TASK-194-triage-low-entity-localization.md) Triage the 10 low spec-harvest findings in `entity-localization` · FEATURE-014
  - **STORY-055** Spec-harvest — the three unrated areas — in-progress (0/1)
    - [ ] [TASK-195](EPIC-014-code-review-remediation/STORY-055-spec-harvest-unrated-areas/TASK-195-rate-id-and-fold-the-16-recovered-findings.md) Rate, ID and fold the 16 recovered findings into the severity backlog · FEATURE-014
  - _tracked directly on the epic_
    - [x] [TASK-196](EPIC-014-code-review-remediation/TASK-196-date-truncated-comparison-matched-zero-rows.md) `x.Col.Date == value` matched zero rows on every input, every column, every day · FEATURE-014
    - [x] [TASK-197](EPIC-014-code-review-remediation/TASK-197-timeonly-has-no-column-mapping.md) `TimeOnly` had no column mapping — and after [[TASK-112]] it took the whole entity down · FEATURE-014
    - [x] [TASK-204](EPIC-014-code-review-remediation/TASK-204-an-unbuildable-index-took-down-the-whole-entity.md) An index that could not be built took the entity's whole read surface with it — permanently · FEATURE-014
    - [x] [TASK-211](EPIC-014-code-review-remediation/TASK-211-on-the-fly-views-broken-on-postgresql-and-the-error-is-swallowed.md) On-the-fly views are broken on PostgreSQL — and the error is swallowed, so they return an empty result · FEATURE-014
    - [x] [TASK-216](EPIC-014-code-review-remediation/TASK-216-filtered-writes-qualify-the-where-and-break-on-postgresql.md) A filtered DELETE / UPDATE qualifies its `WHERE` with a bare table name, so every filtered write fails on PostgreSQL · FEATURE-014
    - [x] [TASK-231](EPIC-014-code-review-remediation/TASK-231-outbox-sql-shipped-unregistered-and-untested.md) `Birko.EventBus.Outbox.SQL` shipped complete but registered nowhere — unbuilt, untested, invisible · FEATURE-014
    - [x] [TASK-240](EPIC-014-code-review-remediation/TASK-240-a-transaction-boundary-async-writes-honour.md) A transaction boundary that async writes actually honour, stated per provider · FEATURE-014
    - [x] [TASK-241](EPIC-014-code-review-remediation/TASK-241-ravendb-never-sets-the-document-id-from-the-entity-guid.md) RavenDB never sets the document id from the entity Guid — delete is a silent no-op and update duplicates · FEATURE-014
    - [x] [TASK-242](EPIC-014-code-review-remediation/TASK-242-bulk-writes-escape-the-transaction-boundary.md) Every bulk write escaped the transaction boundary, and on three providers it did so silently · FEATURE-014
    - [x] [TASK-246](EPIC-014-code-review-remediation/TASK-246-migration-unique-index-is-silently-not-unique.md) A migration's `.Unique()` silently builds a NON-unique index on every SQL provider · FEATURE-014
    - [x] [TASK-248](EPIC-014-code-review-remediation/TASK-248-mysql-cannot-index-an-unbounded-string-column.md) MySQL cannot index an unbounded `string` column — and that is the canonical documented pattern · FEATURE-014
    - [x] [TASK-249](EPIC-014-code-review-remediation/TASK-249-close-gate-findings-on-the-index-ddl-fix.md) Four close-gate findings on TASK-245 — including a second injection sink its own rule pointed at · FEATURE-014
    - [x] [TASK-058](EPIC-014-code-review-remediation/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md) SqLiteConnector emits invalid AUTOINCREMENT DDL for non-primary-key increment fields (dual-key models) · FEATURE-014
    - [x] [TASK-131](EPIC-014-code-review-remediation/TASK-131-per-sub-repo-spec-trees.md) Per-sub-repo `docs/specs/` trees — the aggregator's staleness guard cannot fire · FEATURE-014
    - [ ] [TASK-150](EPIC-014-code-review-remediation/TASK-150-char-nullable-and-the-remaining-unmapped-types.md) `char?`, `TimeSpan` and `DateTimeOffset` have no column mapping — they now fail loudly instead of quietly · FEATURE-014
    - ~~TASK-205 A qualified `Table.Column` is emitted unquoted while `FROM "Table"` is quoted — PostgreSQL folds them apart~~ · FEATURE-014
    - [ ] [TASK-208](EPIC-014-code-review-remediation/TASK-208-decide-spec-coverage-for-view-ddl-emitters.md) DECISION: which of `Birko.Data.SQL.View` the spec map should cover — two fixes have now landed in the excluded part · FEATURE-014
    - [x] [TASK-210](EPIC-014-code-review-remediation/TASK-210-mongodb-driver-transitive-vulnerability-advisories.md) `MongoDB.Driver 3.2.0` pulls two vulnerable transitive packages, and nothing reports it · FEATURE-014
    - [ ] [TASK-217](EPIC-014-code-review-remediation/TASK-217-update-overload-builds-its-set-list-from-every-column.md) `Update(Table, values, conditions)` builds its SET list from every column, so a partial update cannot work · FEATURE-014
    - [x] [TASK-227](EPIC-014-code-review-remediation/TASK-227-generated-at-always-precedes-the-spec-it-stamps.md) `generated-at` always names the commit *before* the spec it stamps, so staleness is measured from too early · FEATURE-014
    - [x] [TASK-229](EPIC-014-code-review-remediation/TASK-229-shared-projects-declare-their-own-driver-packages.md) Two shared projects `using` a driver they do not declare — and three sources disagree about whose job it is · FEATURE-014
    - [x] [TASK-230](EPIC-014-code-review-remediation/TASK-230-remaining-vulnerable-transitives-across-the-family.md) The remaining vulnerable transitives — 7 advisories across 37 of 246 projects · FEATURE-014
    - [x] [TASK-232](EPIC-014-code-review-remediation/TASK-232-six-of-eight-job-backends-cannot-supply-a-lock.md) DECISION: the lock contract meant three different things — split the durations, keep session semantics · FEATURE-014
    - [x] [TASK-234](EPIC-014-code-review-remediation/TASK-234-thirty-eight-shared-projects-declare-no-dependency.md) 38 more shared projects use an external package they never declare · FEATURE-014
    - [x] [TASK-237](EPIC-014-code-review-remediation/TASK-237-leader-election-for-the-recurring-scheduler.md) `RecurringJobScheduler` duplicates every job per worker — wire leader election · FEATURE-014
    - [x] [TASK-243](EPIC-014-code-review-remediation/TASK-243-mysql-ddl-implicitly-commits-an-open-boundary.md) On MySQL, a store's first operation inside a boundary silently commits that boundary · FEATURE-014
    - [x] [TASK-245](EPIC-014-code-review-remediation/TASK-245-mysql-cannot-create-any-declared-index.md) Index DDL every provider accepts — MySQL rejected the clause, PostgreSQL could not resolve the columns · FEATURE-014
    - [x] [TASK-247](EPIC-014-code-review-remediation/TASK-247-schema-builder-fallback-emits-broken-index-ddl.md) `SqlSchemaBuilder`'s raw-SQL fallbacks emit index DDL that two providers reject · FEATURE-014
    - [x] [TASK-250](EPIC-014-code-review-remediation/TASK-250-spec-source-globs-are-not-git-pathspecs.md) A spec source glob is not a git pathspec, so the staleness check never saw 124 files · FEATURE-014
    - [ ] [TASK-144](EPIC-014-code-review-remediation/TASK-144-two-rule-translators-one-rule-model.md) `RuleSpecification` and `RuleExpressionConverter` are two translators of one rule model · FEATURE-014
    - [ ] [TASK-146](EPIC-014-code-review-remediation/TASK-146-async-ordered-readone-parity.md) Nothing pins that the async repository has no connector-bypassing read · FEATURE-014
    - [ ] [TASK-226](EPIC-014-code-review-remediation/TASK-226-per-sub-repo-spec-trees-for-single-repo-areas.md) Per-sub-repo `docs/specs/` trees for the 4 single-repo areas (and the 64 unspecced projects) · FEATURE-014
    - [ ] [TASK-233](EPIC-014-code-review-remediation/TASK-233-cosmos-span-rewrite-may-be-redundant.md) DECISION: the CosmosDB span-`Contains` rewrite may now be redundant — the SDK fixed it upstream · FEATURE-014
    - [x] [TASK-236](EPIC-014-code-review-remediation/TASK-236-lock-providers-for-the-six-remaining-job-backends.md) A per-backend verdict on locking for the six job backends without a provider · FEATURE-014
    - [x] [TASK-238](EPIC-014-code-review-remediation/TASK-238-sync-projitems-reference-a-projitems.md) Seven `Birko.Data.Sync.*` projitems carry a `ProjectReference` to another `.projitems` · FEATURE-014
    - [ ] [TASK-239](EPIC-014-code-review-remediation/TASK-239-over-declared-packages-net10-provides.md) Packages declared that .NET 10 already provides — `NU1510`, the mirror image of TASK-234 · FEATURE-014
    - [x] [TASK-244](EPIC-014-code-review-remediation/TASK-244-schema-ensure-runs-before-the-boundary-is-published.md) Lazy schema-ensure runs before the store publishes its transaction boundary · FEATURE-014 · ⚠ **P3 → P1 2026-08-22**: consumer Symbio hit a live instance (its TASK-527) — setup answered 200 while the `Users` table was never created, login failed forever, and the database could not be built at all
    - [x] [TASK-253](EPIC-014-code-review-remediation/TASK-253-migration-hypertable-emitters-carry-the-same-folding-defect.md) The migration hypertable emitters carry the same identifier defect — and one bypasses the DDL funnel · FEATURE-014
    - [ ] [TASK-255](EPIC-014-code-review-remediation/TASK-255-continuous-aggregate-hardcodes-its-time-column.md) `BuildContinuousAggregateSql` still hardcodes `time` — CR-H070 unfixed in the method next door · FEATURE-014
    - [x] [TASK-256](EPIC-014-code-review-remediation/TASK-256-postgres-copy-cannot-bind-a-utc-datetime.md) PostgreSQL's binary `COPY` cannot bind a UTC `DateTime`, and the test suite is green because its fixture avoids it · FEATURE-014
    - [x] [TASK-257](EPIC-014-code-review-remediation/TASK-257-mssql-maps-unlengthed-strings-to-text.md) On MSSql an unlengthed `string` column becomes `TEXT`, so no predicate on it works · FEATURE-014
    - [ ] [TASK-264](EPIC-014-code-review-remediation/TASK-264-migrations-lose-declared-column-metadata.md) A migration's declared column metadata is dropped on the way to the connector · FEATURE-014
    - [ ] [TASK-265](EPIC-014-code-review-remediation/TASK-265-mysql-unique-primary-unlengthed-string.md) On MySQL a `[UniqueField]`/`[PrimaryField]` unlengthed string still emits `LONGTEXT` · FEATURE-014
    - [ ] [TASK-266](EPIC-014-code-review-remediation/TASK-266-binary-and-wide-composite-index-keys.md) Index keys still wrong after TASK-257: a `byte[]` column, and a too-wide composite · FEATURE-014
    - [ ] [TASK-267](EPIC-014-code-review-remediation/TASK-267-verify-conventions-shadow-does-not-shadow.md) The project-local `verify-conventions` did not run at the close gate, again · FEATURE-014
    - [ ] [TASK-268](EPIC-014-code-review-remediation/TASK-268-field-type-mapping-polish.md) Two small SQL field-mapping gaps (`TimeOnly` width, `CharField.Lenght` null) · FEATURE-014
    - [ ] [TASK-269](EPIC-014-code-review-remediation/TASK-269-nothing-reports-a-stale-declared-column-type.md) Nothing reports a column whose stored type no longer matches the model · FEATURE-014
    - [ ] [TASK-270](EPIC-014-code-review-remediation/TASK-270-connector-cache-invites-per-caller-state.md) `DataBase.GetConnector` shares one connector process-wide, and three features have put per-caller state on it · FEATURE-014
    - [ ] [TASK-271](EPIC-014-code-review-remediation/TASK-271-timescaledb-emitters-can-now-use-the-connector.md) The TimescaleDB migration emitters bypass the connector for a reason that no longer exists · FEATURE-014
    - [ ] [TASK-272](EPIC-014-code-review-remediation/TASK-272-first-class-schema-support.md) An entity cannot say which schema it lives in · FEATURE-014
    - [x] [TASK-273](EPIC-014-code-review-remediation/TASK-273-compositeindex-cannot-express-a-filter-predicate.md) `CompositeIndex` cannot express a filter predicate, so a unique index over a NULLABLE column is unusable on MSSql · FEATURE-014
    - [ ] [TASK-274](EPIC-014-code-review-remediation/TASK-274-second-index-lane-drops-sparse.md) The second index lane silently drops `Sparse` — `IIndexBuilder.Sparse()` is `=> this` in all six schema builders · FEATURE-014
    - [ ] [TASK-275](EPIC-014-code-review-remediation/TASK-275-uniquefield-inline-constraint-nullable-column.md) `[UniqueField]` on a nullable column is an inline constraint, so on MSSql it rejects the second ordinary row · FEATURE-014
    - [ ] [TASK-276](EPIC-014-code-review-remediation/TASK-276-unidentified-flake-in-the-sql-offline-suite.md) One test in `Birko.Data.SQL.Tests` fails about 10% of full-suite runs, identity never captured · FEATURE-014
    - [x] [TASK-277](EPIC-014-code-review-remediation/TASK-277-sqlite-swallows-a-write-to-a-missing-table.md) A write to a missing table reports SUCCESS on every provider — `OnException` swallows it · FEATURE-014
    - [x] [TASK-278](EPIC-014-code-review-remediation/TASK-278-mssql-limited-reads-emit-invalid-tsql.md) On SQL Server every limited read emits invalid T-SQL — `ReadFirstAsync` and paging both fail · FEATURE-014
    - [ ] [TASK-258](EPIC-014-code-review-remediation/TASK-258-retrywhenowned-preserves-nothing-that-is-asserted.md) `retryWhenOwned`'s "preserves each provider's policy" is an argument, not a measurement · FEATURE-014
    - [ ] [TASK-259](EPIC-014-code-review-remediation/TASK-259-schema-builder-publishes-its-connection-onto-a-cached-connector.md) `SqlSchemaBuilder` publishes its connection onto a process-wide cached connector and never clears it · FEATURE-014
    - [ ] [TASK-260](EPIC-014-code-review-remediation/TASK-260-continuous-aggregate-takes-raw-sql-fragments.md) `CreateContinuousAggregate` takes two raw SQL fragments that cannot be contained · FEATURE-014
    - [ ] [TASK-261](EPIC-014-code-review-remediation/TASK-261-getchunkinterval-reads-a-column-timescaledb-2-removed.md) `GetChunkInterval` reads a catalogue column TimescaleDB removed in 2.0 · FEATURE-014
    - [ ] [TASK-262](EPIC-014-code-review-remediation/TASK-262-migration-emitters-assume-framework-created-objects.md) The migration emitters' identifier rules assume this framework created the object · FEATURE-014
    - [x] [TASK-263](EPIC-014-code-review-remediation/TASK-263-no-way-to-persist-an-instant-with-its-offset.md) There is no way to persist an instant with its offset — the tz-aware column type is mapped but unreachable · FEATURE-014
- **EPIC-015** Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web — in-progress (10/22 tasks done)
  - **STORY-029** Tier 0 — single-source design tokens + multi-target generator — done (0/0) (done)
  - **STORY-030** Tier 0 — Avalonia theme system + runtime ThemeVariant swap — done (0/0) (done)
  - **STORY-031** Tier 0 validation — Avalonia gallery app + first restyled controls — done (0/0) (done)
  - **STORY-032** Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free) — done (0/0) (done)
  - **STORY-033** Building blocks — schema-driven Form, Drawer, SplitPanel — done (0/0) (done)
  - **STORY-034** Tier 1 — restyled native controls (~20) — done (0/0) (done)
  - **STORY-035** Tier 2 — composite controls with no native peer — done (0/0) (done)
  - **STORY-036** Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation — done (0/0) (done)
  - **STORY-048** Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack — planned (0/5)
    - [ ] [TASK-092](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-092-bump-avalonia-12-net10-xunit-v3.md) Bump Birko.Xaml to Avalonia 12.1.0 / `net10.0` + xunit v3 (Kanban DataTransfer, focus event) · FEATURE-015
    - [ ] [TASK-093](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-093-livecharts-avalonia-12-story.md) Decide the LiveCharts story for Avalonia 12 (the only blocker on the bump) · FEATURE-015
    - [ ] [TASK-095](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-095-avalonia-screenshot-baseline-gate.md) Screenshot baseline gate for the Avalonia suite (build it *before* the Av12 bump) · FEATURE-015
    - [ ] [TASK-096](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-096-consumer-repo-avalonia-12-rollout.md) Roll Avalonia 12 out to consumer repos in lockstep · FEATURE-015
    - [ ] [TASK-094](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-094-avalonia-12-obsolete-warning-sweep.md) Clear the 28 Avalonia 12 obsolete warnings (`Watermark`, `Bitmap.Save`) · FEATURE-015
  - **STORY-049** Office-style ribbon overflow — progressive group scaling + group-to-popup collapse — done (4/4) (done)
    - [x] [TASK-097](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-097-make-ribbon-overflow-reachable.md) Make the existing ribbon overflow reachable (interim fix, both skins) · FEATURE-015
    - [x] [TASK-098](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-098-ribbon-size-variant-scaling-priority-model.md) Ribbon model + tokens: size variant, scaling priority, group icon (XAML **and** web together) · FEATURE-015
    - [x] [TASK-099](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-099-progressive-group-scaling-degrade-pass.md) The degrade pass — measure and scale groups Large → Medium → Small in priority order · FEATURE-015
    - [x] [TASK-100](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-100-group-collapse-to-popup.md) Group-collapse-to-popup — the chunk button and its flyout · FEATURE-015
  - **STORY-056** Mixed per-item size variants within one ribbon group — planned (0/6)
    - [ ] [TASK-119](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-119-decide-mixed-size-model.md) Decide the mixed-size model: per-item degrade order, or fixed group templates · FEATURE-015
    - [ ] [TASK-120](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-120-mixed-size-model-both-skins.md) The mixed-size model, in both skins, with its tokens · FEATURE-015
    - [ ] [TASK-121](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-121-reformulate-scaling-ladder-for-mixed-groups.md) Reformulate the degrade ladder for mixed-size groups · FEATURE-015
    - [ ] [TASK-122](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-122-render-mixed-columns-both-skins.md) Render mixed columns — the CSS grid and the Avalonia panel · FEATURE-015
    - [ ] [TASK-123](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-123-panel-height-under-mixed-sizes.md) Panel height under mixed sizes, and extending the clipping guard · FEATURE-015
    - [ ] [TASK-124](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-124-stale-ribbongroupsize-parity-comment.md) The `RibbonGroupSize` doc comment describes a parity gap that no longer exists · FEATURE-015
  - _tracked directly on the epic_
    - [x] [TASK-055](EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md) Xaml Form field-type parity with b-form (wire existing controls + FormField props) · FEATURE-015
    - [x] [TASK-056](EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md) Xaml date & time picker controls + field types · FEATURE-015
    - [x] [TASK-057](EPIC-015-birko-xaml-ui-framework/TASK-057-xaml-form-multiselect-tags-file.md) Xaml Form field types: MultiSelect / Tags / File · FEATURE-015
    - [x] [TASK-101](EPIC-015-birko-xaml-ui-framework/TASK-101-avalonia-ribbon-pinned-temporary-reveal.md) Avalonia `Ribbon`: pinned vs temporary-reveal collapse, to match `b-ribbon` and Office · FEATURE-015
    - [x] [TASK-102](EPIC-015-birko-xaml-ui-framework/TASK-102-avalonia-ribbon-narrow-fallback.md) Avalonia `Ribbon`: a narrow fallback, mirroring `b-ribbon`'s hamburger · FEATURE-015
    - [ ] [TASK-103](EPIC-015-birko-xaml-ui-framework/TASK-103-focus-visual-for-all-avalonia-buttons.md) Every Avalonia control needs a focus visual — `Buttons.axaml` has none · FEATURE-015
    - [x] [TASK-054](EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md) Xaml restyled Slider (Tier-1 gap) + `Range` Form field type · FEATURE-015
- **EPIC-016** Birko framework backports from Reps (+ cross-provider & Xaml follow-ups) — in-progress (12/14 tasks done)
  - **STORY-037** Backend / SQL framework backports (shipped) — done (0/0) (done)
  - **STORY-038** Frontend Birko.Web backports (shipped) — done (0/0) (done)
  - **STORY-039** Cross-provider SQL store-factory + DI backport — in-progress (1/2)
    - [ ] [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL 🔍 review · FEATURE-016
    - [x] [TASK-051](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md) FIX: MSSqlStore.SetSettings drops connection fields (lossy) · FEATURE-016
  - **STORY-040** Web → Xaml UI / offline / device backports — done (6/6) (done)
    - [x] [TASK-043](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md) Xaml mobile app-shell (BMobileAppShell equivalent) · FEATURE-016
    - [x] [TASK-044](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md) Formatter for Birko.Xaml.Core (duration + culture-aware) · FEATURE-016
    - [x] [TASK-045](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-045-xaml-wake-lock.md) Xaml wake-lock device abstraction (IWakeLock) · FEATURE-016
    - [x] [TASK-046](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-046-xaml-offline-mirror.md) Xaml offline read-through mirror (MirrorStore / readThrough concept) · FEATURE-016
    - [x] [TASK-047](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-047-xaml-sync-status-indicator.md) Xaml sync-status indicator (offline / syncing / synced) · FEATURE-016
    - [x] [TASK-048](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-048-xaml-audio-cue.md) Xaml audio-cue device util (beep + vibrate) · FEATURE-016
  - **STORY-041** BMobileAppShell showcase / placement — done (2/2) (done)
    - [x] [TASK-049](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-049-bmobileappshell-playground-placement.md) BMobileAppShell — better placement / demo in Birko.Web.Playground · FEATURE-016
    - [x] [TASK-050](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-050-bmobileappshell-xaml-gallery.md) BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery · FEATURE-016
  - **STORY-052** Component gaps found by consumers adopting the `b-*` catalogue — in-progress (3/4)
    - [ ] [TASK-135](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-135-b-input-decimal-comma-locale-mode.md) `b-input type="decimal"`: comma-locale decimal entry, owned by the component 🔍 review · FEATURE-016
    - [x] [TASK-107](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-107-b-button-tap-target-and-form-participation.md) `b-button`: a reachable tap target, and form participation · FEATURE-016
    - [x] [TASK-104](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-104-b-chart-small-chart-axis-polish.md) `b-chart`: axis polish for small charts (tick density, nice scale, latest-value overlay, threshold labels) · FEATURE-016
    - [x] [TASK-105](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-105-b-card-padding-md-and-shadow-token.md) `b-card`: the missing `md` padding rung, and elevation as a token · FEATURE-016
- **EPIC-017** Tenant isolation hardening — in-progress (0/1 tasks done)
  - **STORY-044** Opt-in strict (fail-closed) tenancy mode — done (0/0) (done)
  - **STORY-045** Fix decorator ordering so per-tenant uniqueness probes are tenant-scoped — done (0/0) (done)
  - **STORY-046** Restore ambient (tenant) scope for background event dispatch — in-progress (0/1)
    - [ ] [TASK-148](EPIC-017-tenant-isolation-hardening/STORY-046-event-scope-restoration/TASK-148-scope-restoration-pipeline-behavior.md) `ScopeRestorationBehavior` for the distributed-consumer dispatch path ⚠ blocked · FEATURE-017
- **EPIC-018** Birko.Web.Core — the browser-side runtime — in-progress (4/4 tasks done)
  - _tracked directly on the epic_
    - [x] [TASK-198](EPIC-018-birko-web-core-runtime/TASK-198-fetch-has-no-timeout-so-a-dead-connection-hangs-forever.md) `fetch` has no timeout, so a dead connection hung the app forever — and a stalled body reported success · FEATURE-018
    - [x] [TASK-199](EPIC-018-birko-web-core-runtime/TASK-199-syncmanager-misreads-a-write-that-already-landed.md) `SyncManager` had no name for a write that had already landed · FEATURE-018
    - [x] [TASK-202](EPIC-018-birko-web-core-runtime/TASK-202-apiclient-get-corrupted-an-inline-query-string.md) `ApiClient.get` corrupted any endpoint that already carried a query string · FEATURE-018
    - [x] [TASK-203](EPIC-018-birko-web-core-runtime/TASK-203-nothing-recorded-and-never-synced-both-read-as-empty.md) "nothing recorded" and "never synced" both read as `[]` · FEATURE-018

## Loose tasks

> No parent epic. 17 task(s) — see the drift note above.

- [x] [TASK-036](_loose/TASK-036-workspace-reorg-birko-framework-consumers-buckets.md) Reorganize C:\Source into Birko/{Framework,Framework.Tests,Consumers} + aicode bucket
- [ ] [TASK-130](_loose/TASK-130-theme-contrast-scanner-gate.md) Scan every shipped theme for colour contrast, and gate it like the drift check
- [ ] [TASK-140](_loose/TASK-140-resolve-module-from-hash-ignores-the-route-table.md) `resolveModuleFromHash` derives the module positionally and never consults the route table
- [ ] [TASK-200](_loose/TASK-200-symbio-outbox-replay-duplicates-a-create.md) Symbio: an outbox replay duplicates a create, and TASK-151 scoped the cause out of itself
- [ ] [TASK-127](_loose/TASK-127-all-tenants-scope-and-ambient-tenant-decision.md) Decide what `WithAllTenants` means when a tenant is also in scope
- [ ] [TASK-138](_loose/TASK-138-readasync-zero-arg-overload-ambiguity.md) `ReadAsync()` with no arguments does not compile — CS0121 between the read-all and filtered overloads
- [ ] [TASK-139](_loose/TASK-139-coarse-pointer-policy-vs-knob-in-the-component-catalogue.md) Decide whether a `pointer: coarse` rule inside a `b-*` component is policy or a knob
- [ ] [TASK-142](_loose/TASK-142-spec-map-coverage-audit.md) The spec map silently under-covers, and nothing detects it
- [ ] [TASK-143](_loose/TASK-143-public-crud-overrides-defeat-base-guards.md) Stores that override public CRUD instead of `*Core` defeat every base-class guard
- [ ] [TASK-145](_loose/TASK-145-document-the-decorator-stripping-escape-hatch.md) Nothing at the `GetUnwrappedStore` call sites says they strip every decorator
- [ ] [TASK-147](_loose/TASK-147-attachtag-does-not-validate-tag-ownership.md) `AttachTagAsync` validates neither a tag's existence nor its ownership
- [ ] [TASK-149](_loose/TASK-149-story-level-tracking-is-invisible-to-every-scheduler.md) A story that tracks work without task files is invisible to every scheduler
- [ ] [TASK-201](_loose/TASK-201-reps-declare-idpinned-on-client-minted-creates.md) Reps: declare `idPinned` on the client-minted creates — and not on the one that must not have it 🔍 review
- [ ] [TASK-206](_loose/TASK-206-hybrid-l2-fallback-cannot-tell-misconfiguration-from-outage.md) `HybridCache`'s L2 fallback filter cannot tell a misconfiguration from an outage
- [ ] [TASK-059](_loose/TASK-059-nested-projitems-import-convention-decision.md) Decide the long-term convention for nested `.projitems` imports (MSB4011)
- [ ] [TASK-106](_loose/TASK-106-css-part-as-a-catalogue-convention-decision.md) Decide whether `::part` is a catalogue convention or stays a one-off
- [ ] [TASK-235](_loose/TASK-235-fisdata-angular-will-hit-netsdk1087-on-migration.md) `FisData.Stock.Angular.Server` will fail `NETSDK1087` when its net10 migration lands ⚠ blocked


<details>
<summary><strong>Completed epics (1)</strong></summary>

- **EPIC-009** Birko.Communication — Remaining protocols — done (2/2 tasks done)
  - **STORY-019** gRPC support — done (1/1) (done)
    - [x] [TASK-026](EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md) gRPC client + server support · FEATURE-009
  - **STORY-020** OAuth2 authorization server — done (1/1) (done)
    - [x] [TASK-027](EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md) Implement Birko.Security.OAuth.Server · FEATURE-009

</details>

