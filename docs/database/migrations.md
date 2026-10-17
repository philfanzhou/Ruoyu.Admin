# Migration history

## Migration strategy

This project uses **EF Core Migrations** (a migration chain matched exactly and in order: `20260930190608_InitialCreate` → `20261003030002_AddReferenceObservations` → `20261017060000_AddAppSettings`). The repository-maintained `AuditMigrationExecutor` (`backend/Admin.WebApi/Database/AuditMigrationExecutor.cs`) implements ServiceMantle's `IDatabaseMigrationExecutor` extension interface and is invoked at startup by ServiceMantle 0.3.1-rc.1's `StartupDatabaseGate`, which executes the shared `DatabaseMigrationOrchestrator` (a PostgreSQL advisory lock lets multiple instances migrate one after another). The old shared `DatabaseInitializer` + inline `CREATE TABLE IF NOT EXISTS` DDL path was removed in issue #57.

## Startup flow

Configuration is resolved by `AuditDatabaseStartupConfiguration` before the startup checks are registered: `Database:AllowCreate` missing/empty is false and only true/false is accepted; an empty or unparsable connection or an invalid switch is rejected with `database_target_preparation.invalid_target`, with no database I/O and no echo of the input. Deployment is fixed to PostgreSQL `MultiInstance`, the capability is explicitly declared by the shared `PostgreSqlDatabaseDeploymentCapabilityProvider`, and a real PostgreSQL advisory lock is used.

Using the official same-version `ServiceMantle 0.3.1-rc.1` PostgreSQL options preset and explicit capability registration; product configuration is still validated by the local boundary, and `AddStartupDatabaseGate` remains the one and only host entry point. Lock wait and preparation budgets are 30 seconds each, health classification, the executor, and the migrations are unchanged. The shared component's single-instance canonical identity is a SHA-256 digest of the credential-free TCP target; the original local entry point rejected single-instance identity outright. Admin fixedly selects the real lock lease in multi-instance mode and does not invoke the single-instance identity feature, so this component replacement does not affect the actual execution path. Rollback needs only the previous application code and the same original package version; it involves no database structure or data migration and does not undo already committed operations.

1. **Shared startup gate** (`StartupDatabaseGate`): inspects the database pointed to by the resolved connection string — an existing database is used as-is (no maintenance connection is created and no CREATEDB privilege is needed); a **verifiably missing** database is created only when `Database:AllowCreate=true` (default `false`), otherwise startup is refused with the fixed error code `database_target_preparation.creation_not_allowed` and zero writes; an unreachable server / authentication / permission / identity conflict is always refused, never falling back to database creation. The maintenance connection is derived by the shared `PostgreSqlMaintenanceConnection`, changing only the database name to `postgres` with the same credentials; the preparation budget is fixed at 30 seconds, after which the target database is re-inspected and the flow continues only if it is connectable.
2. **Migration execution order and locking** (ServiceMantle): an advisory lock derived from ServiceId `ruoyu-admin` (30-second acquisition budget) covers the initial check, execution, and the final in-lock check; failures exit non-zero with safe error codes (`migration.lock_*` / `migration.inspection_failed` / `migration.version_too_new` / `migration.execution_failed` / `migration.final_state_invalid`).
3. Takeover rules of the **executor** (`AuditMigrationExecutor`):
   - **Empty database** (no business tables, no history) → execute the known migration chain to create the tables;
   - **Complete legacy database** (both tables present, no `__EFMigrationsHistory`, structure verified column by column) → only backfill missing indexes (`CREATE INDEX IF NOT EXISTS`) and write the baseline history in a transaction, then execute the new observation migration, **data preserved**;
   - **Version too new** (history contains unknown migration ids) or **unknown/conflicting structure** (extra columns, type/nullability/identity shape mismatch, primary key/index mismatch, partial tables) → refuse takeover with the fixed error code `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`, zero writes, no automatic repair;
   - The baseline structure (columns, types, `GENERATED ALWAYS AS IDENTITY`, defaults, `<table>_pkey` primary key names, `IX_*` indexes) matches the old inline DDL item by item, guaranteed by `AuditMigrationTests` comparing the migration result against the old inline DDL item by item.

The shared `IHostedLifecycleService.StartingAsync` runs the one-and-only startup database check before the WebHost and `OssAuditWorker.StartAsync`; any failure refuses host startup with only a whitelisted safe code — no listener is started and the worker is not started. The host's cancellation token runs through every stage; on cancellation it throws `OperationCanceledException`, enters no later stage, and records no success. The startup check itself adds no tables or files and does not roll back databases already created or migrations already committed.

## Change history

| Date | Change | Affected tables |
|------|----------|--------|
| 2026-10-17 | Added migration `20261017060000_AddAppSettings`: the new `AppSettings` table for database-hosted application settings, with the executor contract (`KnownMigrationIds` / `KnownTableNames`) and the level-two upgrade path (issue #118) | `AppSettings` |
| 2026-10-02 | Upgraded same-version ServiceMantle 0.3.0, wired in the shared startup gate and EF Core health snapshot, deleted local database preparation, startup result recording, and the health snapshot adapter code (issue #66); the executor and migrations are unchanged | No database structure change |
| 2026-09-30 | Established the baseline migration `20260930190608_InitialCreate` and wired in the ServiceMantle migration execution components, removing the inline DDL and the shared initializer (issue #57) | `OssAuditRecords`, `OssAuditRuns` |
| [to confirm] | Initial creation of the database and table structure (the old inline DDL path) | `OssAuditRecords`, `OssAuditRuns` |

## Notes

- Adding or modifying a migration must keep the `AuditMigrationExecutor.KnownMigrationIds` contract in sync (at startup the executor verifies that the program's migrations match the known id list).
- Before upgrading, the operator must back up; any database modified by hand is not guaranteed to be taken over.
- Readiness uses the shared singleton `StartupDatabaseReceipt` and the scoped `EfCoreHealthSnapshotSource<AuditDbContext>` (MappedSchema). NotStarted / Running / Failed mean zero database access; Succeeded runs a read-only zero-row query against the EF-mapped table columns, which proves neither constraints, nor indexes, nor data correctness; it is re-sampled per request with a 3-second budget.

## #66 release compatibility notes

| Event | Old code/state | Current code/state |
|---|---|---|
| Missing database, creation refused by default | `RUOYU_ADMIN_DB_CREATION_NOT_ALLOWED` | `database_target_preparation.creation_not_allowed` |
| Invalid `Database:AllowCreate` | `RUOYU_ADMIN_DB_ALLOW_CREATE_INVALID` | `database_target_preparation.invalid_target` |
| Still not connectable after preparation | observation failure code (e.g. `database_target_preparation.connection_failed`) | `database_target_preparation.not_connectable_after_preparation` |
| Freshly created, not yet run receipt | Running | NotStarted, 503 `ruoyu-admin.startup_incomplete` |
| Receipt running | Running, 503 `ruoyu-admin.startup_incomplete` | unchanged |
| Failed receipt | incomplete, 503 `ruoyu-admin.startup_incomplete` | Failed, 503 `ruoyu-admin.startup_failed` |
| Cancelled run | no success recorded | Running, no success recorded |

Normal Succeeded/ready JSON, the migration `migration.*` and `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE` codes, the port, and authentication are unchanged. Probes are classified by the PostgreSQL classifier; exceptions that cannot be classified still refuse readiness with a fixed error code: `health.probe_failed`, without faking connection state. Rolling back the previous version restores the old startup/probe codes without rolling back the database structure; it does not undo created databases or committed migrations.

## StorageReferences v1 observation upgrade

Databases that exactly match the old baseline migration history, and legacy databases with a legal structure but no migration history, are both checked against the old baseline first; the latter must register the baseline migration history before the additive migration runs. The new migration adds the nullable text columns `ReferenceContractVersion` and `ReferenceSnapshots` to `OssAuditRuns`. Existing business and audit data is not rewritten, and no new-version reference snapshots or validation results are fabricated for old records. The migration history must exactly match, in order, a prefix of the known migration ids; a missing baseline, unknown ids, or structural conflicts are refused, and the executor's `KnownMigrationIds` must be kept in sync. When a non-empty `ReferenceSnapshots` or status-3 records exist, `Down` refuses to run, to avoid losing history; recovery must use a `pg_dump` of the whole database. Behavioral rules are in [StorageAudit](../modules/OssAudit/StorageAudit.md).

## #67 database schema reading and comparison component replacement

The executor reads the structure and migration history through `PostgreSqlSchemaEvidenceReader` (an explicitly registered extension component reading only the two known business tables under `public`); `EfCoreExpectedSchemaDerivation` derives the expectation from the EF design-time model, and the baseline expectation continues to exclude the two later-migration columns. The shared comparator checks names, identity, total index key count, and INCLUDE; when Admin converts the read result into the structure needed for comparison, it keeps following the original rules of ignoring name case, irrelevant extra indexes, and foreign keys. Types and column names still match exactly; defaults are not compared.

A legal legacy database missing only named indexes still gets the original DDL backfill; the baseline history is written idempotently by `EfCoreMigrationBaselineWriter` in its own transaction, while Admin remains responsible for verifying the structure first, holding the advisory lock lease, and re-checking before writing. The reader's multiple queries are not guaranteed to be a same-instant snapshot. The migration inventory, structure, configuration keys, and fixed error codes are unchanged; rolling back the application code and the same-version packages suffices — no new data migration exists, and committed migrations are not undone when the application rolls back.

The shared reader first reads the migration history without naming business tables, preferring to refuse unknown migration ids and histories that do not follow the known id order, and only then reads the full business-table structure. Even when an unknown version comes with zero-column tables that cannot be represented, it still returns `VersionTooNew`. A legal migration history cannot bypass the structure check; on refusal, no indexes are backfilled and no baseline migration history is registered.

## #118 AppSettings migration (2026-10-17)

Migration `20261017060000_AddAppSettings` creates the `AppSettings` table: `Key text` primary
key (`AppSettings_pkey`, natural key, no identity), `Value text NOT NULL` (the
registry-normalized value), `UpdatedAt bigint NOT NULL DEFAULT 0` (epoch seconds, the
`OssAuditRecord.CreatedAt` style), and `UpdatedBy text NULL` (the admin subject from the server
session ticket). No `Down` is implemented: the chain is forward-only, and rolling back means
restoring the whole database.

Executor contract notes:

- `KnownMigrationIds` and `KnownTableNames` include the third migration and the new table; a
  database whose history is the exact level-two prefix (`InitialCreate` +
  `AddReferenceObservations`, both audit tables with the reference columns) classifies as
  `KnownUpgradeRequired` and upgrades in place, rows retained.
- Structure refusal applies to `AppSettings` exactly as to the audit tables
  (`RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`, zero writes).
- Legacy takeover remains bound to the retired inline DDL's exact two-table set; a no-history
  database that already contains `AppSettings` is refused instead of taken over.
- Rollback boundary: the structure comparison projects only the executor's expected tables, so
  a table the running version does not know (for example an `AppSettings` table left behind
  after an application rollback with the history trimmed, or any stray table) neither rejects
  the inspection nor gets touched. A history that still contains the newer migration id keeps
  answering `VersionTooNew` — restoring the previous application binary against a
  fully-migrated database is refused, and the supported database rollback remains a full
  restore.
- Readiness (`MappedSchema` probing) now covers `AppSettings` automatically; no readiness
  semantics were changed.
