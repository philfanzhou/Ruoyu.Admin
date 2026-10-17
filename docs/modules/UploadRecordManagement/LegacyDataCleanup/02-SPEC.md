# LegacyDataCleanup API

## Endpoint list

| Method | Path | Description |
|------|------|------|
| GET | /api/admin/oss-upload-records/legacy-check/{id} | Check whether an upload record is legacy data |
| POST | /api/admin/oss-upload-records/legacy-clean/{id} | Clean up legacy upload-record data |

---

### GET /api/admin/oss-upload-records/legacy-check/{id}

Check whether the specified upload record is legacy data.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |

**gRPC call:** `Mistake.GetMistakeItemsByUpload`

**Response (no associated mistakes):**

```json
{
  "isLegacy": false,
  "message": "This record has no associated mistakes"
}
```

**Response (has pending-review mistakes):**

```json
{
  "isLegacy": false,
  "pendingCount": 3,
  "message": "This record still has 3 mistakes pending review"
}
```

**Response (all reviewed — legacy data):**

```json
{
  "isLegacy": true,
  "mistakeCount": 5,
  "hasUploadPathImage": true,
  "message": "All the mistakes of this record have finished review, but the upload record still exists; it is legacy data"
}
```

**Response field notes:**
- `isLegacy` (bool): whether it is legacy data
- `mistakeCount` (int): the number of associated mistakes (returned only when isLegacy=true)
- `hasUploadPathImage` (bool): whether any image is still under the `uploads/` path (returned only when isLegacy=true)
- `pendingCount` (int): the number of pending-review mistakes (returned only when isLegacy=false and some are pending)
- `message` (string): status description

**Error response:** 500 - Failed to check legacy status

---

### POST /api/admin/oss-upload-records/legacy-clean/{id}

Clean up legacy upload-record data. Runs the full review-completion and record-deletion flow.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |

**gRPC call flow:**
1. `Mistake.GetMistakeItemsByUpload` - fetch the associated mistakes and verify the preconditions
2. `Mistake.CompleteUploadReview` - complete the review flow (ReviewerId = "admin-legacy-cleanup")
3. `StudentLearning.RemoveImagesFromRecord` - remove the reviewed images (only when CompleteUploadReview returns RemovedImagePaths)
4. `StudentLearning.DeleteUploadRecordAfterReview` - delete the upload record

**Response (success):**

```json
{
  "success": true,
  "message": "Cleanup complete",
  "uploadRecordId": "string"
}
```

**Error responses:**
- 400 - This record has no associated mistakes; cannot clean up
- 400 - This record still has N mistakes pending review; cannot clean up
- 400 - Cannot find the student ID of this upload record
- 400 - CompleteUploadReview failed
- 400 - DeleteUploadRecordAfterReview failed
- 500 - Failed to clean legacy data

**Execution step logs:**
- Step 1: `Starting legacy data cleanup for upload record: {RecordId}`
- Step 1: `Calling CompleteUploadReview for {RecordId}`
- Step 2: `Removing {Count} image paths from upload record {RecordId}` (only when there are RemovedImagePaths)
- Step 3: `Calling DeleteUploadRecordAfterReview for {RecordId}`
- Done: `Legacy data cleanup completed successfully for {RecordId}`

## Multi-step operation failure semantics in detail

The LegacyClean operation has 3 sequential steps with no overall transaction or rollback mechanism:

### Step 1: CompleteUploadReview
- **Call**: `_mistakeClient.CompleteUploadReviewAsync`
- **Failure behavior**: returns 400 Bad Request; the whole operation aborts
- **Side effects**: none (the Mistake service internally marks the review complete)

### Step 2: RemoveImagesFromRecord (conditional)
- **Call**: `_learningClient.RemoveImagesFromRecordAsync`
- **Trigger condition**: runs only when `completeReviewResult.RemovedImagePaths.Count > 0`
- **Failure behavior**: the exception propagates upward, returning 500, but step 1 is already done and cannot be rolled back
- **Side effects**: the image paths are removed from the upload record

### Step 3: DeleteUploadRecordAfterReview
- **Call**: `_learningClient.DeleteUploadRecordAfterReviewAsync`
- **Failure behavior**: returns 400 Bad Request, but steps 1 and 2 are already done and cannot be rolled back
- **Side effects**: the upload record is deleted

### Risk assessment

| Failure point | Completed steps | Data state | Impact |
|---------|-----------|---------|------|
| Step 1 fails | none | consistent | none |
| Step 2 fails | step 1 | the Mistake service has marked the review complete, but the image paths were not removed from the upload record | the upload record still contains the reviewed images; the cleanup can be re-run |
| Step 3 fails | steps 1+2 | the images are removed but the upload record is not deleted | the upload record becomes an empty record and can be deleted manually |

### Idempotency analysis

- CompleteUploadReview: [inferred] the Mistake service should be idempotent for repeated calls with the same uploadId
- RemoveImagesFromRecord: [inferred] the Student service should be idempotent for repeated calls with already-removed paths
- DeleteUploadRecordAfterReview: [inferred] repeated calls for an already-deleted record should fail without side effects

> **Recommendation**: the current implementation relies on the downstream services' idempotency for data consistency. For stronger consistency guarantees, compensating transactions or the Saga pattern could be considered, but in the current scenario a retry is usually enough to resolve the problem.
