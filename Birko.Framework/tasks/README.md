# Tasks — Birko.Framework

_Generated 2026-07-29. Run `/tasks triage` to refresh. **Do not hand-edit** — changes will be overwritten._

## Counts

| Status       | Epics              | Stories            | Tasks               |
|--------------|--------------------|--------------------|---------------------|
| planned      | 10                 | 25                 | —                   |
| todo         | —                  | —                  | 38                  |
| in-progress  | 6                  | 5                  | 2                   |
| review       | —                  | —                  | 8                   |
| blocked      | —                  | —                  | 0                   |
| done         | 1                  | 21                 | 24                  |
| cancelled    | 0                  | 0                  | 0                   |

## In progress now

- [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: component gallery + live token editor + theme-CSS export (P2, ai)

## In review (awaiting sign-off)

- [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add `bare` attribute to all form controls (P2, ai)
- [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table to bare components (P2, ai)
- [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai)
- [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) `description` — a persistent help-text row on the form controls (P2, ai)
- [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals (P3, ai)

## Tree

- **EPIC-001** [Birko.Web.Components — UI polish](EPIC-001-web-components-ui-polish/EPIC.md) — in-progress (4/9 tasks done)
  - [x] [TASK-053](EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md) b-range: vertical orientation (equalizer-style slider) (P3, ai) — **done**
  - STORY-001 [bare attribute for inline form usage](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/STORY.md) — planned (0/1)
    - [ ] [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add `bare` attribute to all form controls (P2, ai) 🔍 review
  - STORY-002 [b-editable-table migration to bare components](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/STORY.md) — planned (0/1)
    - [ ] [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table to bare components (P2, ai) 🔍 review
  - STORY-003 [size attribute coverage](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/STORY.md) — planned (0/1)
    - [ ] [TASK-003](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/TASK-003-size-on-pagination-dropdown-breadcrumb.md) size attribute on b-pagination, b-dropdown-menu, b-breadcrumb (P2, ai)
  - STORY-023 [Form-associated custom elements (ElementInternals)](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/STORY.md) — planned (0/1)
    - [ ] [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals (P3, ai) 🔍 review
  - STORY-028 [Display & disclosure components](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/STORY.md) — done (3/3)
    - [x] [TASK-039](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-039-b-chart-coerce-unitless-height.md) b-chart: coerce/validate a unitless `height` (avoid endless SVG stretch) (P3, ai) — **done**
    - [x] [TASK-040](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-040-b-accordion-component.md) Add a `b-accordion` (collapsible / disclosure group) component (P2, ai) — **done**
    - [x] [TASK-041](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-041-shared-coerce-css-length.md) Extract a shared `coerceCssLength` helper and fix the unitless-length bug across components (P2, ai) — **done**
  - STORY-050 [Visible help text on form controls](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/STORY.md) — in-progress (0/1)
    - [ ] [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) `description` — a persistent help-text row on the form controls (P2, ai) 🔍 review
- **EPIC-002** [Birko.Data.Redis](EPIC-002-birko-data-redis/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-004](EPIC-002-birko-data-redis/TASK-004-implement-birko-data-redis.md) Implement Birko.Data.Redis (P2, ai)
- **EPIC-003** [Birko.Caching.NCache](EPIC-003-birko-caching-ncache/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-005](EPIC-003-birko-caching-ncache/TASK-005-implement-birko-caching-ncache.md) Implement Birko.Caching.NCache (P2, ai)
- **EPIC-004** [Birko.Storage — Cloud providers](EPIC-004-storage-cloud-providers/EPIC.md) — planned (0/3 tasks done)
  - STORY-004 [AWS S3 storage](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/STORY.md) — planned (0/1)
    - [ ] [TASK-006](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/TASK-006-birko-storage-aws.md) Implement Birko.Storage.Aws (P1, ai)
  - STORY-005 [Google Cloud Storage](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/STORY.md) — planned (0/1)
    - [ ] [TASK-007](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/TASK-007-birko-storage-google.md) Implement Birko.Storage.Google (P2, ai)
  - STORY-006 [MinIO (S3-compatible)](EPIC-004-storage-cloud-providers/STORY-006-minio/STORY.md) — planned (0/1)
    - [ ] [TASK-008](EPIC-004-storage-cloud-providers/STORY-006-minio/TASK-008-birko-storage-minio.md) Implement Birko.Storage.Minio (P2, ai)
- **EPIC-005** [Birko.Messaging — Provider expansion](EPIC-005-messaging-provider-expansion/EPIC.md) — planned (0/5 tasks done)
  - STORY-007 [Email providers (SendGrid + Mailgun)](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/STORY.md) — planned (0/2)
    - [ ] [TASK-009](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-009-birko-messaging-sendgrid.md) Implement Birko.Messaging.SendGrid (P1, ai)
    - [ ] [TASK-010](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-010-birko-messaging-mailgun.md) Implement Birko.Messaging.Mailgun (P2, ai)
  - STORY-008 [SMS via Twilio](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/STORY.md) — planned (0/1)
    - [ ] [TASK-011](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/TASK-011-birko-messaging-twilio.md) Implement Birko.Messaging.Twilio (P1, ai)
  - STORY-009 [Push notifications (Firebase + APNs)](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/STORY.md) — planned (0/2)
    - [ ] [TASK-012](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-012-birko-messaging-firebase.md) Implement Birko.Messaging.Firebase (P2, ai)
    - [ ] [TASK-013](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-013-birko-messaging-apple.md) Implement Birko.Messaging.Apple (P2, ai)
- **EPIC-006** [Birko.MessageQueue — Provider expansion](EPIC-006-messagequeue-provider-expansion/EPIC.md) — planned (0/5 tasks done)
  - STORY-010 [RabbitMQ (AMQP)](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/STORY.md) — planned (0/1)
    - [ ] [TASK-014](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/TASK-014-birko-messagequeue-rabbitmq.md) Implement Birko.MessageQueue.RabbitMQ (P1, ai)
  - STORY-011 [Kafka](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/STORY.md) — planned (0/1)
    - [ ] [TASK-015](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/TASK-015-birko-messagequeue-kafka.md) Implement Birko.MessageQueue.Kafka (P1, ai)
  - STORY-012 [Cloud queue providers (Azure Service Bus + AWS SQS)](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/STORY.md) — planned (0/2)
    - [ ] [TASK-016](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-016-birko-messagequeue-azure.md) Implement Birko.MessageQueue.Azure (P2, ai)
    - [ ] [TASK-017](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-017-birko-messagequeue-aws.md) Implement Birko.MessageQueue.Aws (P2, ai)
  - STORY-013 [MassTransit adapter](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/STORY.md) — planned (0/1)
    - [ ] [TASK-018](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/TASK-018-birko-messagequeue-masstransit.md) Implement Birko.MessageQueue.MassTransit (P2, ai)
- **EPIC-007** [Birko.Telemetry — Additional exporters](EPIC-007-telemetry-exporters/EPIC.md) — planned (0/3 tasks done)
  - STORY-014 [Prometheus exporter](EPIC-007-telemetry-exporters/STORY-014-prometheus/STORY.md) — planned (0/1)
    - [ ] [TASK-019](EPIC-007-telemetry-exporters/STORY-014-prometheus/TASK-019-birko-telemetry-prometheus.md) Implement Birko.Telemetry.Prometheus (P2, ai)
  - STORY-015 [Seq log exporter](EPIC-007-telemetry-exporters/STORY-015-seq/STORY.md) — planned (0/1)
    - [ ] [TASK-020](EPIC-007-telemetry-exporters/STORY-015-seq/TASK-020-birko-telemetry-seq.md) Implement Birko.Telemetry.Seq (P2, ai)
  - STORY-016 [Grafana LGTM stack exporter](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/STORY.md) — planned (0/1)
    - [ ] [TASK-021](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/TASK-021-birko-telemetry-grafana.md) Implement Birko.Telemetry.Grafana (P2, ai)
- **EPIC-008** [Birko.Health — Queue + cloud health checks](EPIC-008-health-mq-cloud-checks/EPIC.md) — planned (0/4 tasks done)
  - STORY-017 [Message queue health checks](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-022](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-022-rabbitmq-health-check.md) RabbitMqHealthCheck (P2, ai)
    - [ ] [TASK-023](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-023-kafka-health-check.md) KafkaHealthCheck (P2, ai)
  - STORY-018 [Cloud queue health checks](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-024](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-024-azure-service-bus-health-check.md) AzureServiceBusHealthCheck (P2, ai)
    - [ ] [TASK-025](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-025-aws-sqs-health-check.md) AwsSqsHealthCheck (P2, ai)
- **EPIC-010** [Birko.Data.RavenDB — Index ergonomics](EPIC-010-ravendb-index-ergonomics/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-028](EPIC-010-ravendb-index-ergonomics/TASK-028-attribute-driven-raven-indexes.md) Attribute-driven RavenDB index definitions (Option B) (P2, ai)
- **EPIC-011** [Birko.Framework — Test coverage gaps](EPIC-011-test-coverage-gaps/EPIC.md) — planned (0/7 tasks done)
  - [ ] [TASK-052](EPIC-011-test-coverage-gaps/TASK-052-birko-web-unit-test-runner.md) Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke) (P2, ai)
  - STORY-021 [Redis-dependent tests](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/STORY.md) — planned (0/2)
    - [ ] [TASK-029](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-029-backgroundjobs-redis-tests.md) Birko.BackgroundJobs.Redis.Tests (P2, ai)
    - [ ] [TASK-030](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-030-caching-redis-tests.md) Birko.Caching.Redis.Tests (P2, ai)
  - STORY-022 [Phase 4 lower-priority tests](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/STORY.md) — planned (0/3)
    - [ ] [TASK-031](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-031-models-validation-tests.md) Birko.Models.* validation tests (P2, ai)
    - [ ] [TASK-032](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-032-viewmodel-crud-tests.md) Birko.Data.*.ViewModel CRUD tests (P2, ai)
    - [ ] [TASK-033](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-033-configuration-contracts-tests.md) Birko.Configuration + Birko.Contracts DTO tests (P2, ai)
  - STORY-047 [Review filter-parser behaviour on live document databases](EPIC-011-test-coverage-gaps/STORY-047-null-filter-live-parser-review/STORY.md) — planned (0/1)
    - [ ] [TASK-060](EPIC-011-test-coverage-gaps/STORY-047-null-filter-live-parser-review/TASK-060-run-and-review-live-null-tests.md) Run & review the live null-filter parser tests
- **EPIC-012** [Birko.MessageQueue.MQTT — v5 features](EPIC-012-mqtt-v5-features/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-034](EPIC-012-mqtt-v5-features/TASK-034-mqtt-v5-topic-aliases-user-properties.md) MQTT v5 topic aliases + user properties (P2, ai)
- **EPIC-013** [Reference consumers — integration smoke harness + Web playground](EPIC-013-reference-consumers/EPIC.md) — in-progress (1/2 tasks done)
  - [x] [TASK-037](EPIC-013-reference-consumers/TASK-037-extract-backend-smoke-harness-consumer.md) Replace the TUI example with an extracted backend integration smoke-harness consumer (P2, ai) — **done**
  - [ ] [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: component gallery + live token editor + theme-CSS export (P2, ai) ← in-progress
- **EPIC-014** [Code review — audit remediation](EPIC-014-code-review-remediation/EPIC.md) — in-progress (0/0 tasks done)
  - STORY-024 [Critical findings](EPIC-014-code-review-remediation/STORY-024-critical-findings/STORY.md) — done (0/0)
  - STORY-025 [High findings](EPIC-014-code-review-remediation/STORY-025-high-findings/STORY.md) — done (0/0)
  - STORY-026 [Medium findings](EPIC-014-code-review-remediation/STORY-026-medium-findings/STORY.md) — in-progress (0/0)
  - STORY-027 [Low findings](EPIC-014-code-review-remediation/STORY-027-low-findings/STORY.md) — done (0/0)
  - STORY-042 [Integration-test tier — the Docker-gated remediation findings](EPIC-014-code-review-remediation/STORY-042-integration-test-tier/STORY.md) — planned (0/0)
  - STORY-043 [Workflow backends — unify the serialization seam (ISerializer everywhere)](EPIC-014-code-review-remediation/STORY-043-workflow-serializer-seam/STORY.md) — done (0/0)
- **EPIC-015** [Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web](EPIC-015-birko-xaml-ui-framework/EPIC.md) — in-progress (6/15 tasks done)
  - [x] [TASK-054](EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md) Xaml restyled Slider (Tier-1 gap) + `Range` Form field type (P3, ai) — **done**
  - [x] [TASK-055](EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md) Xaml Form field-type parity with b-form (wire existing controls + FormField props) (P2, ai) — **done**
  - [x] [TASK-056](EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md) Xaml date & time picker controls + field types (P2, ai) — **done**
  - [x] [TASK-057](EPIC-015-birko-xaml-ui-framework/TASK-057-xaml-form-multiselect-tags-file.md) Xaml Form field types: MultiSelect / Tags / File (P2, ai) — **done**
  - [ ] [TASK-101](EPIC-015-birko-xaml-ui-framework/TASK-101-avalonia-ribbon-pinned-temporary-reveal.md) Avalonia `Ribbon`: pinned vs temporary-reveal collapse, to match `b-ribbon` and Office (P2, ai) 🔍 review
  - [ ] [TASK-102](EPIC-015-birko-xaml-ui-framework/TASK-102-avalonia-ribbon-narrow-fallback.md) Avalonia `Ribbon`: a narrow fallback, mirroring `b-ribbon`'s hamburger (P2, ai) 🔍 review
  - STORY-029 [Tier 0 — single-source design tokens + multi-target generator](EPIC-015-birko-xaml-ui-framework/STORY-029-design-tokens-generator/STORY.md) — done (0/0)
  - STORY-030 [Tier 0 — Avalonia theme system + runtime ThemeVariant swap](EPIC-015-birko-xaml-ui-framework/STORY-030-avalonia-theme-system/STORY.md) — done (0/0)
  - STORY-031 [Tier 0 validation — Avalonia gallery app + first restyled controls](EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md) — done (0/0)
  - STORY-032 [Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free)](EPIC-015-birko-xaml-ui-framework/STORY-032-xaml-core-foundation/STORY.md) — done (0/0)
  - STORY-033 [Building blocks — schema-driven Form, Drawer, SplitPanel](EPIC-015-birko-xaml-ui-framework/STORY-033-building-blocks-form-drawer-splitpanel/STORY.md) — done (0/0)
  - STORY-034 [Tier 1 — restyled native controls (~20)](EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md) — done (0/0)
  - STORY-035 [Tier 2 — composite controls with no native peer](EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md) — done (0/0)
  - STORY-036 [Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation](EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md) — done (0/0)
  - STORY-048 [Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/STORY.md) — planned (0/5)
    - [ ] [TASK-092](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-092-bump-avalonia-12-net10-xunit-v3.md) Bump Birko.Xaml to Avalonia 12.1.0 / `net10.0` + xunit v3 (Kanban DataTransfer, focus event) (P2, ai)
    - [ ] [TASK-093](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-093-livecharts-avalonia-12-story.md) Decide the LiveCharts story for Avalonia 12 (the only blocker on the bump) (P2, human)
    - [ ] [TASK-094](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-094-avalonia-12-obsolete-warning-sweep.md) Clear the 28 Avalonia 12 obsolete warnings (`Watermark`, `Bitmap.Save`) (P3, ai)
    - [ ] [TASK-095](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-095-avalonia-screenshot-baseline-gate.md) Screenshot baseline gate for the Avalonia suite (build it *before* the Av12 bump) (P2, ai)
    - [ ] [TASK-096](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-096-consumer-repo-avalonia-12-rollout.md) Roll Avalonia 12 out to consumer repos in lockstep (P2, ai)
  - STORY-049 [Office-style ribbon overflow — progressive group scaling + group-to-popup collapse](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/STORY.md) — in-progress (2/4)
    - [x] [TASK-097](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-097-make-ribbon-overflow-reachable.md) Make the existing ribbon overflow reachable (interim fix, both skins) (P1, ai) — **done**
    - [x] [TASK-098](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-098-ribbon-size-variant-scaling-priority-model.md) Ribbon model + tokens: size variant, scaling priority, group icon (XAML **and** web together) (P2, ai) — **done**
    - [ ] [TASK-099](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-099-progressive-group-scaling-degrade-pass.md) The degrade pass — measure and scale groups Large → Medium → Small in priority order (P2, ai) 🔍 review
    - [ ] [TASK-100](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-100-group-collapse-to-popup.md) Group-collapse-to-popup — the chunk button and its flyout (P2, ai) ← in-progress
- **EPIC-016** [Birko framework backports from Reps (+ cross-provider & Xaml follow-ups)](EPIC-016-birko-backports-from-reps/EPIC.md) — in-progress (9/11 tasks done)
  - [ ] [TASK-059](_loose/TASK-059-nested-projitems-import-convention-decision.md) Decide the long-term convention for nested `.projitems` imports (MSB4011) (P3, ai)
  - STORY-037 [Backend / SQL framework backports (shipped)](EPIC-016-birko-backports-from-reps/STORY-037-backend-sql-backports/STORY.md) — done (0/0)
  - STORY-038 [Frontend Birko.Web backports (shipped)](EPIC-016-birko-backports-from-reps/STORY-038-frontend-web-backports/STORY.md) — done (0/0)
  - STORY-039 [Cross-provider SQL store-factory + DI backport](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/STORY.md) — in-progress (1/2)
    - [ ] [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai) 🔍 review
    - [x] [TASK-051](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md) FIX: MSSqlStore.SetSettings drops connection fields (lossy) (P2, ai) — **done**
  - STORY-040 [Web → Xaml UI / offline / device backports](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/STORY.md) — done (6/6)
    - [x] [TASK-043](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md) Xaml mobile app-shell (BMobileAppShell equivalent) (P2, ai) — **done**
    - [x] [TASK-044](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md) Formatter for Birko.Xaml.Core (duration + culture-aware) (P2, ai) — **done**
    - [x] [TASK-045](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-045-xaml-wake-lock.md) Xaml wake-lock device abstraction (IWakeLock) (P3, ai) — **done**
    - [x] [TASK-046](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-046-xaml-offline-mirror.md) Xaml offline read-through mirror (MirrorStore / readThrough concept) (P3, ai) — **done**
    - [x] [TASK-047](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-047-xaml-sync-status-indicator.md) Xaml sync-status indicator (offline / syncing / synced) (P3, ai) — **done**
    - [x] [TASK-048](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-048-xaml-audio-cue.md) Xaml audio-cue device util (beep + vibrate) (P3, ai) — **done**
  - STORY-041 [BMobileAppShell showcase / placement](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/STORY.md) — done (2/2)
    - [x] [TASK-049](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-049-bmobileappshell-playground-placement.md) BMobileAppShell — better placement / demo in Birko.Web.Playground (P2, ai) — **done**
    - [x] [TASK-050](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-050-bmobileappshell-xaml-gallery.md) BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery (P3, ai) — **done**
- **EPIC-017** [Tenant isolation hardening](EPIC-017-tenant-isolation-hardening/EPIC.md) — in-progress (0/0 tasks done)

## Loose tasks

- [x] [TASK-036](_loose/TASK-036-workspace-reorg-birko-framework-consumers-buckets.md) Reorganize C:\Source into Birko/{Framework,Framework.Tests,Consumers} + aicode bucket (P1, ai) — **done**
- [x] [TASK-058](_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md) SqLiteConnector emits invalid AUTOINCREMENT DDL for non-primary-key increment fields (dual-key models) (P2, ai) — **done**
- [ ] [TASK-059](_loose/TASK-059-nested-projitems-import-convention-decision.md) Decide the long-term convention for nested `.projitems` imports (MSB4011) (P3, ai)

## Completed

<details>
<summary>1 completed epic(s)</summary>

- **EPIC-009** [Birko.Communication — Remaining protocols](EPIC-009-communication-protocols/EPIC.md) — done (2/2 tasks done)
  - STORY-019 [gRPC support](EPIC-009-communication-protocols/STORY-019-grpc/STORY.md) — done (1/1)
    - [x] [TASK-026](EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md) gRPC client + server support (P2, ai) — **done**
  - STORY-020 [OAuth2 authorization server](EPIC-009-communication-protocols/STORY-020-oauth2-server/STORY.md) — done (1/1)
    - [x] [TASK-027](EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md) Implement Birko.Security.OAuth.Server (P2, ai) — **done**

</details>

