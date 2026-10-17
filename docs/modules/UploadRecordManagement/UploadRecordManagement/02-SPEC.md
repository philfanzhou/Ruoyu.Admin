# UploadRecordManagement API

The newly added managed-v1 and legacyfalse branches share the existing assign route. The managed branch's fields, validation, and per-group results follow the [managed integration contract](../../../Integration/ManagedAssignment.md); the index-based shape below describes the legacy branch. The current client is HTTP; the old gRPC names only describe the original migration background.

## Endpoint list

| Method | Path | Description |
|------|------|------|
| GET | /api/admin/oss-upload-records | Get all upload records |
| POST | /api/admin/oss-upload-records/{id}/reset-status | Reset an upload record's status |
| POST | /api/admin/oss-upload-records/{id}/assign | Assign an upload record at image granularity |
| GET | /api/admin/oss-upload-records/image | Get an upload record image |
| POST | /api/admin/oss-upload-records/{id}/rotate | Rotate an upload record image |
| DELETE | /api/admin/oss-upload-records/{id}/images/{imageIndex} | Remove an image from an upload record |
| POST | /api/admin/oss-upload-records/{id}/analyze | VL-analyze an upload record |

---

### GET /api/admin/oss-upload-records

Get all upload records page by page, filterable by status and student.

**Query parameters:**

| Parameter | Type | Required | Default | Description |
|------|------|------|--------|------|
| page | int | no | 1 | page number |
| pageSize | int | no | 20 | page size |
| status | int | no | -1 | upload status (-1 means all) |
| studentId | string | no | - | student ID |

**HTTP call:** Student `GET /api/uploads`; the list items serve directly as the detail drawer's data source, adding contentRevision/imageEntries/assignmentProtocol while keeping imagePaths. With managed enabled, bad metadata returns 502 and dependency errors fail explicitly.

**Response:**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "Zhang San",
      "status": 1,
      "imagePaths": ["path/to/image.jpg"],
      "comments": "string",
      "createdAt": 1234567890,
      "updatedAt": 1234567890
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

**Error response:** 500 - Failed to get upload records

---

### POST /api/admin/oss-upload-records/{id}/reset-status

Reset an upload record's status.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |

**Request body:**

```json
{
  "studentId": "string",
  "targetStatus": 1
}
```

**gRPC call:** `StudentLearning.ResetUploadRecordStatus`

**Response:**

```json
{
  "success": true,
  "message": "Status reset successfully"
}
```

**Error responses:**
- 400 - Failed to reset status (gRPC returned failure)
- 500 - Failed to reset upload record status

---

### POST /api/admin/oss-upload-records/{id}/assign

Assign the specified images of an upload record to mistake items. One request may contain multiple assignments; the images within each assignment share the same mistake.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |

**Request body:**

```json
{
  "studentId": "string",
  "assignments": [
    {
      "imageIndices": [0, 1],
      "subject": 1,
      "grade": 7,
      "comments": "Images 1 and 2 are the same Chinese-language mistake"
    }
  ]
}
```

**Request field notes:**
- `studentId` (string, required): student ID
- `assignments` (array, required): the assignment entry list
  - `imageIndices` (array, required): the image indices this assignment references (based on the upload record's original indices), with at least 1 element
  - `subject` (int, required): subject
  - `grade` (int, required): grade
  - `comments` (string, optional): remark; when empty, falls back to the upload record's comments

**gRPC call flow:**
1. `StudentLearning.GetUploadRecord` - fetch the upload record to get the image paths
2. Call `Mistake.SubmitMistakeUpload` for each assignment - create the mistake items
3. `StudentLearning.MarkUploadRecordCompleted` - mark the upload record as completed

**Response (success):**

```json
{
  "success": true,
  "message": "Assignment successful",
  "createdItems": [
    {
      "subject": 1,
      "grade": 7,
      "itemId": "mistake-item-id-1",
      "imageCount": 2
    }
  ],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**Response (with warnings):**

```json
{
  "success": false,
  "message": "Assignment completed with warnings",
  "warnings": ["No valid images found for assignment with indices: 5"],
  "createdItems": [...],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**Error responses:**
- 400 - Upload record not found
- 500 - Failed to assign upload record

---

### GET /api/admin/oss-upload-records/image

Fetch an image file from OSS storage.

**Query parameters:**

| Parameter | Type | Required | Description |
|------|------|------|------|
| path | string | yes | OSS object path |

**Dependency:** `IOssService.DownloadAsync`

**Response:** the image binary stream (Content-Type inferred from the extension)

**Supported image formats:** .jpg/.jpeg, .png, .gif, .webp, .bmp, .svg (other formats return application/octet-stream)

**Error responses:**
- 400 - Path is required
- 404 - Image not found
- 500 - Failed to get image

---

### POST /api/admin/oss-upload-records/{id}/rotate

Rotate an image in an upload record.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID (must be a legal GUID) |

**Request body:**

```json
{
  "studentId": "string",
  "imageIndex": 0,
  "rotation": 90
}
```

**gRPC call:** `StudentLearning.RotateUploadImage`

**Response:**

```json
{
  "success": true
}
```

**Error responses:**
- 400 - Invalid record ID / StudentId is required / gRPC returned failure
- 500 - Failed to rotate image

---

### DELETE /api/admin/oss-upload-records/{id}/images/{imageIndex}

Remove the image at the given index from an upload record.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |
| imageIndex | int | image index |

**Query parameters:**

| Parameter | Type | Required | Description |
|------|------|------|------|
| studentId | string | yes | student ID |

**gRPC call:** `StudentLearning.RemoveImageFromRecord`

**Response:**

```json
{
  "success": true,
  "message": "string",
  "recordDeleted": false,
  "remainingImageCount": 3
}
```

**Notes:** `recordDeleted` indicates whether the upload record was deleted automatically after the image removal (when all images are removed); `remainingImageCount` is the number of remaining images.

**Error responses:**
- 400 - studentId is required / gRPC returned failure
- 500 - Failed to remove image

---

### POST /api/admin/oss-upload-records/{id}/analyze

Run VL (vision-language) analysis on an upload record, automatically recognizing image groupings and subject/grade.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | upload record ID |

**gRPC call:** `StudentManagement.AnalyzeUploadRecord` (an Admin-only interface, to be migrated to the generic interface in Task 4.6)

**Response:**

```json
{
  "success": true,
  "errorMessage": "",
  "rawResponse": "string",
  "skipped": false,
  "prompt": "string",
  "compressedImages": ["path1", "path2"],
  "groups": [
    {
      "imageIndices": [0, 1],
      "subject": 1,
      "grade": 7,
      "description": "string"
    }
  ]
}
```

**Error response:** 500 - VL analysis failed

---

## Common error format

```json
{
  "message": "error description"
}
```
