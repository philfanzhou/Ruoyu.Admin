# LegacyDataCleanup Data Models

## gRPC message mapping

### LegacyCheck

| Step | gRPC request | gRPC service | Description |
|------|-----------|-----------|------|
| 1 | GetMistakeItemsByUploadRequest | Mistake | fetch all the mistakes associated with the upload record |

### LegacyClean

| Step | gRPC request | gRPC service | Description |
|------|-----------|-----------|------|
| 1 | GetMistakeItemsByUploadRequest | Mistake | fetch the associated mistakes and verify the preconditions |
| 2 | CompleteUploadReviewRequest | Mistake | complete the review flow |
| 3 | RemoveImagesFromRecordRequest | StudentLearning | remove the reviewed images (optional step) |
| 4 | DeleteUploadRecordAfterReviewRequest | StudentLearning | delete the upload record |

## Key gRPC request/response fields

### CompleteUploadReviewRequest

| Field | Type | Description |
|------|------|------|
| SourceUploadId | string | upload record ID |
| ReviewerId | string | reviewer ID (fixed to "admin-legacy-cleanup" in LegacyClean) |

### CompleteUploadReviewResponse

| Field | Type | Description |
|------|------|------|
| Success | bool | whether it succeeded |
| ErrorMessage | string | the error message on failure |
| RemovedImagePaths | repeated string | the list of image paths to remove from the upload record |

### DeleteUploadRecordAfterReviewRequest

| Field | Type | Description |
|------|------|------|
| RecordId | string | upload record ID |
| StudentId | string | student ID |

### RemoveImagesFromRecordRequest

| Field | Type | Description |
|------|------|------|
| RecordId | string | upload record ID |
| StudentId | string | student ID |
| ImagePaths | repeated string | the list of image paths to remove |

## Enum values

### ReviewStatus (MistakeProto)

| Value | Name | Description |
|----|------|------|
| 0 | PendingReview | pending review |
| 1 | Confirmed | confirmed |
| 2 | Rejected | rejected |

The condition for "reviewed" in LegacyCheck/LegacyClean: ReviewStatus is Confirmed or Rejected.

## Data flow

```
LegacyCheck:
  GetMistakeItemsByUpload → Items → decide allReviewed / hasPending → return isLegacy

LegacyClean:
  GetMistakeItemsByUpload → verify preconditions
    → CompleteUploadReview → RemovedImagePaths?
      → yes: RemoveImagesFromRecord
      → no: skip
    → DeleteUploadRecordAfterReview → return the result
```
