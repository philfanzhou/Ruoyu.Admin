# UploadRecordManagement Conventions

The current calls use HTTP ServiceClients; the old gRPC names and the index-based image-selection flow below are retained only as legacy migration background. The managed mode follows the [managed assignment notes](../../../Integration/ManagedAssignment.md): it calls only the fixed `POST /api/mistakes/upload`, takes the access token from the server session that has already verified administrator permission and CSRF, and uses the sensitive-value-free `errorKind` instead of raw downstream text. Admin writes no receipts, moves or deletes no images or sources, and does not proactively seal sources (after handover, new groups are rejected); the in-page view can recover the fixed request, and Mistake stores the permanent results.

## Naming conventions

- Controller route: `api/admin/oss-upload-records` (kebab-case)
- Request models: PascalCase, defined inside the Controller file (for example `RotateImageRequest`, `ResetStatusRequest`, `ImageAssignment`, `AssignUploadRecordRequest`)
- Response fields: camelCase (anonymous-object properties)

## Error handling conventions

- Every endpoint is wrapped in try-catch; exceptions uniformly return 500 + `ErrorResponse`
- A gRPC `Success=false` returns 400 + `ErrorResponse`
- A failed parameter check returns 400 + `ErrorResponse`
- `ErrorResponse` format: `{ "message": "error description" }`

## gRPC calling conventions

- Upload-record operations preferably go through `StudentLearningGrpcServiceClient` (the generic interface)
- The following operations still go through `StudentManagementGrpcServiceClient` (an Admin-only interface), to be migrated to the generic interface in Task 4.6:
  - `GetAllUploadRecords`: the admin-side paged query over all upload records (no studentId needed)
  - `AnalyzeUploadRecord`: admin-side VL analysis (an Admin-only debug interface)
  - `GetStudent`: fetches student names (to fill studentName in the GetAllUploadRecords response)
- Mistake creation goes through `MistakeGrpcServiceClient`
- Student names are fetched by calling `StudentManagement.GetStudent` one by one (not batched), falling back to the studentId when the query fails
- Image operations go through `IOssService`

## Logging conventions

- Log with `ILogger<OssUploadRecordController>`
- Error logs: `_logger.LogError(ex, "Failed to ...: {RecordId}", id)`
- Info logs: `_logger.LogInformation("Successfully created mistake items {ItemIds} for upload record {RecordId}", ...)`
- Log messages are in English

## Data conversion conventions

- gRPC Timestamp to Unix seconds: via the private `ParseTimestampToUnixSeconds` method, supporting numeric strings and ISO 8601 formats
- UploadStatus enum: the frontend passes an int, converted to the `SProto.UploadStatus` enum
- Content-Type: inferred from the file extension, defaulting to `application/octet-stream`

## Assignment flow conventions

1. First fetch the upload record (GetUploadRecord)
2. Extract paths by image index
3. Call SubmitMistakeUpload per assignment
4. Finally call MarkUploadRecordCompleted
5. Return createdItems, remainingImageCount, totalImageCount

## Security conventions

- RotateImage validates that recordId is a legal GUID
- RemoveImageFromRecord requires the studentId query parameter
- GetImage requires a non-empty path query parameter
