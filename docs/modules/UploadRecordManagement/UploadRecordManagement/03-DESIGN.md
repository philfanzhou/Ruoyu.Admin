# UploadRecordManagement Data Models

The current implementation uses HTTP ServiceClients. The index-based image-selection requests and the old gRPC mapping below are retained only as legacy migration background; for the managed-v1 mode, revision, per-group `requestKey`, `sourcePaths` or `sourceRegions`, and per-group results see the [managed assignment notes](../../../Integration/ManagedAssignment.md). Managed submission does not re-read the current Student and adds no Admin database migrations or operation-log tables; it uses a dedicated client calling the fixed POST route, sending the access token already verified in the server session.

## Request models

### RotateImageRequest

| Field | Type | Required | Description |
|------|------|------|------|
| StudentId | string | yes | student ID |
| ImageIndex | int | yes | image index |
| Rotation | int | yes | rotation angle |

Defined in: `backend/Controllers/OssUploadRecordController.cs`

### ResetStatusRequest

| Field | Type | Required | Description |
|------|------|------|------|
| StudentId | string | yes | student ID |
| TargetStatus | int | yes | target status (corresponds to the UploadStatus enum) |

Defined in: `backend/Controllers/OssUploadRecordController.cs`

### ImageAssignment

| Field | Type | Required | Description |
|------|------|------|------|
| ImageIndices | List\<int\> | yes | the list of image indices this assignment references, at least 1 element |
| Subject | int | yes | subject |
| Grade | int | yes | grade |
| Comments | string? | no | remark |

Defined in: `backend/Controllers/OssUploadRecordController.cs`

### AssignUploadRecordRequest

| Field | Type | Required | Description |
|------|------|------|------|
| StudentId | string | yes | student ID |
| Assignments | List\<ImageAssignment\> | yes | the assignment entry list |

Defined in: `backend/Controllers/OssUploadRecordController.cs`

## gRPC message mapping

### Upload-record queries

| Admin API request | gRPC request | gRPC service |
|----------------|-----------|-----------|
| GetAllUploadRecords | GetAllUploadRecordsRequest | StudentManagement |
| GetUploadRecord (inside assign) | GetUploadRecordRequest | StudentLearning |
| ResetUploadRecordStatus | ResetUploadRecordStatusRequest | StudentLearning |
| MarkUploadRecordCompleted | MarkUploadRecordCompletedRequest | StudentLearning |
| RotateUploadImage | RotateUploadImageRequest | StudentLearning |
| RemoveImageFromRecord | RemoveImageFromRecordRequest | StudentLearning |
| AnalyzeUploadRecord | AnalyzeUploadRecordRequest | StudentManagement |

### Mistake creation

| Admin API request | gRPC request | gRPC service |
|----------------|-----------|-----------|
| SubmitMistakeUpload (inside assign) | SubmitMistakeUploadRequest | Mistake |

## Enum values

### UploadStatus

| Value | Name | Description |
|----|------|------|
| 0 | Unspecified | unspecified |
| 1 | Uploaded | uploaded |

[to confirm] The complete list of UploadStatus enum values

### Subject / Grade

Fetch the complete enum options via `GET /api/admin/enum-options`.

## Response model notes

### GetAllUploadRecords response

- `studentName`: fetched through batch queries to StudentManagement.GetStudent, falling back to the studentId when the query fails
- `createdAt` / `updatedAt`: parsed from the gRPC Timestamp into Unix seconds, supporting numeric strings and ISO 8601 formats

### AssignUploadRecord response

- `createdItems`: one element per assignment, containing `itemId` (the newly created mistake_item ID)
- `remainingImageCount`: the number of images remaining in `image_upload_record.image_paths` after the assignment
- `totalImageCount`: the upload record's original total image count
- `warnings`: the warning list when some assignments fail
