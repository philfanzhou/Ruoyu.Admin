# OssAuditRecords

The OSS audit observation and historical disposition record table. A new v1 row only means no reference was observed at capture time.

## Table information

| Property | Value |
|------|------|
| Table name | `OssAuditRecords` |
| Database | `ruoyu_admin` (PostgreSQL) |
| Entity class | `Admin.WebApi.Persistence.OssAuditRecord` |
| DbContext | `AuditDbContext` |

## Field definitions

| Field | Type | Constraint | Description |
|------|------|------|------|
| `Id` | `bigint` | PK | Primary key, auto-increment |
| `ObjectPath` | `string` | NOT NULL, UNIQUE | OSS object path, e.g. `uploads/xxx.jpg`, `mistakes/xxx.jpg` |
| `Bucket` | `string` | NOT NULL | Bucket name. New records take only `uploads` or `mistakes` (decided by `OssAuditWorker.AuditedBucketNames`); `questions` and `documents` keep their historical values, and startup does not delete them |
| `Size` | `bigint` | NOT NULL | Object size (bytes) |
| `LastModified` | `bigint` | NOT NULL | Object last-modified time (Unix seconds) |
| `Status` | `int` | NOT NULL, DEFAULT 0 | Audit status, see the enum below |
| `CreatedAt` | `bigint` | NOT NULL | Record creation time (Unix seconds) |
| `ResolvedAt` | `bigint` | NULL | Record processing time (Unix seconds), null while unprocessed |
| `Note` | `string` | NULL | Remark; may carry a reason when a record is ignored |

## Status enum

| Value | Name | Description |
|----|------|------|
| 0 | `Pending` | Historical pending |
| 1 | `Resolved` | Historical deleted state; not deleted again now |
| 2 | `Ignored` | Historical ignored |
| 3 | `UnreferencedObservation` | No reference observed; a read-only report, not a deletion permission |

Old 0/1/2 stay historical and are not backfilled as new observations; no status can skip the shared collector or grant deletion.

## Indexes

| Index name | Field | Type | Description |
|--------|------|------|------|
| PK | `Id` | UNIQUE | Primary key index |
| IX_OssAuditRecords_Status | `Status` | NON-UNIQUE | Filter audit records by status |
| IX_OssAuditRecords_Bucket | `Bucket` | NON-UNIQUE | Filter by bucket |
| IX_OssAuditRecords_ObjectPath | `ObjectPath` | UNIQUE | Ensures the same object path is never recorded twice |
| IX_OssAuditRecords_CreatedAt | `CreatedAt` | NON-UNIQUE | Sort queries by creation time |

## DDL (PostgreSQL)

```sql
CREATE TABLE IF NOT EXISTS "OssAuditRecords" (
    "Id"        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ObjectPath" text    NOT NULL,
    "Bucket"     text    NOT NULL,
    "Size"       bigint  NOT NULL,
    "LastModified" bigint NOT NULL,
    "Status"     integer NOT NULL DEFAULT 0,
    "CreatedAt"  bigint  NOT NULL,
    "ResolvedAt" bigint  NULL,
    "Note"       text    NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OssAuditRecords_ObjectPath" ON "OssAuditRecords" ("ObjectPath");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Status" ON "OssAuditRecords" ("Status");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Bucket" ON "OssAuditRecords" ("Bucket");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_CreatedAt" ON "OssAuditRecords" ("CreatedAt");
```

The table structure is managed by the EF Core migration chain matched exactly and in order plus the ServiceMantle startup checks; see [migrations](../migrations.md).

## Business rules

- `ObjectPath` has an exactly matching UNIQUE constraint; when a record of any status already exists for a path, no duplicate is created.
- v1 only adds read-only observations with `Status=3`, committed in one transaction after a complete scan; no deletion or other disposition decision is made on that basis.
- Existing single records of any status, and non-empty batch `resolve`, all pass the same reference-collector check and are then refused deletion: 409 when references are complete, 502 when unavailable; no object Delete, no Copy, and no audit-record deletion are executed.
- `ignore` only moves historical status 0 to 2, optionally filling Note; status 3 is refused. Startup does not clean old review or old bucket records.
- For the original status meanings and Admin's current behavior rules, see [StorageAudit](../../modules/OssAudit/StorageAudit.md).
