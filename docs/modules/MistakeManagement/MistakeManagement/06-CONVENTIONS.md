# MistakeManagement Conventions

## Naming conventions

- Controller route: `api/admin/mistakes` (kebab-case)
- Request models: PascalCase, defined inside the Controller file (for example `UpdateMistakeRequest`)
- Response fields: camelCase (anonymous-object properties)

## Error handling conventions

- Every endpoint is wrapped in try-catch; exceptions uniformly return 500 + `ErrorResponse`
- `ErrorResponse` format: `{ "message": "error description" }`
- In MigrateImages, a single-image migration failure logs an Error and keeps the original path without aborting the whole flow

## gRPC calling conventions

- Mistake operations go through `MistakeGrpcServiceClient`
- Student names are fetched via `StudentManagementGrpcServiceClient.GetStudent`
- Image migration goes through `StudentLearningGrpcServiceClient.MigrateImagesToMistake` (no direct OSS operations anymore)

## Logging conventions

- Log with `ILogger<MistakeController>`
- Error logs: `_logger.LogError(ex, "Failed to ...")`
- Migration info logs: `_logger.LogInformation("Migrated image from {OldPath} to {NewPath}", oldPath, newPath)`
- Migration error logs: `_logger.LogError(ex, "Failed to migrate image {Path}", oldPath)`
- Log messages are in English

## Pagination conventions

- GetMistakeItems supports pagination with parameter validation: page ≤ 0 → 1, size ≤ 0 → 10, size > 100 → 100
- GetMistakesByUploadId does not support pagination; it returns all associated mistakes, with total as a client-side count

## Student-name fetching conventions

- Calls `StudentManagement.GetStudent` one by one (not batched)
- Falls back to showing the studentId when the query fails
- Skips the query for a whitespace studentId

## MigrateImages conventions

- Image migration is delegated to Student gRPC `MigrateImagesToMistake`; the path mapping is handled server-side
- The same path is migrated only once within a request (the pathMapping cache)
- Migration-result handling: success → update region.SourceImagePath; failure → keep the original path and log an Error
- UpdateMistakeItem is called last to batch-update all sourceRegions (including unmigrated ones)

## Response-field difference conventions

- List endpoints (GetMistakeItems, GetMistakesByUploadId): do not return sourceRegions; return imageCount and firstImagePath
- Detail endpoint (GetMistakeItem): returns the complete sourceRegions (with boundingBox)
- Update endpoint (UpdateMistakeItem): returns trimmed fields (id, studentId, studentName, subject, grade, sourceUploadId, reviewStatus)
