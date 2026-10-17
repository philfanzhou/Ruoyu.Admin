# Entity relationship diagram

The Admin Portal local database contains only 2 tables, and there are **no foreign keys** between them.

## Diagram

```
+---------------------+          +---------------------+
|   OssAuditRecords   |          |    OssAuditRuns     |
+---------------------+          +---------------------+
| PK  Id              |          | PK  Id              |
|     ObjectPath (UQ) |          |     StartedAt       |
|     Bucket          |          |     CompletedAt     |
|     Size            |          |     Status          |
|     LastModified    |          |     NewZombieCount  |
|     Status          |          |     TriggerType     |
|     CreatedAt       |          |     ErrorMessage    |
|     ResolvedAt      |          +---------------------+
|     Note            |
+---------------------+          no foreign key
        |                               |
        |   logical association (not FK)|
        |                               |
        +---------- one AuditRun ------+
                    produces multiple
                    AuditRecords
```

## Relationship details

### OssAuditRuns → OssAuditRecords (logical association)

- **Relationship type**: one-to-many (logical), no foreign key
- **Description**: one audit run (`OssAuditRun`) produces zero or more audit records (`OssAuditRecord`)
- **Why no FK**: `OssAuditRecord` has no `AuditRunId` field, so a record cannot be traced back to the run that produced it
- **Business impact**: new runs fully persist the snapshot metadata of the three services; audit records have no RunId and remain associated through the original business logic; `resolve` deletes neither objects nor records

### Independence between the tables

The two tables are fully independent at the database level:

- `OssAuditRecords`: holds historical records and new read-only status-3 observations; `resolve` shares the reference check, returning 409 when references are complete and 502 when unavailable, and only historical Pending may be `ignore`d
- `OssAuditRuns`: stores the execution state of the audit job itself, used for concurrency control and status queries

## External data references

The Admin Portal database contains no tables of data from external services (Student, Mistake, Identity). Business data is queried over HTTP; complete references are collected by StorageReferences v1, run records persist the three services' metadata and versions, and the complete reference paths remain each providing service's responsibility to keep.

See [DataOwnership.md](../overview/DataOwnership.md) for details.
