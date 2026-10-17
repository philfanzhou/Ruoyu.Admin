# MistakeManagement API

## Endpoint list

| Method | Path | Description |
|------|------|------|
| GET | /api/admin/mistakes | Get the mistake list |
| GET | /api/admin/mistakes/{id} | Get a single mistake's details |
| GET | /api/admin/mistakes/by-upload/{uploadId} | Get the mistakes of an upload record |
| PUT | /api/admin/mistakes/{id} | Update mistake information |
| POST | /api/admin/mistakes/migrate-images/{id} | Migrate mistake images |

---

### GET /api/admin/mistakes

Get the mistake-record list page by page, supporting multiple filter conditions.

**Query parameters:**

| Parameter | Type | Required | Default | Description |
|------|------|------|--------|------|
| studentId | string | no | - | student ID |
| subject | int | no | - | subject |
| grade | int | no | - | grade |
| reviewStatus | int | no | - | review status (corresponds to the ReviewStatus enum) |
| page | int | no | 1 | page number (≤0 resets to 1) |
| size | int | no | 20 | page size (≤0 resets to 10, >100 capped at 100) |

**gRPC call:** `Mistake.GetMistakeItemList`

**Response:**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "Zhang San",
      "subject": 1,
      "grade": 1,
      "sourceUploadId": "string",
      "reviewStatus": 0,
      "reviewerId": "string",
      "reviewedAt": 0,
      "reviewComment": "string",
      "type": 0,
      "questionId": "string",
      "createdAt": 0,
      "updatedAt": 0,
      "imageCount": 1,
      "firstImagePath": "string"
    }
  ],
  "total": 100,
  "page": 1,
  "pageSize": 20,
  "totalPages": 5
}
```

**Notes:** `imageCount` is `sourceRegions.Count`, and `firstImagePath` is the first sourceRegion's `SourceImagePath`.

**Error response:** 500 - Failed to get mistake items

---

### GET /api/admin/mistakes/{id}

Get the specified mistake's detailed information, including sourceRegions and boundingBox.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | mistake ID |

**gRPC call:** `Mistake.GetMistakeItem`

**Response:**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "Zhang San",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0,
  "reviewerId": "string",
  "reviewedAt": 0,
  "reviewComment": "string",
  "type": 0,
  "questionId": "string",
  "createdAt": 0,
  "updatedAt": 0,
  "imageCount": 1,
  "firstImagePath": "string",
  "sourceRegions": [
    {
      "sourceImagePath": "string",
      "boundingBox": {
        "x1": 0,
        "y1": 0,
        "x2": 100,
        "y2": 100
      }
    }
  ]
}
```

**Notes:** `boundingBox` may be null (when BoundingBox is null in the gRPC response).

**Error response:** 500 - Failed to get mistake item

---

### GET /api/admin/mistakes/by-upload/{uploadId}

Get all the mistakes associated with the specified upload record.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| uploadId | string | upload record ID |

**gRPC call:** `Mistake.GetMistakeItemsByUpload`

**Response:**

```json
{
  "items": [...],
  "total": 10
}
```

**Notes:** `total` is items.Count (a client-side count, not server-side pagination); pagination parameters are not supported.

**Error response:** 500 - Failed to get mistakes by upload id

---

### PUT /api/admin/mistakes/{id}

Update the specified mistake's student, subject, and grade information.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | mistake ID |

**Request body:**

```json
{
  "studentId": "string",
  "subject": 1,
  "grade": 1
}
```

**Request field notes:**
- `studentId` (string?, optional): student ID; when empty, an empty string is passed to gRPC
- `subject` (int?, optional): subject; when empty, 0 is passed to gRPC
- `grade` (int?, optional): grade; when empty, 0 is passed to gRPC

**gRPC call:** `Mistake.UpdateMistakeItem`

**Response:**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "Zhang San",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0
}
```

**Error response:** 500 - Failed to update mistake item

---

### POST /api/admin/mistakes/migrate-images/{id}

Migrate the mistake's images from the `uploads/` path to the `mistakes/` path.

**Path parameters:**

| Parameter | Type | Description |
|------|------|------|
| id | string | mistake ID |

**Execution flow:**
1. Call `Mistake.GetMistakeItem` to get the mistake details
2. Filter the regions in `sourceRegions` whose `SourceImagePath` starts with `uploads/`, deduplicate into `sourcePaths`
3. Call the `StudentLearning.MigrateImagesToMistake` gRPC interface with `sourcePaths`
4. Iterate the per-image migration results returned by gRPC:
   - success → record `sourcePath → newPath` in pathMapping and update the region's `SourceImagePath` to the new path
   - failure → log an Error and keep the original path
5. Call `Mistake.UpdateMistakeItem` to update the mistake's sourceRegions

**Response (nothing to migrate):**

```json
{
  "success": true,
  "message": "All image paths are already under mistakes; nothing to migrate",
  "migratedCount": 0
}
```

**Response (with migration):**

```json
{
  "success": true,
  "message": "Migrated 3 images",
  "migratedCount": 3
}
```

**Error responses:**
- 500 - Failed to migrate images (gRPC call exception, GetMistakeItem exception, UpdateMistakeItem exception, etc.)

---

## Common error format

```json
{
  "message": "error description"
}
```
