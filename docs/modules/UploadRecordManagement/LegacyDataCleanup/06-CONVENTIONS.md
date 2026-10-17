# LegacyDataCleanup Conventions

## Naming conventions

- Endpoint routes: `legacy-check/{id}` and `legacy-clean/{id}` (kebab-case)
- Fixed ReviewerId value: `"admin-legacy-cleanup"` (identifies review-completion operations triggered by the administrator legacy-cleanup flow)

## Error handling conventions

- Every endpoint is wrapped in try-catch; exceptions uniformly return 500 + `ErrorResponse`
- A gRPC `Success=false` returns 400 + `ErrorResponse`
- Unmet preconditions return 400 + a descriptive message
- `ErrorResponse` format: `{ "message": "error description" }`

## Logging conventions

- Log with `ILogger<OssUploadRecordController>`
- Info logs record each step's execution:
  - `Starting legacy data cleanup for upload record: {RecordId}`
  - `Step 1: Calling CompleteUploadReview for {RecordId}`
  - `Step 2: Removing {Count} image paths from upload record {RecordId}`
  - `Step 3: Calling DeleteUploadRecordAfterReview for {RecordId}`
  - `Legacy data cleanup completed successfully for {RecordId}`
- Warning logs record step failures:
  - `CompleteUploadReview failed for {RecordId}: {Message}`
  - `DeleteUploadRecordAfterReview failed for {RecordId}: {Message}`
- Log messages are in English

## Flow conventions

### LegacyCheck is read-only
- Modifies no data
- Only queries the Mistake service for the associated mistakes' review status

### LegacyClean is a multi-step transaction
- Steps run strictly in order: verify → CompleteUploadReview → RemoveImagesFromRecord (optional) → DeleteUploadRecordAfterReview
- No transaction guarantee between steps: if a middle step fails, the already-executed steps are not rolled back [inferred]
- RemoveImagesFromRecord is optional, running only when CompleteUploadReview returns RemovedImagePaths

## User-facing message conventions

- The user-facing messages of LegacyCheck/LegacyClean are written in Chinese
- Log messages are in English
- The message inside ErrorResponse is mixed: precondition validation is in Chinese, gRPC failures are in English (taken from the gRPC response)

## Decision-logic conventions

- `allReviewed`: `mistakeList.Items.All(i => i.ReviewStatus == Confirmed || i.ReviewStatus == Rejected)`
- `hasPending`: `mistakeList.Items.Any(i => i.ReviewStatus == PendingReview)`
- Both conditions are checked together: cleanup is refused when `!allReviewed || hasPending`
- `hasUploadPathImage`: checks whether any `sourceRegions` entry has `SourceImagePath.StartsWith("uploads/")`
