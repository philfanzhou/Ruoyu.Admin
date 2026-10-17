# Database documentation

Local database documentation for the Admin Portal service. This directory is the **single source of truth** for the database structure.

> **Single-source-of-truth principle**: table structures, fields, indexes, and constraints are maintained only in this directory. The capability documents under [modules/](../modules/) only link here and never copy full table definitions. Any table-structure change must first update the documents in this directory.

## Database overview

| Property | Value |
|------|------|
| Database type | PostgreSQL |
| Database name | `ruoyu_admin` |
| Connection string configuration key | `ConnectionStrings:AuditDb` (local development default) / Consul `PostgreSql:*` + `Database:Name` (production synthesis) |
| ORM | Entity Framework Core (baseline migration + ServiceMantle migration execution components) |
| DbContext | `AuditDbContext` |

## Database connection strategy

The connection string is assembled by `SharedPostgreSqlConnectionStringFactory.BuildOrFallback`: Consul's `PostgreSql:Host/Port/Username/Password` is synthesized with the local `Database:Name=ruoyu_admin` into a PostgreSQL connection string; without Consul configured it falls back to the local `ConnectionStrings:AuditDb`. See [development/LocalSetup.md](../development/LocalSetup.md#database-connection-strategy) for details.

## Table list

| Table | Description | Detailed documentation |
|------|------|----------|
| `OssAuditRecords` | OSS read-only observations and historical audit records | [tables/oss_audit_records.md](tables/oss_audit_records.md) |
| `OssAuditRuns` | OSS audit run records | [tables/oss_audit_runs.md](tables/oss_audit_runs.md) |

## Entity relationships

See [relations.md](relations.md).

## Migration history

See [migrations.md](migrations.md).

Since issue #57 this project uses EF Core Migrations (a known migration chain matched exactly and in order), wired into ServiceMantle's migration execution components by `AuditMigrationExecutor` (a PostgreSQL advisory lock lets multiple instances execute one after another) to manage table structures.

## Removed tables

None. This project has never removed a table since its creation.

## Declaration

> This document is the **single source of truth** for the Admin Portal database structure. Any table-structure change must first update this document and the corresponding table document, and only then the code.
