# Tasks — Birko.Framework

_Generated 2026-07-06. Run `/tasks triage` to refresh. **Do not hand-edit** — changes will be overwritten._

## Counts

| Status       | Epics | Stories | Tasks |
|--------------|-------|---------|-------|
| planned      | 11    | 25      | —     |
| todo         | —     | —       | 34    |
| in-progress  | 3     | 2       | 1     |
| review       | —     | —       | 5     |
| blocked      | —     | —       | 0     |
| done         | 2     | 14      | 16    |
| cancelled    | 0     | 0       | 0     |

## In progress now

- [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: gallery + live token editor + theme-CSS export (P2, ai)

## In review

- [TASK-036](_loose/TASK-036-workspace-reorg-birko-framework-consumers-buckets.md) Reorganize C:\Source into Birko/{Framework,Web,Framework.Tests,Consumers} + aicode bucket (P1, ai) — framework build green; Symbio build sign-off pending
- [TASK-041](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-041-shared-coerce-css-length.md) Shared coerceCssLength helper + fix unitless-length across 6 components (P2, ai) — landed in Birko.Web.Core; playground bundles green
- [TASK-039](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-039-b-chart-coerce-unitless-height.md) b-chart: coerce unitless height — avoid endless SVG stretch (P3, ai) — component fix landed; in-browser playground check pending
- [TASK-040](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-040-b-accordion-component.md) Add a b-accordion (collapsible / disclosure group) component (P2, ai) — built + in playground gallery; in-browser interaction check pending
- [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Cross-provider SQL store-factory + DI (P2, ai) — offline tests green; live CRUD round-trip deferred (Docker later)

## Tree

- **EPIC-001** [Birko.Web.Components — UI polish](EPIC-001-web-components-ui-polish/EPIC.md) — in-progress (1/8 tasks done)
  - [x] [TASK-053](EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md) b-range: vertical orientation (equalizer slider) (P3, ai) — **done**
  - STORY-001 [bare attribute for inline form usage](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/STORY.md) — planned (0/1)
    - [ ] [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add bare attribute to all form controls (P2, ai)
  - STORY-002 [b-editable-table migration](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/STORY.md) — planned (0/1)
    - [ ] [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table (P2, ai)
  - STORY-003 [size attribute coverage](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/STORY.md) — planned (0/1)
    - [ ] [TASK-003](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/TASK-003-size-on-pagination-dropdown-breadcrumb.md) size on b-pagination, b-dropdown-menu, b-breadcrumb (P2, ai)
  - STORY-023 [form-associated custom elements (ElementInternals)](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/STORY.md) — planned (0/1)
    - [ ] [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals (P3, ai)
  - STORY-028 [display & disclosure components](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/STORY.md) — in-progress (0/3)
    - [ ] [TASK-039](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-039-b-chart-coerce-unitless-height.md) b-chart: coerce unitless height — avoid endless SVG stretch (P3, ai) 🔍 review
    - [ ] [TASK-040](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-040-b-accordion-component.md) Add a b-accordion (collapsible / disclosure group) component (P2, ai) 🔍 review
    - [ ] [TASK-041](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-041-shared-coerce-css-length.md) Shared coerceCssLength helper + fix unitless-length across 6 components (P2, ai) 🔍 review
- **EPIC-002** [Birko.Data.Redis](EPIC-002-birko-data-redis/EPIC.md) — planned (0/1)
  - [ ] [TASK-004](EPIC-002-birko-data-redis/TASK-004-implement-birko-data-redis.md) Implement Birko.Data.Redis (P2, ai)
- **EPIC-003** [Birko.Caching.NCache](EPIC-003-birko-caching-ncache/EPIC.md) — planned (0/1)
  - [ ] [TASK-005](EPIC-003-birko-caching-ncache/TASK-005-implement-birko-caching-ncache.md) Implement Birko.Caching.NCache (P2, ai)
- **EPIC-004** [Birko.Storage — Cloud providers](EPIC-004-storage-cloud-providers/EPIC.md) — planned (0/3)
  - STORY-004 [AWS S3 storage](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/STORY.md) — planned (0/1)
    - [ ] [TASK-006](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/TASK-006-birko-storage-aws.md) Implement Birko.Storage.Aws (P1, ai)
  - STORY-005 [Google Cloud Storage](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/STORY.md) — planned (0/1)
    - [ ] [TASK-007](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/TASK-007-birko-storage-google.md) Implement Birko.Storage.Google (P2, ai)
  - STORY-006 [MinIO](EPIC-004-storage-cloud-providers/STORY-006-minio/STORY.md) — planned (0/1)
    - [ ] [TASK-008](EPIC-004-storage-cloud-providers/STORY-006-minio/TASK-008-birko-storage-minio.md) Implement Birko.Storage.Minio (P2, ai)
- **EPIC-005** [Birko.Messaging — Provider expansion](EPIC-005-messaging-provider-expansion/EPIC.md) — planned (0/5)
  - STORY-007 [Email providers](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/STORY.md) — planned (0/2)
    - [ ] [TASK-009](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-009-birko-messaging-sendgrid.md) Implement Birko.Messaging.SendGrid (P1, ai)
    - [ ] [TASK-010](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-010-birko-messaging-mailgun.md) Implement Birko.Messaging.Mailgun (P2, ai)
  - STORY-008 [SMS via Twilio](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/STORY.md) — planned (0/1)
    - [ ] [TASK-011](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/TASK-011-birko-messaging-twilio.md) Implement Birko.Messaging.Twilio (P1, ai)
  - STORY-009 [Push notifications](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/STORY.md) — planned (0/2)
    - [ ] [TASK-012](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-012-birko-messaging-firebase.md) Implement Birko.Messaging.Firebase (P2, ai)
    - [ ] [TASK-013](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-013-birko-messaging-apple.md) Implement Birko.Messaging.Apple (P2, ai)
- **EPIC-006** [Birko.MessageQueue — Provider expansion](EPIC-006-messagequeue-provider-expansion/EPIC.md) — planned (0/5)
  - STORY-010 [RabbitMQ](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/STORY.md) — planned (0/1)
    - [ ] [TASK-014](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/TASK-014-birko-messagequeue-rabbitmq.md) Implement Birko.MessageQueue.RabbitMQ (P1, ai)
  - STORY-011 [Kafka](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/STORY.md) — planned (0/1)
    - [ ] [TASK-015](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/TASK-015-birko-messagequeue-kafka.md) Implement Birko.MessageQueue.Kafka (P1, ai)
  - STORY-012 [Cloud queue providers](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/STORY.md) — planned (0/2)
    - [ ] [TASK-016](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-016-birko-messagequeue-azure.md) Implement Birko.MessageQueue.Azure (P2, ai)
    - [ ] [TASK-017](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-017-birko-messagequeue-aws.md) Implement Birko.MessageQueue.Aws (P2, ai)
  - STORY-013 [MassTransit adapter](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/STORY.md) — planned (0/1)
    - [ ] [TASK-018](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/TASK-018-birko-messagequeue-masstransit.md) Implement Birko.MessageQueue.MassTransit (P2, ai)
- **EPIC-007** [Birko.Telemetry — Additional exporters](EPIC-007-telemetry-exporters/EPIC.md) — planned (0/3)
  - STORY-014 [Prometheus exporter](EPIC-007-telemetry-exporters/STORY-014-prometheus/STORY.md) — planned (0/1)
    - [ ] [TASK-019](EPIC-007-telemetry-exporters/STORY-014-prometheus/TASK-019-birko-telemetry-prometheus.md) Implement Birko.Telemetry.Prometheus (P2, ai)
  - STORY-015 [Seq log exporter](EPIC-007-telemetry-exporters/STORY-015-seq/STORY.md) — planned (0/1)
    - [ ] [TASK-020](EPIC-007-telemetry-exporters/STORY-015-seq/TASK-020-birko-telemetry-seq.md) Implement Birko.Telemetry.Seq (P2, ai)
  - STORY-016 [Grafana LGTM stack](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/STORY.md) — planned (0/1)
    - [ ] [TASK-021](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/TASK-021-birko-telemetry-grafana.md) Implement Birko.Telemetry.Grafana (P2, ai)
- **EPIC-008** [Birko.Health — Queue + cloud health checks](EPIC-008-health-mq-cloud-checks/EPIC.md) — planned (0/4)
  - STORY-017 [MQ health checks](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-022](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-022-rabbitmq-health-check.md) RabbitMqHealthCheck (P2, ai)
    - [ ] [TASK-023](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-023-kafka-health-check.md) KafkaHealthCheck (P2, ai)
  - STORY-018 [Cloud queue health checks](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-024](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-024-azure-service-bus-health-check.md) AzureServiceBusHealthCheck (P2, ai)
    - [ ] [TASK-025](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-025-aws-sqs-health-check.md) AwsSqsHealthCheck (P2, ai)
- **EPIC-010** [Birko.Data.RavenDB — Index ergonomics](EPIC-010-ravendb-index-ergonomics/EPIC.md) — planned (0/1)
  - [ ] [TASK-028](EPIC-010-ravendb-index-ergonomics/TASK-028-attribute-driven-raven-indexes.md) Attribute-driven RavenDB index definitions (P2, ai)
- **EPIC-011** [Birko.Framework — Test coverage gaps](EPIC-011-test-coverage-gaps/EPIC.md) — planned (0/6)
  - [ ] [TASK-052](EPIC-011-test-coverage-gaps/TASK-052-birko-web-unit-test-runner.md) Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke) (P2, ai)
  - STORY-021 [Redis-dependent tests](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/STORY.md) — planned (0/2)
    - [ ] [TASK-029](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-029-backgroundjobs-redis-tests.md) Birko.BackgroundJobs.Redis.Tests (P2, ai)
    - [ ] [TASK-030](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-030-caching-redis-tests.md) Birko.Caching.Redis.Tests (P2, ai)
  - STORY-022 [Phase 4 lower-priority tests](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/STORY.md) — planned (0/3)
    - [ ] [TASK-031](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-031-models-validation-tests.md) Birko.Models.* validation tests (P2, ai)
    - [ ] [TASK-032](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-032-viewmodel-crud-tests.md) Birko.Data.*.ViewModel CRUD tests (P2, ai)
    - [ ] [TASK-033](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-033-configuration-contracts-tests.md) Birko.Configuration + Birko.Contracts DTO tests (P2, ai)
- **EPIC-012** [Birko.MessageQueue.MQTT — v5 features](EPIC-012-mqtt-v5-features/EPIC.md) — planned (0/1)
  - [ ] [TASK-034](EPIC-012-mqtt-v5-features/TASK-034-mqtt-v5-topic-aliases-user-properties.md) MQTT v5 topic aliases + user properties (P2, ai)
- **EPIC-013** [Reference consumers — smoke harness + Web playground](EPIC-013-reference-consumers/EPIC.md) — in-progress (1/2)
  - [x] [TASK-037](EPIC-013-reference-consumers/TASK-037-extract-backend-smoke-harness-consumer.md) Replace TUI example with extracted backend smoke-harness consumer (P2, ai) — **done**
  - [ ] [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: gallery + live token editor + theme-CSS export (P2, ai) ← in-progress
- **EPIC-014** [Code review — audit remediation](EPIC-014-code-review-remediation/EPIC.md) — planned (0/4 stories; 864 findings, tasks extracted on demand from CODE-REVIEW-AUDIT-2026-06-17.md)
  - STORY-024 [Critical findings (24)](EPIC-014-code-review-remediation/STORY-024-critical-findings/STORY.md) — planned (CR-C01 … CR-C24)
  - STORY-025 [High findings (147)](EPIC-014-code-review-remediation/STORY-025-high-findings/STORY.md) — planned (CR-H001 … CR-H147)
  - STORY-026 [Medium findings (275)](EPIC-014-code-review-remediation/STORY-026-medium-findings/STORY.md) — planned (CR-M001 …)
  - STORY-027 [Low findings (418)](EPIC-014-code-review-remediation/STORY-027-low-findings/STORY.md) — planned (CR-L001 …)
- **EPIC-015** [Birko.Xaml — Avalonia-first XAML UI framework](EPIC-015-birko-xaml-ui-framework/EPIC.md) — done (8 stories + 3 follow-up controls: Slider, field-type parity, date/time pickers)
  - [x] [TASK-054](EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md) Restyled Slider (Tier-1 gap) + Range Form field type (P3, ai) — **done**
  - [x] [TASK-055](EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md) Form field-type parity with b-form: wire existing controls + FormField props (P2, ai) — **done**
  - [x] [TASK-056](EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md) Date & time picker controls + field types (P2, ai) — **done**
  - STORY-029 [Tier 0 — single-source design tokens + multi-target generator](EPIC-015-birko-xaml-ui-framework/STORY-029-design-tokens-generator/STORY.md) — done (Birko.DesignTokens: byte-identical CSS + AXAML, 30 tests)
  - STORY-030 [Tier 0 — Avalonia theme system + runtime ThemeVariant swap](EPIC-015-birko-xaml-ui-framework/STORY-030-avalonia-theme-system/STORY.md) — done (Birko.Xaml.Core + .Avalonia: ThemeDictionaries + runtime swap, 8 headless tests)
  - STORY-031 [Tier 0 validation — Avalonia gallery app + first restyled controls](EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md) — done (gate GO: Button/TextBox/Card/Badge + gallery, 15 tests + per-theme screenshots)
  - STORY-031 [Tier 0 validation — Avalonia gallery app + first restyled controls](EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md) — planned
  - STORY-032 [Birko.Xaml.Core — i18n + base ViewModels (Avalonia-free)](EPIC-015-birko-xaml-ui-framework/STORY-032-xaml-core-foundation/STORY.md) — done (i18n + {l:Tr} + base VMs + CRUD port, 15+1 tests, Avalonia-free enforced)
  - STORY-033 [Building blocks — schema-driven Form, Drawer, SplitPanel](EPIC-015-birko-xaml-ui-framework/STORY-033-building-blocks-form-drawer-splitpanel/STORY.md) — done (Form + Drawer + SplitPanel, 8 tests + gallery)
  - STORY-034 [Tier 1 — restyled native controls (~20)](EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md) — done (all ~20 Tier-1 controls incl. Modal + DataGrid; 45 tests + gallery)
  - STORY-035 [Tier 2 — composite controls with no native peer](EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md) — done (all 7: tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart)
  - STORY-036 [Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation](EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md) — done (dual chrome sidebar+ribbon, nav, page bases, Ctrl+K palette, user/tenant, FormModal, transitions)
- **EPIC-016** [Birko framework backports from Reps (+ cross-provider & Xaml follow-ups)](EPIC-016-birko-backports-from-reps/EPIC.md) — in-progress (migrated from Consumers/WorkoutTracker EPIC-002; 2/5 stories done)
  - STORY-037 [Backend / SQL framework backports](EPIC-016-birko-backports-from-reps/STORY-037-backend-sql-backports/STORY.md) — done (shipped; compact ledger, detail in Reps)
  - STORY-038 [Frontend Birko.Web backports](EPIC-016-birko-backports-from-reps/STORY-038-frontend-web-backports/STORY.md) — done (shipped; compact ledger, detail in Reps)
  - STORY-039 [Cross-provider SQL store-factory + DI backport](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/STORY.md) — in-progress (0/1; 1 in review)
    - [ ] [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai) 🔍 review — live CRUD env-gated
    - [x] [TASK-051](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md) FIX: MSSqlStore.SetSettings drops connection fields (P2, ai) — **done**
  - STORY-040 [Web → Xaml UI / offline / device backports](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/STORY.md) — done (6/6)
    - [x] [TASK-043](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md) Xaml mobile app-shell (BMobileAppShell equivalent) (P2, ai) — **done**
    - [x] [TASK-044](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md) Formatter for Birko.Xaml.Core (duration + culture-aware) (P2, ai) — **done**
    - [x] [TASK-045](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-045-xaml-wake-lock.md) Xaml wake-lock device abstraction (IWakeLock) (P3, ai) — **done**
    - [x] [TASK-046](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-046-xaml-offline-mirror.md) Xaml offline read-through mirror (MirrorDataSource) (P3, ai) — **done**
    - [x] [TASK-047](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-047-xaml-sync-status-indicator.md) Xaml sync-status indicator — offline/syncing/synced (P3, ai) — **done**
    - [x] [TASK-048](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-048-xaml-audio-cue.md) Xaml audio-cue device util (beep + vibrate) (P3, ai) — **done**
  - STORY-041 [BMobileAppShell showcase / placement](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/STORY.md) — done (2/2)
    - [x] [TASK-049](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-049-bmobileappshell-playground-placement.md) BMobileAppShell — better placement / demo in Birko.Web.Playground (P2, ai) — **done**
    - [x] [TASK-050](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-050-bmobileappshell-xaml-gallery.md) BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery (P3, ai) — **done**

### Loose tasks (no parent epic)

- [ ] [TASK-036](_loose/TASK-036-workspace-reorg-birko-framework-consumers-buckets.md) Reorganize C:\Source into Birko/{Framework,Web,Framework.Tests,Consumers} + aicode bucket (P1, ai) 🔍 review

<details>
<summary>Completed (1 epic)</summary>

- **EPIC-009** [Birko.Communication — Remaining protocols](EPIC-009-communication-protocols/EPIC.md) — done (2/2)
  - STORY-019 [gRPC support](EPIC-009-communication-protocols/STORY-019-grpc/STORY.md) — done (1/1)
    - [x] [TASK-026](EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md) gRPC client + server support (P2, ai)
  - STORY-020 [OAuth2 server](EPIC-009-communication-protocols/STORY-020-oauth2-server/STORY.md) — done (1/1)
    - [x] [TASK-027](EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md) Implement Birko.Security.OAuth.Server (P2, ai)

</details>
