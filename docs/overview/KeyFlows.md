# Key business flows

## 1. OSS audit flow

The OSS audit discovers "zombie" files in OSS storage that no business references. It supports both scheduled and manual triggers.

### Sequence diagram

```
Scheduled trigger (OssAuditWorker)         Manual trigger (OssAuditController)
    │                                        │
    │  daily at 2:00 AM (configurable)       │  POST /api/admin/oss-audit/trigger
    │                                        │
    ▼                                        ▼
┌──────────────────────────────────────────────────────┐
│ 1. Create OssAuditRun (Status=Running)               │
│    → if SaveChanges fails (DbUpdateException)        │
│      a concurrent audit exists; exit immediately     │
│    → double check: query for other Status=0 Runs     │
│      if any, delete the current Run and exit         │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 2. Aggregate Student's registered OSS paths          │
│    HTTP: IStudentHttpClient.GetAllUploadRecordsAsync │
│    (paginate upload records, aggregate image_paths   │
│     locally)                                         │
│    → Student service unavailable → mark Run Failed   │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 3. Merge in Homework's reference paths               │
│    HTTP: HomeworkReferenceClient.GetAllImagePathsAsync│
│    → Homework service unavailable → mark Run Failed  │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 4. Aggregate Mistake's referenced image paths        │
│    HTTP: IMistakeHttpClient.GetMistakeItemListAsync  │
│    (paginate mistake items, aggregate                │
│     source_regions image paths)                      │
│    → Mistake service unavailable → mark Run Failed   │
│      incomplete reference aggregation must not yield │
│      disposable conclusions, so no degraded continue │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 5. Iterate OssAuditWorker.AuditedBuckets             │
│    currently the two buckets uploads, mistakes       │
│    (questions, documents have no reference source    │
│     and are outside the audit scope)                 │
│    IOssService: ListObjectsWithBucketAsync (per bucket)│
│                                                       │
│    For each object:                                  │
│    ├─ ThumbnailHelper.IsThumbnailPath? → skip        │
│    │  (thumbnails are tied to the original image;   │
│    │  removed automatically when it is deleted)     │
│    ├─ in registeredPaths? → skip                    │
│    ├─ in mistakePaths? → skip                       │
│    └─ in neither → check whether OssAuditRecords    │
│       already has it                                │
│       └─ not present → insert a new record          │
│          (Status=Pending)                           │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 6. Update OssAuditRun                               │
│    Status=Completed, NewZombieCount=N               │
└──────────────────────────────────────────────────────┘
```

### Resolve flow

```
Administrator                OssAuditController       Student Svc      Mistake Svc      DB         OSS
  │                              │                        │                  │              │          │
  │ POST /records/{id}/resolve   │                        │                  │              │          │
  │─────────────────────────────>│                        │                  │              │          │
  │                              │ GetRegisteredOssPaths  │                  │              │          │
  │                              │───────────────────────>│                  │              │          │
  │                              │<──────────────────────│                  │              │          │
  │                              │                        │                  │              │          │
  │                              │ GetAllReferencedImagePaths                │              │          │
  │                              │─────────────────────────────────────────>│              │          │
  │                              │<────────────────────────────────────────│              │          │
  │                              │                        │                  │              │          │
  │                              │ [Verify: path still referenced?]          │              │          │
  │                              │ referenced → 400 BadReq │                  │              │          │
  │                              │                        │                  │              │          │
  │                              │ IOssService.DeleteAsync │                  │              │          │
  │                              │─────────────────────────────────────────────────────────────────────>│
  │                              │                        │                  │              │          │
  │                              │  (inside DeleteAsync:  │                  │              │          │
  │                              │   IsThumbnailPath? → no │                  │              │          │
  │                              │   GetAllThumbnailPaths  │                  │              │          │
  │                              │   → delete associated   │                  │              │          │
  │                              │     thumbnails          │                  │              │          │
  │                              │   → delete the original)│                  │              │          │
  │                              │<─────────────────────────────────────────────────────────────────────│
  │                              │                        │                  │              │          │
  │                              │ Remove AuditRecord     │                  │              │          │
  │                              │──────────────────────────────────────────────────────>│          │
  │                              │<─────────────────────────────────────────────────────│          │
  │                              │                        │                  │              │          │
  │<─────────────────────────────│ 200 OK                 │                  │              │          │
```

---

## 2. Upload record assignment flow

Assigns the images of an upload record to mistake items by group, supporting cross-subject assignment.

### Sequence diagram

```
Administrator                OssUploadRecordController  Student Svc      Mistake Svc
  │                              │                        │                  │
  │ POST /{id}/assign            │                        │                  │
  │ {assignments: [...]}         │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │                        │                  │
  │                              │ GetUploadRecord        │                  │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │                              │ ┌─ for each assignment ┐│                  │
  │                              │ │                     ││                  │
  │                              │ │ SubmitMistakeUpload ││                  │
  │                              │ │─────────────────────────────────────>│
  │                              │ │<────────────────────────────────────│
  │                              │ │ (returns createdItemIds)││          │
  │                              │ └─────────────────────┘│                  │
  │                              │                        │                  │
  │                              │ MarkUploadRecordCompleted               │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │<─────────────────────────────│ 200 OK                 │                  │
  │   { createdItems,            │                        │                  │
  │     remainingImageCount,     │                        │                  │
  │     totalImageCount }        │                        │                  │
```

### Key semantics

- One `assignment` = one mistake = one `mistake_item`
- The multiple images within one assignment are packed into the same `mistake_item`'s `source_regions`
- Multiple assignments can specify different `subject` / `grade` each (cross-subject assignment)
- The same upload record can be assigned multiple times (each creates a new `mistake_item`)
- After assignment the upload record is marked Completed and the `mistake_item` enters `PendingReview`

---

## 3. Legacy data cleanup flow

Cleans up upload records that finished review but still linger. When all mistakes linked to an upload record have been reviewed (Confirmed/Rejected) but the upload record itself was not deleted, it is legacy data.

### Sequence diagram

```
Administrator                OssUploadRecordController  Mistake Svc      Student Svc
  │                              │                        │                  │
  │ 1. GET /legacy-check/{id}    │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │ GetMistakeItemsByUpload│                  │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │                              │ [Decide: legacy data?] │                  │
  │  - no linked mistakes → isLegacy=false               │                  │
  │  - any pending review → isLegacy=false               │                  │
  │  - all reviewed → isLegacy=true                      │                  │
  │<─────────────────────────────│                        │                  │
  │                              │                        │                  │
  │ 2. POST /legacy-clean/{id}   │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │                        │                  │
  │                              │ Step 1: CompleteUploadReview             │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │  (RemovedImagePaths)   │                  │
  │                              │                        │                  │
  │                              │ Step 2: RemoveImagesFromRecord [if any]  │
  │                              │───────────────────────────────────────>│
  │                              │<──────────────────────────────────────│
  │                              │                        │                  │
  │                              │ Step 3: DeleteUploadRecordAfterReview   │
  │                              │───────────────────────────────────────>│
  │                              │<──────────────────────────────────────│
  │                              │                        │                  │
  │<─────────────────────────────│ 200 OK { success: true }               │
```

### The three cleanup steps

1. **CompleteUploadReview**: calls the Mistake service to complete the upload review and get the list of image paths to remove
2. **RemoveImagesFromRecord**: calls the Student service to remove the processed image paths from the upload record
3. **DeleteUploadRecordAfterReview**: calls the Student service to delete the upload record

---

## 4. Student CRUD and account linking flow

Creating, querying, updating, and deleting students, plus linking/unlinking Identity accounts.

### Create student sequence diagram

```
Administrator                StudentsController       Student Svc (gRPC)
  │                              │                        │
  │ POST /api/admin/students     │                        │
  │ { name, grade,              │                        │
  │   identityAccountIds }       │                        │
  │─────────────────────────────>│                        │
  │                              │                        │
  │                              │ [Validation]           │
  │                              │ - name non-empty       │
  │                              │ - grade valid (1-12)   │
  │                              │ - accountIds non-empty │
  │                              │ - accountIds well-formed│
  │                              │                        │
  │                              │ CreateStudent          │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │                              │                        │
  │<─────────────────────────────│ 200 OK (StudentDto)    │
```

### Link/unlink account sequence diagram

```
Administrator                StudentsController       Student Svc (gRPC)
  │                              │                        │
  │ Link:                        │                        │
  │ POST /{studentId}/accounts   │                        │
  │ { identityAccountId }        │                        │
  │─────────────────────────────>│                        │
  │                              │ LinkIdentityAccount    │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │<─────────────────────────────│ 200 OK                 │
  │                              │                        │
  │ Unlink:                      │                        │
  │ DELETE /{sid}/accounts/{aid} │                        │
  │─────────────────────────────>│                        │
  │                              │ UnlinkIdentityAccount  │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │<─────────────────────────────│ 200 OK                 │
```

### Key constraints

- Creating a student requires at least one `identityAccountId`
- `identityAccountId` must be a valid GUID
- Grade range is 1-12 (1-6 primary, 7-9 junior high, 10-12 senior high)
- When updating a student, `identityAccountIds` is optional; when provided it replaces the whole link list
