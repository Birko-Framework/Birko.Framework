# Tasks — Birko.Framework

_Generated 2026-08-01. Run `/tasks triage` to refresh. **Do not hand-edit** — changes will be overwritten._

## Counts

| Status       | Epics              | Stories            | Tasks               |
|--------------|--------------------|--------------------|---------------------|
| planned      | 10                 | 24                 | —                   |
| todo         | —                  | —                  | 62                  |
| in-progress  | 6                  | 10                  | 1                   |
| review       | —                  | —                  | 10                  |
| blocked      | —                  | —                  | 0                   |
| done         | 1                  | 22                 | 33                  |
| cancelled    | 0                  | 0                  | 0                   |

Todo by priority: **P0 4 · P1 15 · P2 39 · P3 4**

## In progress now

- [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: component gallery + live token editor + theme-CSS export (P2, ai)

## In review (awaiting sign-off)

- [TASK-135](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-135-b-input-decimal-comma-locale-mode.md) `b-input type="decimal"`: comma-locale decimal entry, owned by the component (P1, ai)
- [TASK-136](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-136-bform-validate-surfaces-control-validity.md) `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()` (P1, ai)
- [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add `bare` attribute to all form controls (P2, ai)
- [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table to bare components (P2, ai)
- [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) `description` — a persistent help-text row on the form controls (P2, ai)
- [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai)
- [TASK-107](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-107-b-button-tap-target-and-form-participation.md) `b-button`: a reachable tap target, and form participation (P2, ai)
- [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals (P3, ai)
- [TASK-104](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-104-b-chart-small-chart-axis-polish.md) `b-chart`: axis polish for small charts (tick density, nice scale, latest-value overlay, threshold labels) (P3, ai)
- [TASK-105](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-105-b-card-padding-md-and-shadow-token.md) `b-card`: the missing `md` padding rung, and elevation as a token (P3, ai)

## Next up (top P0s, blocked excluded)

- [TASK-109](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-109-sql-bulk-null-filter-whole-table-statement.md) A null or untranslatable filter renders `DELETE FROM "T"` — the whole table (P0, ai)
- [TASK-112](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-112-unmapped-primitive-types-never-persist.md) `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist (P0, ai)
- [TASK-113](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-113-tenantsyncprovider-scopes-only-saves.md) `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant (P0, ai)
- [TASK-116](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-116-rulespecification-leaves-degrade-to-match-all.md) `RuleSpecification` leaves degrade to match-all — on the destructive paths (P0, ai)

## Tree

- **EPIC-001** [Birko.Web.Components — UI polish](EPIC-001-web-components-ui-polish/EPIC.md) — in-progress (4/9 tasks done)
  - [x] [TASK-053](EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md) b-range: vertical orientation (equalizer-style slider) (P3, ai) · FEATURE-001
  - STORY-001 [bare attribute for inline form usage](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/STORY.md) — in-progress (0/1)
    - [ ] [TASK-001](EPIC-001-web-components-ui-polish/STORY-001-bare-attribute/TASK-001-add-bare-attribute-to-form-controls.md) Add `bare` attribute to all form controls (P2, ai) 🔍 review · FEATURE-001
  - STORY-002 [b-editable-table migration to bare components](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/STORY.md) — in-progress (0/1)
    - [ ] [TASK-002](EPIC-001-web-components-ui-polish/STORY-002-editable-table-migration/TASK-002-benchmark-and-migrate-editable-table.md) Benchmark + migrate b-editable-table to bare components (P2, ai) 🔍 review · FEATURE-001
  - STORY-003 [size attribute coverage](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/STORY.md) — planned (0/1)
    - [ ] [TASK-003](EPIC-001-web-components-ui-polish/STORY-003-size-attribute-coverage/TASK-003-size-on-pagination-dropdown-breadcrumb.md) size attribute on b-pagination, b-dropdown-menu, b-breadcrumb (P2, ai) · FEATURE-001
  - STORY-023 [Form-associated custom elements (ElementInternals)](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/STORY.md) — in-progress (0/5)
    - [ ] [TASK-035](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md) Make form controls form-associated via ElementInternals (P3, ai) 🔍 review · FEATURE-001
    - [ ] [TASK-136](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-136-bform-validate-surfaces-control-validity.md) `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()` (P1, ai) 🔍 review · FEATURE-001
    - [ ] [TASK-132](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-132-bform-required-inert-on-unchecked-toggle.md) `b-form`: `required` on a checkbox / switch is inert — an unchecked toggle counts as filled (P2, ai) · FEATURE-001
    - [ ] [TASK-133](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-133-bform-radio-value-never-collected.md) `b-form`: a `radio` field's value is never collected, and a `required` radio group can never validate (P2, ai) · FEATURE-001
    - [ ] [TASK-134](EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-134-bform-remaining-validity-flags-decision.md) Decide whether `b-form.validate()` adopts the remaining validity flags, starting with `typeMismatch` (P2, ai) · FEATURE-001
  - STORY-028 [Display & disclosure components](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/STORY.md) — done (3/3) (done)
    - [x] [TASK-039](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-039-b-chart-coerce-unitless-height.md) b-chart: coerce/validate a unitless `height` (avoid endless SVG stretch) (P3, ai) · FEATURE-001
    - [x] [TASK-040](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-040-b-accordion-component.md) Add a `b-accordion` (collapsible / disclosure group) component (P2, ai) · FEATURE-001
    - [x] [TASK-041](EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/TASK-041-shared-coerce-css-length.md) Extract a shared `coerceCssLength` helper and fix the unitless-length bug across components (P2, ai) · FEATURE-001
  - STORY-050 [Visible help text on form controls](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/STORY.md) — in-progress (0/1)
    - [ ] [TASK-091](EPIC-001-web-components-ui-polish/STORY-050-help-text-row/TASK-091-description-help-text-row.md) `description` — a persistent help-text row on the form controls (P2, ai) 🔍 review · FEATURE-001
- **EPIC-002** [Birko.Data.Redis](EPIC-002-birko-data-redis/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-004](EPIC-002-birko-data-redis/TASK-004-implement-birko-data-redis.md) Implement Birko.Data.Redis (P2, ai) · FEATURE-002
- **EPIC-003** [Birko.Caching.NCache](EPIC-003-birko-caching-ncache/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-005](EPIC-003-birko-caching-ncache/TASK-005-implement-birko-caching-ncache.md) Implement Birko.Caching.NCache (P2, ai) · FEATURE-003
- **EPIC-004** [Birko.Storage — Cloud providers](EPIC-004-storage-cloud-providers/EPIC.md) — planned (0/3 tasks done)
  - STORY-004 [AWS S3 storage](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/STORY.md) — planned (0/1)
    - [ ] [TASK-006](EPIC-004-storage-cloud-providers/STORY-004-aws-s3/TASK-006-birko-storage-aws.md) Implement Birko.Storage.Aws (P1, ai) · FEATURE-004
  - STORY-005 [Google Cloud Storage](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/STORY.md) — planned (0/1)
    - [ ] [TASK-007](EPIC-004-storage-cloud-providers/STORY-005-google-cloud-storage/TASK-007-birko-storage-google.md) Implement Birko.Storage.Google (P2, ai) · FEATURE-004
  - STORY-006 [MinIO (S3-compatible)](EPIC-004-storage-cloud-providers/STORY-006-minio/STORY.md) — planned (0/1)
    - [ ] [TASK-008](EPIC-004-storage-cloud-providers/STORY-006-minio/TASK-008-birko-storage-minio.md) Implement Birko.Storage.Minio (P2, ai) · FEATURE-004
- **EPIC-005** [Birko.Messaging — Provider expansion](EPIC-005-messaging-provider-expansion/EPIC.md) — planned (0/5 tasks done)
  - STORY-007 [Email providers (SendGrid + Mailgun)](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/STORY.md) — planned (0/2)
    - [ ] [TASK-009](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-009-birko-messaging-sendgrid.md) Implement Birko.Messaging.SendGrid (P1, ai) · FEATURE-005
    - [ ] [TASK-010](EPIC-005-messaging-provider-expansion/STORY-007-email-providers/TASK-010-birko-messaging-mailgun.md) Implement Birko.Messaging.Mailgun (P2, ai) · FEATURE-005
  - STORY-008 [SMS via Twilio](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/STORY.md) — planned (0/1)
    - [ ] [TASK-011](EPIC-005-messaging-provider-expansion/STORY-008-sms-twilio/TASK-011-birko-messaging-twilio.md) Implement Birko.Messaging.Twilio (P1, ai) · FEATURE-005
  - STORY-009 [Push notifications (Firebase + APNs)](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/STORY.md) — planned (0/2)
    - [ ] [TASK-012](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-012-birko-messaging-firebase.md) Implement Birko.Messaging.Firebase (P2, ai) · FEATURE-005
    - [ ] [TASK-013](EPIC-005-messaging-provider-expansion/STORY-009-push-notifications/TASK-013-birko-messaging-apple.md) Implement Birko.Messaging.Apple (P2, ai) · FEATURE-005
- **EPIC-006** [Birko.MessageQueue — Provider expansion](EPIC-006-messagequeue-provider-expansion/EPIC.md) — planned (0/5 tasks done)
  - STORY-010 [RabbitMQ (AMQP)](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/STORY.md) — planned (0/1)
    - [ ] [TASK-014](EPIC-006-messagequeue-provider-expansion/STORY-010-rabbitmq/TASK-014-birko-messagequeue-rabbitmq.md) Implement Birko.MessageQueue.RabbitMQ (P1, ai) · FEATURE-006
  - STORY-011 [Kafka](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/STORY.md) — planned (0/1)
    - [ ] [TASK-015](EPIC-006-messagequeue-provider-expansion/STORY-011-kafka/TASK-015-birko-messagequeue-kafka.md) Implement Birko.MessageQueue.Kafka (P1, ai) · FEATURE-006
  - STORY-012 [Cloud queue providers (Azure Service Bus + AWS SQS)](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/STORY.md) — planned (0/2)
    - [ ] [TASK-016](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-016-birko-messagequeue-azure.md) Implement Birko.MessageQueue.Azure (P2, ai) · FEATURE-006
    - [ ] [TASK-017](EPIC-006-messagequeue-provider-expansion/STORY-012-cloud-mq/TASK-017-birko-messagequeue-aws.md) Implement Birko.MessageQueue.Aws (P2, ai) · FEATURE-006
  - STORY-013 [MassTransit adapter](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/STORY.md) — planned (0/1)
    - [ ] [TASK-018](EPIC-006-messagequeue-provider-expansion/STORY-013-masstransit/TASK-018-birko-messagequeue-masstransit.md) Implement Birko.MessageQueue.MassTransit (P2, ai) · FEATURE-006
- **EPIC-007** [Birko.Telemetry — Additional exporters](EPIC-007-telemetry-exporters/EPIC.md) — planned (0/3 tasks done)
  - STORY-014 [Prometheus exporter](EPIC-007-telemetry-exporters/STORY-014-prometheus/STORY.md) — planned (0/1)
    - [ ] [TASK-019](EPIC-007-telemetry-exporters/STORY-014-prometheus/TASK-019-birko-telemetry-prometheus.md) Implement Birko.Telemetry.Prometheus (P2, ai) · FEATURE-007
  - STORY-015 [Seq log exporter](EPIC-007-telemetry-exporters/STORY-015-seq/STORY.md) — planned (0/1)
    - [ ] [TASK-020](EPIC-007-telemetry-exporters/STORY-015-seq/TASK-020-birko-telemetry-seq.md) Implement Birko.Telemetry.Seq (P2, ai) · FEATURE-007
  - STORY-016 [Grafana LGTM stack exporter](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/STORY.md) — planned (0/1)
    - [ ] [TASK-021](EPIC-007-telemetry-exporters/STORY-016-grafana-lgtm/TASK-021-birko-telemetry-grafana.md) Implement Birko.Telemetry.Grafana (P2, ai) · FEATURE-007
- **EPIC-008** [Birko.Health — Queue + cloud health checks](EPIC-008-health-mq-cloud-checks/EPIC.md) — planned (0/4 tasks done)
  - STORY-017 [Message queue health checks](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-022](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-022-rabbitmq-health-check.md) RabbitMqHealthCheck (P2, ai) · FEATURE-008
    - [ ] [TASK-023](EPIC-008-health-mq-cloud-checks/STORY-017-mq-health-checks/TASK-023-kafka-health-check.md) KafkaHealthCheck (P2, ai) · FEATURE-008
  - STORY-018 [Cloud queue health checks](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/STORY.md) — planned (0/2)
    - [ ] [TASK-024](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-024-azure-service-bus-health-check.md) AzureServiceBusHealthCheck (P2, ai) · FEATURE-008
    - [ ] [TASK-025](EPIC-008-health-mq-cloud-checks/STORY-018-cloud-health-checks/TASK-025-aws-sqs-health-check.md) AwsSqsHealthCheck (P2, ai) · FEATURE-008
- **EPIC-010** [Birko.Data.RavenDB — Index ergonomics](EPIC-010-ravendb-index-ergonomics/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-028](EPIC-010-ravendb-index-ergonomics/TASK-028-attribute-driven-raven-indexes.md) Attribute-driven RavenDB index definitions (Option B) (P2, ai) · FEATURE-010
- **EPIC-011** [Birko.Framework — Test coverage gaps](EPIC-011-test-coverage-gaps/EPIC.md) — planned (0/7 tasks done)
  - [ ] [TASK-052](EPIC-011-test-coverage-gaps/TASK-052-birko-web-unit-test-runner.md) Adopt a web unit-test runner for Birko.Web.* (migrate backport-smoke) (P2, ai) · FEATURE-011
  - STORY-021 [Redis-dependent tests](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/STORY.md) — planned (0/2)
    - [ ] [TASK-029](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-029-backgroundjobs-redis-tests.md) Birko.BackgroundJobs.Redis.Tests (P2, ai) · FEATURE-011
    - [ ] [TASK-030](EPIC-011-test-coverage-gaps/STORY-021-redis-dependent-tests/TASK-030-caching-redis-tests.md) Birko.Caching.Redis.Tests (P2, ai) · FEATURE-011
  - STORY-022 [Phase 4 lower-priority tests](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/STORY.md) — planned (0/3)
    - [ ] [TASK-031](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-031-models-validation-tests.md) Birko.Models.* validation tests (P2, ai) · FEATURE-011
    - [ ] [TASK-032](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-032-viewmodel-crud-tests.md) Birko.Data.*.ViewModel CRUD tests (P2, ai) · FEATURE-011
    - [ ] [TASK-033](EPIC-011-test-coverage-gaps/STORY-022-phase-4-tests/TASK-033-configuration-contracts-tests.md) Birko.Configuration + Birko.Contracts DTO tests (P2, ai) · FEATURE-011
  - STORY-047 [Review filter-parser behaviour on live document databases](EPIC-011-test-coverage-gaps/STORY-047-null-filter-live-parser-review/STORY.md) — planned (0/1)
    - [ ] [TASK-060](EPIC-011-test-coverage-gaps/STORY-047-null-filter-live-parser-review/TASK-060-run-and-review-live-null-tests.md) Run & review the live null-filter parser tests (P2, ai) · FEATURE-011
- **EPIC-012** [Birko.MessageQueue.MQTT — v5 features](EPIC-012-mqtt-v5-features/EPIC.md) — planned (0/1 tasks done)
  - [ ] [TASK-034](EPIC-012-mqtt-v5-features/TASK-034-mqtt-v5-topic-aliases-user-properties.md) MQTT v5 topic aliases + user properties (P2, ai) · FEATURE-012
- **EPIC-013** [Reference consumers — integration smoke harness + Web playground](EPIC-013-reference-consumers/EPIC.md) — in-progress (1/2 tasks done)
  - [x] [TASK-037](EPIC-013-reference-consumers/TASK-037-extract-backend-smoke-harness-consumer.md) Replace the TUI example with an extracted backend integration smoke-harness consumer (P2, ai) · FEATURE-013
  - [ ] [TASK-038](EPIC-013-reference-consumers/TASK-038-birko-web-playground.md) Birko.Web playground: component gallery + live token editor + theme-CSS export (P2, ai) ← in-progress · FEATURE-013
- **EPIC-014** [Code review — audit remediation](EPIC-014-code-review-remediation/EPIC.md) — in-progress (5/16 tasks done)
  - [ ] [TASK-131](EPIC-014-code-review-remediation/TASK-131-per-sub-repo-spec-trees.md) Per-sub-repo `docs/specs/` trees — the aggregator's staleness guard cannot fire (P2, ai) · FEATURE-014
  - STORY-024 [Critical findings](EPIC-014-code-review-remediation/STORY-024-critical-findings/STORY.md) — done (0/0) (done)
  - STORY-025 [High findings](EPIC-014-code-review-remediation/STORY-025-high-findings/STORY.md) — done (0/0) (done)
  - STORY-026 [Medium findings](EPIC-014-code-review-remediation/STORY-026-medium-findings/STORY.md) — in-progress (0/0)
  - STORY-027 [Low findings](EPIC-014-code-review-remediation/STORY-027-low-findings/STORY.md) — done (0/0) (done)
  - STORY-042 [Integration-test tier — the Docker-gated remediation findings](EPIC-014-code-review-remediation/STORY-042-integration-test-tier/STORY.md) — planned (0/0)
  - STORY-043 [Workflow backends — unify the serialization seam (ISerializer everywhere)](EPIC-014-code-review-remediation/STORY-043-workflow-serializer-seam/STORY.md) — done (0/0) (done)
  - STORY-051 [Spec-harvest — high findings](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/STORY.md) — in-progress (5/15)
    - [x] [TASK-108](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-108-pbkdf2-empty-segment-auth-bypass.md) `Pbkdf2PasswordHasher.Verify` returns `true` for any password against an empty-segment hash (P0, ai) · FEATURE-014
    - [ ] [TASK-109](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-109-sql-bulk-null-filter-whole-table-statement.md) A null or untranslatable filter renders `DELETE FROM "T"` — the whole table (P0, ai) · FEATURE-014
    - [x] [TASK-110](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-110-order-by-identifier-unresolved-and-unquoted.md) ORDER BY identifiers reach SQL text unresolved and unquoted (P0, ai) · FEATURE-014
    - [ ] [TASK-111](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-111-rule-field-unresolved-in-where-clause.md) `rule.Field` reaches the WHERE clause unresolved and unquoted (P1, ai) · FEATURE-014
    - [ ] [TASK-112](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-112-unmapped-primitive-types-never-persist.md) `long` / `double` / `float` / `short` / `byte[]` map to no column and never persist (P0, ai) · FEATURE-014
    - [ ] [TASK-113](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-113-tenantsyncprovider-scopes-only-saves.md) `TenantSyncProvider` scopes only saves — reads, previews and deletes span every tenant (P0, ai) · FEATURE-014
    - [x] [TASK-114](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-114-tenant-write-guard-trusts-caller-supplied-tenantguid.md) The item-level tenant write guard trusts the caller-supplied `TenantGuid` (P0, ai) · FEATURE-014
    - [x] [TASK-115](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-115-nested-withtenant-does-not-narrow-reads.md) A nested `WithTenant` does not narrow reads inside an all-tenants scope (P1, ai) · FEATURE-014
    - [ ] [TASK-116](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-116-rulespecification-leaves-degrade-to-match-all.md) `RuleSpecification` leaves degrade to match-all — on the destructive paths (P0, ai) · FEATURE-014
    - [ ] [TASK-117](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-117-rediscache-clearasync-flushdb.md) `RedisCache.ClearAsync` issues `FLUSHDB` when no `KeyPrefix` is set (P1, ai) · FEATURE-014
    - [ ] [TASK-118](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-118-tenant-header-guard-covers-only-x-tenant-id.md) The tenant header/claim guard covers only the hard-coded `X-Tenant-Id` (P1, ai) · FEATURE-014
    - [ ] [TASK-125](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-125-readone-bypasses-store-decorators.md) `ReadOne` queries the connector directly, bypassing every store decorator (P1, ai) · FEATURE-014
    - [ ] [TASK-126](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-126-tagging-has-no-tenant-assertion.md) `TagServiceBase` states its tenant contract in a comment and enforces nothing (P1, ai) · FEATURE-014
    - [x] [TASK-128](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-128-view-order-by-identifier-unresolved.md) The view path's ORDER BY still interpolates caller text — the twin TASK-110 did not cover (P0, ai) · FEATURE-014
    - [ ] [TASK-129](EPIC-014-code-review-remediation/STORY-051-spec-harvest-high-findings/TASK-129-aggregate-view-ddl-double-alias.md) An aggregate view's generated DDL carries a double alias, so no persistent aggregate view can be created (P1, ai) · FEATURE-014
  - STORY-053 [Spec-harvest — medium findings](EPIC-014-code-review-remediation/STORY-053-spec-harvest-medium-findings/STORY.md) — planned (0/0)
  - STORY-054 [Spec-harvest — low findings](EPIC-014-code-review-remediation/STORY-054-spec-harvest-low-findings/STORY.md) — planned (0/0)
  - STORY-055 [Spec-harvest — the three unrated areas](EPIC-014-code-review-remediation/STORY-055-spec-harvest-unrated-areas/STORY.md) — in-progress (0/0)
- **EPIC-015** [Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web](EPIC-015-birko-xaml-ui-framework/EPIC.md) — in-progress (10/22 tasks done)
  - [x] [TASK-054](EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md) Xaml restyled Slider (Tier-1 gap) + `Range` Form field type (P3, ai) · FEATURE-015
  - [x] [TASK-055](EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md) Xaml Form field-type parity with b-form (wire existing controls + FormField props) (P2, ai) · FEATURE-015
  - [x] [TASK-056](EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md) Xaml date & time picker controls + field types (P2, ai) · FEATURE-015
  - [x] [TASK-057](EPIC-015-birko-xaml-ui-framework/TASK-057-xaml-form-multiselect-tags-file.md) Xaml Form field types: MultiSelect / Tags / File (P2, ai) · FEATURE-015
  - [x] [TASK-101](EPIC-015-birko-xaml-ui-framework/TASK-101-avalonia-ribbon-pinned-temporary-reveal.md) Avalonia `Ribbon`: pinned vs temporary-reveal collapse, to match `b-ribbon` and Office (P2, ai) · FEATURE-015
  - [x] [TASK-102](EPIC-015-birko-xaml-ui-framework/TASK-102-avalonia-ribbon-narrow-fallback.md) Avalonia `Ribbon`: a narrow fallback, mirroring `b-ribbon`'s hamburger (P2, ai) · FEATURE-015
  - [ ] [TASK-103](EPIC-015-birko-xaml-ui-framework/TASK-103-focus-visual-for-all-avalonia-buttons.md) Every Avalonia control needs a focus visual — `Buttons.axaml` has none (P2, ai) · FEATURE-015
  - STORY-029 [Tier 0 — single-source design tokens + multi-target generator](EPIC-015-birko-xaml-ui-framework/STORY-029-design-tokens-generator/STORY.md) — done (0/0) (done)
  - STORY-030 [Tier 0 — Avalonia theme system + runtime ThemeVariant swap](EPIC-015-birko-xaml-ui-framework/STORY-030-avalonia-theme-system/STORY.md) — done (0/0) (done)
  - STORY-031 [Tier 0 validation — Avalonia gallery app + first restyled controls](EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md) — done (0/0) (done)
  - STORY-032 [Birko.Xaml.Core — i18n ({l:Tr}) + base ViewModels (Avalonia-free)](EPIC-015-birko-xaml-ui-framework/STORY-032-xaml-core-foundation/STORY.md) — done (0/0) (done)
  - STORY-033 [Building blocks — schema-driven Form, Drawer, SplitPanel](EPIC-015-birko-xaml-ui-framework/STORY-033-building-blocks-form-drawer-splitpanel/STORY.md) — done (0/0) (done)
  - STORY-034 [Tier 1 — restyled native controls (~20)](EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md) — done (0/0) (done)
  - STORY-035 [Tier 2 — composite controls with no native peer](EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md) — done (0/0) (done)
  - STORY-036 [Tier 3 — Birko.Xaml.Shell: page bases + app chrome + navigation](EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md) — done (0/0) (done)
  - STORY-048 [Avalonia 12 / .NET 10 upgrade for the Birko.Xaml stack](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/STORY.md) — planned (0/5)
    - [ ] [TASK-092](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-092-bump-avalonia-12-net10-xunit-v3.md) Bump Birko.Xaml to Avalonia 12.1.0 / `net10.0` + xunit v3 (Kanban DataTransfer, focus event) (P2, ai) · FEATURE-015
    - [ ] [TASK-093](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-093-livecharts-avalonia-12-story.md) Decide the LiveCharts story for Avalonia 12 (the only blocker on the bump) (P2, human) · FEATURE-015
    - [ ] [TASK-094](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-094-avalonia-12-obsolete-warning-sweep.md) Clear the 28 Avalonia 12 obsolete warnings (`Watermark`, `Bitmap.Save`) (P3, ai) · FEATURE-015
    - [ ] [TASK-095](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-095-avalonia-screenshot-baseline-gate.md) Screenshot baseline gate for the Avalonia suite (build it *before* the Av12 bump) (P2, ai) · FEATURE-015
    - [ ] [TASK-096](EPIC-015-birko-xaml-ui-framework/STORY-048-avalonia-12-net10-upgrade/TASK-096-consumer-repo-avalonia-12-rollout.md) Roll Avalonia 12 out to consumer repos in lockstep (P2, ai) · FEATURE-015
  - STORY-049 [Office-style ribbon overflow — progressive group scaling + group-to-popup collapse](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/STORY.md) — done (4/4) (done)
    - [x] [TASK-097](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-097-make-ribbon-overflow-reachable.md) Make the existing ribbon overflow reachable (interim fix, both skins) (P1, ai) · FEATURE-015
    - [x] [TASK-098](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-098-ribbon-size-variant-scaling-priority-model.md) Ribbon model + tokens: size variant, scaling priority, group icon (XAML **and** web together) (P2, ai) · FEATURE-015
    - [x] [TASK-099](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-099-progressive-group-scaling-degrade-pass.md) The degrade pass — measure and scale groups Large → Medium → Small in priority order (P2, ai) · FEATURE-015
    - [x] [TASK-100](EPIC-015-birko-xaml-ui-framework/STORY-049-ribbon-overflow-progressive-scaling/TASK-100-group-collapse-to-popup.md) Group-collapse-to-popup — the chunk button and its flyout (P2, ai) · FEATURE-015
  - STORY-056 [Mixed per-item size variants within one ribbon group](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/STORY.md) — planned (0/6)
    - [ ] [TASK-119](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-119-decide-mixed-size-model.md) Decide the mixed-size model: per-item degrade order, or fixed group templates (P1, human) · FEATURE-015
    - [ ] [TASK-120](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-120-mixed-size-model-both-skins.md) The mixed-size model, in both skins, with its tokens (P1, ai) · FEATURE-015
    - [ ] [TASK-121](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-121-reformulate-scaling-ladder-for-mixed-groups.md) Reformulate the degrade ladder for mixed-size groups (P1, ai) · FEATURE-015
    - [ ] [TASK-122](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-122-render-mixed-columns-both-skins.md) Render mixed columns — the CSS grid and the Avalonia panel (P2, ai) · FEATURE-015
    - [ ] [TASK-123](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-123-panel-height-under-mixed-sizes.md) Panel height under mixed sizes, and extending the clipping guard (P2, ai) · FEATURE-015
    - [ ] [TASK-124](EPIC-015-birko-xaml-ui-framework/STORY-056-mixed-per-item-ribbon-sizes/TASK-124-stale-ribbongroupsize-parity-comment.md) The `RibbonGroupSize` doc comment describes a parity gap that no longer exists (P3, ai) · FEATURE-015
- **EPIC-016** [Birko framework backports from Reps (+ cross-provider & Xaml follow-ups)](EPIC-016-birko-backports-from-reps/EPIC.md) — in-progress (9/13 tasks done)
  - STORY-037 [Backend / SQL framework backports (shipped)](EPIC-016-birko-backports-from-reps/STORY-037-backend-sql-backports/STORY.md) — done (0/0) (done)
  - STORY-038 [Frontend Birko.Web backports (shipped)](EPIC-016-birko-backports-from-reps/STORY-038-frontend-web-backports/STORY.md) — done (0/0) (done)
  - STORY-039 [Cross-provider SQL store-factory + DI backport](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/STORY.md) — in-progress (1/2)
    - [ ] [TASK-042](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md) Backport store-factory + DI extension to MSSql / MySQL / PostgreSQL (P2, ai) 🔍 review · FEATURE-016
    - [x] [TASK-051](EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md) FIX: MSSqlStore.SetSettings drops connection fields (lossy) (P2, ai) · FEATURE-016
  - STORY-040 [Web → Xaml UI / offline / device backports](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/STORY.md) — done (6/6) (done)
    - [x] [TASK-043](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md) Xaml mobile app-shell (BMobileAppShell equivalent) (P2, ai) · FEATURE-016
    - [x] [TASK-044](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md) Formatter for Birko.Xaml.Core (duration + culture-aware) (P2, ai) · FEATURE-016
    - [x] [TASK-045](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-045-xaml-wake-lock.md) Xaml wake-lock device abstraction (IWakeLock) (P3, ai) · FEATURE-016
    - [x] [TASK-046](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-046-xaml-offline-mirror.md) Xaml offline read-through mirror (MirrorStore / readThrough concept) (P3, ai) · FEATURE-016
    - [x] [TASK-047](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-047-xaml-sync-status-indicator.md) Xaml sync-status indicator (offline / syncing / synced) (P3, ai) · FEATURE-016
    - [x] [TASK-048](EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-048-xaml-audio-cue.md) Xaml audio-cue device util (beep + vibrate) (P3, ai) · FEATURE-016
  - STORY-041 [BMobileAppShell showcase / placement](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/STORY.md) — done (2/2) (done)
    - [x] [TASK-049](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-049-bmobileappshell-playground-placement.md) BMobileAppShell — better placement / demo in Birko.Web.Playground (P2, ai) · FEATURE-016
    - [x] [TASK-050](EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/TASK-050-bmobileappshell-xaml-gallery.md) BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery (P3, ai) · FEATURE-016
  - STORY-052 [Component gaps found by consumers adopting the `b-*` catalogue](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/STORY.md) — in-progress (0/4)
    - [ ] [TASK-104](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-104-b-chart-small-chart-axis-polish.md) `b-chart`: axis polish for small charts (tick density, nice scale, latest-value overlay, threshold labels) (P3, ai) 🔍 review · FEATURE-016
    - [ ] [TASK-105](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-105-b-card-padding-md-and-shadow-token.md) `b-card`: the missing `md` padding rung, and elevation as a token (P3, ai) 🔍 review · FEATURE-016
    - [ ] [TASK-107](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-107-b-button-tap-target-and-form-participation.md) `b-button`: a reachable tap target, and form participation (P2, ai) 🔍 review · FEATURE-016
    - [ ] [TASK-135](EPIC-016-birko-backports-from-reps/STORY-052-component-gaps-from-catalogue-adoption/TASK-135-b-input-decimal-comma-locale-mode.md) `b-input type="decimal"`: comma-locale decimal entry, owned by the component (P1, ai) 🔍 review · FEATURE-016
- **EPIC-017** [Tenant isolation hardening](EPIC-017-tenant-isolation-hardening/EPIC.md) — in-progress (0/0 tasks done)
  - STORY-044 [Opt-in strict (fail-closed) tenancy mode](EPIC-017-tenant-isolation-hardening/STORY-044-strict-fail-closed-mode/STORY.md) — done (0/0) (done)
  - STORY-045 [Fix decorator ordering so per-tenant uniqueness probes are tenant-scoped](EPIC-017-tenant-isolation-hardening/STORY-045-decorator-order-per-tenant-uniqueness/STORY.md) — done (0/0) (done)
  - STORY-046 [Restore ambient (tenant) scope for background event dispatch](EPIC-017-tenant-isolation-hardening/STORY-046-event-scope-restoration/STORY.md) — in-progress (0/0)

## Loose tasks

- [x] [TASK-036](_loose/TASK-036-workspace-reorg-birko-framework-consumers-buckets.md) Reorganize C:\Source into Birko/{Framework,Framework.Tests,Consumers} + aicode bucket (P1, ai)
- [x] [TASK-058](_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md) SqLiteConnector emits invalid AUTOINCREMENT DDL for non-primary-key increment fields (dual-key models) (P2, ai)
- [ ] [TASK-059](_loose/TASK-059-nested-projitems-import-convention-decision.md) Decide the long-term convention for nested `.projitems` imports (MSB4011) (P3, ai)
- [ ] [TASK-106](_loose/TASK-106-css-part-as-a-catalogue-convention-decision.md) Decide whether `::part` is a catalogue convention or stays a one-off (P3, human)
- [ ] [TASK-127](_loose/TASK-127-all-tenants-scope-and-ambient-tenant-decision.md) Decide what `WithAllTenants` means when a tenant is also in scope (P2, human)
- [ ] [TASK-130](_loose/TASK-130-theme-contrast-scanner-gate.md) Scan every shipped theme for colour contrast, and gate it like the drift check (P1, ai)

<details>
<summary><b>Completed epics (1)</b></summary>

- **EPIC-009** [Birko.Communication — Remaining protocols](EPIC-009-communication-protocols/EPIC.md) — done (2/2 tasks done)
  - STORY-019 [gRPC support](EPIC-009-communication-protocols/STORY-019-grpc/STORY.md) — done (1/1) (done)
    - [x] [TASK-026](EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md) gRPC client + server support (P2, ai) · FEATURE-009
  - STORY-020 [OAuth2 authorization server](EPIC-009-communication-protocols/STORY-020-oauth2-server/STORY.md) — done (1/1) (done)
    - [x] [TASK-027](EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md) Implement Birko.Security.OAuth.Server (P2, ai) · FEATURE-009

</details>
