# Data ownership

For the current Storage Audit facts see [StorageAudit](../modules/OssAudit/StorageAudit.md): the dedicated HTTP v1 three-party complete snapshots, Run metadata, and read-only status-3 observations; all resolves 409/502, zero deletions, and startup keeps old objects/history. The Phase 4 gRPC integration matrix below is an existing historical design and must not be used as the basis for the current audit implementation.

> **Phase 4 planned changes (not yet implemented)**: this document has been updated to the Phase 4 target state. Main changes:
> - Admin Portal now owns the OSS audit logic (path aggregation, zombie detection) and no longer depends on dedicated downstream interfaces
> - The 10 admin-specific interfaces of `StudentManagementGrpcService` are removed, replaced by generic gRPC + direct OSS operations
> - `MistakeGrpcService.GetAllReferencedImagePaths` is removed; Admin Portal paginates through the generic gRPC and aggregates by itself

## Overview

As the administration aggregation layer, Admin Portal owns only a small amount of its own data; most data comes from live queries against downstream services.

## Owned data entities

The following data entities are **owned** by the Admin Portal service and stored in the local PostgreSQL database:

### OssAuditRecord

| Property | Value |
|------|------|
| Database table | `OssAuditRecords` |
| Lifecycle | new record → UnreferencedObservation (read-only); old Pending/Resolved/Ignored kept as history |
| Writers | Admin Portal (OssAuditWorker / OssAuditController) |
| Readers | Admin Portal (OssAuditController) |
| Description | persisted observations and old audit history; the current resolve deletes neither records nor objects |

### OssAuditRun

| Property | Value |
|------|------|
| Database table | `OssAuditRuns` |
| Lifecycle | created → Running → Completed / Failed |
| Writers | Admin Portal (OssAuditWorker) |
| Readers | Admin Portal (OssAuditController) |
| Description | audit job state and the complete three-party ReferenceSnapshots/ReferenceContractVersion; a new v1 failure produces no usable findings |

## Referenced external data

The following data entities do **not** belong to Admin Portal; they are fetched live through downstream services and are not persisted locally:

### Student (from the Student Service)

| Data | How obtained | Purpose |
|------|----------|------|
| Student list/detail | gRPC: `ListStudents`, `GetStudent` | student management CRUD |
| Identity account links | gRPC: `GetIdentityAccountsByStudentId`, `LinkIdentityAccountToStudent`, `UnlinkIdentityAccountFromStudent` | account link management |
| Open subjects | gRPC: `GetStudentOpenSubjects`, `SetStudentOpenSubjects` | subject opening configuration |
| Registered OSS paths | aggregated by Admin Portal itself: calls the generic gRPC `GetAllUploadRecords` with pagination and extracts image paths | deciding at audit time whether an object is referenced |
| OSS object list | direct call to `IOssService.ListObjectsAsync` | scanning OSS objects at audit time |
| OSS object deletion | direct call to `IOssService.DeleteAsync` + generic gRPC cleanup fingerprint | deleting zombie objects at audit Resolve |
| OSS object moves | direct call to `IOssService.CopyObjectAsync` + `DeleteAsync` | migration assistance |
| Upload records | gRPC: `GetAllUploadRecords`, `GetUploadRecord`, etc. | upload record management |
| Image presigned URLs | gRPC: `GetPresignedUrl` | image viewing |
| Image migration | gRPC: `MigrateImagesToMistake` | image migration (uploads→mistakes) |

> **Phase 4 change**: `StudentManagementGrpcService` is removed entirely. The former admin-specific interfaces (`GetRegisteredOssPaths`, `ListOssObjects`, `DeleteOssObject`, `MoveOssObject`) are implemented by Admin Portal itself (direct IOssService + generic gRPC aggregation). The former `GetAllUploadRecords`, `AdminDeleteUploadRecord`, and similar interfaces become generic gRPC calls.

### Mistake (from the Mistake Service)

| Data | How obtained | Purpose |
|------|----------|------|
| Mistake list/detail | gRPC: `GetMistakeItemList`, `GetMistakeItem`, `GetMistakeItemsByUpload` | mistake query/management |
| Mistake update | gRPC: `UpdateMistakeItem` | mistake information modification |
| Mistake image reference paths | aggregated by Admin Portal itself: calls the generic gRPC `GetMistakeItemList` with pagination and extracts image paths | deciding at audit time whether an object is referenced |
| Submit mistake upload | gRPC: `SubmitMistakeUpload` | assigning an upload record as a mistake |
| Complete upload review | gRPC: `CompleteUploadReview` | legacy data cleanup |
| Image presigned URLs | gRPC: `GetPresignedUrl` | image viewing |

> **Phase 4 change**: `MistakeGrpcService.GetAllReferencedImagePaths` is removed. Admin Portal paginates through the generic gRPC mistake items and aggregates the image paths itself.

### Identity Account (from the Identity Service)

| Data | How obtained | Purpose |
|------|----------|------|
| Account information | HTTP: `POST /api/gateway/users/batch` | batch query of account display names |
| Authentication/authorization | HTTP Proxy: `/api/identity/*` | proxying authentication requests |

### Teacher Portal (from the Teacher Portal Service)

| Data | How obtained | Purpose |
|------|----------|------|
| Teacher-side data | HTTP Proxy: `/api/teacher-portal/*` | proxying teacher-side management requests |

## Data write permission boundaries

### Strictly forbidden

Admin Portal **never** writes directly to the following external services' data stores:

- ❌ the Student Service's database
- ❌ the Mistake Service's database
- ❌ the Identity Service's database
- ❌ the Teacher Portal's database

All modifications of external data must go through the corresponding gRPC/HTTP interfaces, with the downstream service responsible for data consistency.

### Allowed indirect writes

Admin Portal modifies external data indirectly through downstream service APIs:

| Operation | Target service | API | Description |
|------|----------|-----|------|
| Create/update/delete students | Student Service | gRPC CRUD | proxying administrator operations |
| Link/unlink identity accounts | Student Service | gRPC Link/Unlink | proxying administrator operations |
| Set open subjects | Student Service | gRPC SetOpenSubjects | proxying administrator operations |
| Delete OSS zombie objects | Admin Portal direct operation | `IOssService.DeleteAsync` (automatically cleans associated thumbnails) + generic gRPC cleanup fingerprint | called at audit Resolve |
| Move OSS objects | Admin Portal direct operation | `IOssService.CopyObjectAsync` + `DeleteAsync` | migration assistance |
| Update mistake information | Mistake Service | gRPC UpdateMistakeItem | proxying administrator operations |
| Submit mistake upload | Mistake Service | gRPC SubmitMistakeUpload | upload record assignment |
| Complete upload review | Mistake Service | gRPC CompleteUploadReview | legacy data cleanup |
| Mark upload record completed | Student Service | gRPC MarkUploadRecordCompleted | after upload record assignment |
| Delete upload record | Student Service | gRPC DeleteUploadRecordAfterReview | legacy data cleanup |
| Image migration | Student Service | gRPC MigrateImagesToMistake | image migration (uploads→mistakes) |

> **Phase 4 change**: zombie objects formerly deleted through `StudentManagementGrpcService.DeleteOssObject` are now deleted by Admin Portal calling `IOssService.DeleteAsync` + the generic gRPC cleanup fingerprint directly. Objects formerly moved through `StudentManagementGrpcService.MoveOssObject` are now operated on OSS directly by Admin Portal. Image migration switched from direct OSS Copy+Delete to calling the Student gRPC `MigrateImagesToMistake`.

### Directly operated external resources

| Operation | Target | Description |
|------|------|------|
| OSS ListObjects | OSS storage | audit browsing: scanning the OSS object list |
| OSS DeleteObject | OSS storage | zombie file cleanup (`DeleteAsync` automatically cleans associated thumbnails) |
| OSS CopyObject + Delete | OSS storage | migration assistance (the MoveOssObject scenario) |

> **Phase 4 change**: the scope of Admin Portal's direct OSS operations expands from "image migration only" to "audit browsing + zombie cleanup + migration assistance", but the credential permissions are downgraded to read-only + limited write (Delete only for zombie cleanup, CopyObject only for migration assistance). Image migration no longer operates OSS directly; it goes through the Student gRPC `MigrateImagesToMistake`.
