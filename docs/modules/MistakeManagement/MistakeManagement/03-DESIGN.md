# MistakeManagement Data Models

## Request models

### UpdateMistakeRequest

| Field | Type | Required | Description |
|------|------|------|------|
| StudentId | string? | no | student ID; when empty, an empty string is passed to gRPC |
| Subject | int? | no | subject; when empty, 0 is passed to gRPC |
| Grade | int? | no | grade; when empty, 0 is passed to gRPC |

Defined in: `backend/Controllers/MistakeController.cs`

## gRPC message mapping

| Admin API request | gRPC request | gRPC service | Description |
|----------------|-----------|-----------|------|
| GetMistakeItems | GetMistakeItemListRequest | Mistake | paged mistake-list query |
| GetMistakeItem | IdRequest | Mistake | get a single mistake's details |
| GetMistakesByUploadId | GetMistakeItemsByUploadRequest | Mistake | query mistakes by upload record |
| UpdateMistakeItem | UpdateMistakeItemRequest | Mistake | update mistake information |
| MigrateImages (step 1) | IdRequest → GetMistakeItem | Mistake | get the mistake details |
| MigrateImages (step 3) | MigrateImagesToMistakeRequest | Student | image migration proxied through Student gRPC |
| MigrateImages (step 5) | UpdateMistakeItemRequest | Mistake | update sourceRegions |

## Key gRPC request/response fields

### GetMistakeItemListRequest

| Field | Type | Description |
|------|------|------|
| StudentId | string | student ID (empty string means no filter) |
| Subject | int | subject (0 means no filter) |
| Grade | int | grade (0 means no filter) |
| ReviewStatus | ReviewStatus | review status (optional) |
| Page | int | page number |
| Size | int | page size |

### MistakeItemDto (the mistake entry in the gRPC response)

| Field | Type | Description |
|------|------|------|
| Id | string | mistake ID |
| StudentId | string | student ID |
| Subject | int | subject |
| Grade | int | grade |
| SourceUploadId | string | source upload record ID |
| ReviewStatus | ReviewStatus | review status |
| ReviewerId | string | reviewer ID |
| ReviewedAt | long | review time |
| ReviewComment | string | review comment |
| Type | int | mistake type |
| QuestionId | string | associated question ID |
| CreatedAt | long | creation time |
| UpdatedAt | long | update time |
| SourceRegions | repeated SourceRegion | image-region list |

### SourceRegion

| Field | Type | Description |
|------|------|------|
| SourceImagePath | string | image path |
| BoundingBox | BoundingBox? | bounding box (may be null) |

### BoundingBox

| Field | Type | Description |
|------|------|------|
| X1 | int | top-left X coordinate |
| Y1 | int | top-left Y coordinate |
| X2 | int | bottom-right X coordinate |
| Y2 | int | bottom-right Y coordinate |

## Enum values

### ReviewStatus (MistakeProto)

| Value | Name | Description |
|----|------|------|
| 0 | PendingReview | pending review |
| 1 | Confirmed | confirmed |
| 2 | Rejected | rejected |

## Data conversion notes

- The list endpoints do not return `sourceRegions`; they return only `imageCount` (= sourceRegions.Count) and `firstImagePath`
- The detail endpoint returns the complete `sourceRegions`, including `boundingBox` (which may be null)
- `studentName` is fetched via `StudentManagement.GetStudent`, falling back to the studentId when the query fails
- The `by-upload` endpoint's `total` is a client-side count (items.Count), not server-side pagination

## N+1 query analysis of student-name fetching

### Current implementation

GetMistakeItems, GetMistakesByUploadId, and GetMistakeItem all need the student names to enrich the response. The current implementation:

```
1. Fetch the mistake list → extract the deduplicated studentId list
2. Call GetStudentAsync once per studentId
3. Build the studentId → studentName mapping
4. Fill studentName into the response
```

### Performance impact

- **Typical scenario**: one page of 20 mistakes involving 3-5 distinct students → 3-5 gRPC calls
- **Extreme scenario**: one page of 100 mistakes involving 50+ distinct students → 50+ gRPC calls
- **Current mitigation**: the code uses `Distinct()` on studentId, avoiding repeated queries for the same student

### Why not a batch query

The Student gRPC service's proto definition has no `ListStudentsByIds` or similar batch-query method. The only available methods are:
- `GetStudent` — single-item query
- `ListStudents` — paged list query (does not support filtering by an ID list)

### Optimization suggestions

1. **Short term**: add a `GetStudentsByIds` RPC to the Student proto that takes an ID list and returns a Student list
2. **Medium term**: introduce an in-memory cache (IMemoryCache) in the Admin Portal for the studentId → studentName mapping (TTL 5-10 minutes)
3. **Long term**: provide a change-notification mechanism in the Student service so the cache can be invalidated proactively

> **Current status**: the N+1 query performs acceptably in typical usage (3-5 extra gRPC calls), but it may become a bottleneck at large scale.

## MigrateImages atomicity analysis

### Current implementation (gRPC proxy mode)

MigrateImages proxies image migration through Student gRPC `MigrateImagesToMistake`, with no overall transaction guarantee:

```
1. GetMistakeItem → get the mistake details
2. Collect the uploads/-prefixed image paths → sourcePaths
3. Call StudentLearningGrpcServiceClient.MigrateImagesToMistakeAsync(sourcePaths)
4. Iterate the migration results: success → update region.SourceImagePath = newPath
5. UpdateMistakeItemAsync → write the updated regions back to the Mistake service
```

### Failure scenario analysis

| Failure point | Completed operations | Data state | Recovery |
|---------|-----------|---------|---------|
| Step 3 fails (gRPC call) | none | consistent | just retry |
| Step 3 partially succeeds | some images migrated | migrated images' paths updated, unmigrated ones not | re-run the migration |
| Step 5 fails (Update) | migration complete | the OSS files are migrated but the Mistake service still points at the old paths | the Mistake records must be manually updated to point at the new paths |

### Mitigation mechanisms

1. **Handled inside the Student service**: `MigrateImagesToMistake` processes Copy+Delete file by file; a single-file failure does not affect the others
2. **Result feedback**: gRPC returns each image's migration result (success/failure), letting the Admin Portal decide whether to update paths
3. **Idempotency**: when the migration is re-run, CopyObject for files already migrated to mistakes/ is idempotent

### Risk assessment

- **Biggest risk**: if step 5 (UpdateMistakeItemAsync) fails, the OSS files are already migrated but the Mistake records still point at the old paths
- **Probability**: low (happens only when the Mistake service is unavailable)
- **Recovery difficulty**: medium (the Mistake records must be updated manually or the migration re-run)

> **Improvement over the old implementation**: in the old implementation the Admin Portal operated on OSS directly (Copy+Delete); it now goes through the Student gRPC proxy, and the Admin Portal no longer holds OSS write permission (CopyObject/Upload).
