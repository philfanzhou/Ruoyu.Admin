# MistakeManagement Tests

## Existing test coverage

### MistakeControllerTests (implemented)

| Test method | What it verifies |
| --- | --- |
| `GetMistakeItems_Success_ReturnsPaginatedItemsWithStudentNames` | GetMistakeItems returns paged data and student names |
| `GetMistakeItems_StudentQueryFails_FallsBackToStudentId` | falls back to the studentId when the student query fails |
| `GetMistakeItems_Page0_ResetsTo1` | page=0 resets to 1 |
| `GetMistakeItems_Size0_ResetsTo10` | size=0 resets to 10 |
| `GetMistakeItems_Size200_ClampedTo100` | size=200 is capped at 100 |
| `GetMistakeItems_ReviewStatus1_SendsConfirmedInGrpcRequest` | reviewStatus=1 sends Confirmed in the gRPC request |
| `GetMistakeItems_GrpcException_Returns500` | a gRPC exception returns 500 |
| `GetMistakeItem_Success_ReturnsItemWithSourceRegionsAndBoundingBox` | GetMistakeItem returns details with sourceRegions and boundingBox |
| `GetMistakeItem_BoundingBoxNull_InResponseStillNull` | a null boundingBox stays null in the response |
| `GetMistakeItem_StudentQueryFails_FallsBackToStudentId` | falls back to the studentId when the student query fails |
| `GetMistakeItem_GrpcException_Returns500` | a gRPC exception returns 500 |
| `GetMistakesByUploadId_Success_ReturnsItemsWithStudentNames` | GetMistakesByUploadId returns the mistake list and student names |
| `GetMistakesByUploadId_GrpcException_Returns500` | a gRPC exception returns 500 |
| `UpdateMistakeItem_Success_ReturnsItemWithStudentName` | UpdateMistakeItem returns the update result and the student name |
| `UpdateMistakeItem_NullFields_SendsEmptyStringAndZeroInGrpcRequest` | null fields become the empty string or 0 in the gRPC request |
| `UpdateMistakeItem_GrpcException_Returns500` | a gRPC exception returns 500 |
| `MigrateImages_AllPathsAlreadyInMistakes_ReturnsMigratedCount0` | migratedCount=0 when every path is already under mistakes/ |
| `MigrateImages_PathsInUploads_CallsMigrateGrpc_ReturnsMigratedCount` | calls the gRPC migration and returns migratedCount when paths are under uploads/ |
| `MigrateImages_SameOldPathMultipleTimes_MigratedOnlyOnce` | the same old path appearing multiple times is migrated only once |
| `MigrateImages_MigrateGrpcThrows_Returns500` | a MigrateImagesToMistake gRPC exception returns 500 |
| `MigrateImages_GetMistakeItemThrows_Returns500` | a GetMistakeItem exception returns 500 |
| `MigrateImages_UpdateMistakeItemThrows_Returns500` | an UpdateMistakeItem exception returns 500 |

## Unit tests — Given-When-Then format

### GetMistakeItems

- **Given** Mistake.GetMistakeItemList returns paged data, **When** GetMistakeItems is called, **Then** it returns 200 and the mistake list including student names
- **Given** Mistake.GetMistakeItemList returns paged data and some student queries fail, **When** GetMistakeItems is called, **Then** the failed students' studentName falls back to the studentId
- **Given** page=0, **When** GetMistakeItems is called, **Then** page is reset to 1
- **Given** size=0, **When** GetMistakeItems is called, **Then** size is reset to 10
- **Given** size=200, **When** GetMistakeItems is called, **Then** size is capped at 100
- **Given** reviewStatus=1 is passed, **When** GetMistakeItems is called, **Then** the gRPC request's ReviewStatus is Confirmed
- **Given** Mistake.GetMistakeItemList throws, **When** GetMistakeItems is called, **Then** it returns 500

### GetMistakeItem

- **Given** Mistake.GetMistakeItem returns data with sourceRegions and boundingBox, **When** GetMistakeItem is called, **Then** it returns 200 and the details including sourceRegions
- **Given** Mistake.GetMistakeItem returns a region whose boundingBox is null, **When** GetMistakeItem is called, **Then** the returned boundingBox is null
- **Given** the student query fails, **When** GetMistakeItem is called, **Then** studentName falls back to the studentId
- **Given** Mistake.GetMistakeItem throws, **When** GetMistakeItem is called, **Then** it returns 500

### GetMistakesByUploadId

- **Given** Mistake.GetMistakeItemsByUpload returns a mistake list, **When** GetMistakesByUploadId is called, **Then** it returns 200 and the mistake list including student names
- **Given** Mistake.GetMistakeItemsByUpload throws, **When** GetMistakesByUploadId is called, **Then** it returns 500

### UpdateMistakeItem

- **Given** Mistake.UpdateMistakeItem returns the updated data, **When** UpdateMistakeItem is called, **Then** it returns 200 and the update result including the student name
- **Given** StudentId/Subject/Grade are null in the request, **When** UpdateMistakeItem is called, **Then** the corresponding gRPC request fields are the empty string or 0
- **Given** Mistake.UpdateMistakeItem throws, **When** UpdateMistakeItem is called, **Then** it returns 500

### MigrateImages

- **Given** every image path is already under mistakes/, **When** MigrateImages is called, **Then** it returns 200 with migratedCount=0
- **Given** some images are under uploads/, **When** MigrateImages is called, **Then** it calls StudentLearning gRPC MigrateImagesToMistake, updates the sourceRegions, and returns migratedCount
- **Given** the same old path appears multiple times, **When** MigrateImages is called, **Then** it migrates only once (using the cache)
- **Given** the MigrateImagesToMistake gRPC throws, **When** MigrateImages is called, **Then** it returns 500
- **Given** GetMistakeItem throws, **When** MigrateImages is called, **Then** it returns 500
- **Given** UpdateMistakeItem (updating sourceRegions) throws, **When** MigrateImages is called, **Then** it returns 500

## Authorization regression tests (implemented)

The test code lives in `Tests/Controllers/ControllerAuthorizationTests.cs`; it verifies through reflection that the controller-level `[Authorize]` attribute exists, preventing accidental removal during refactoring from breaking authorization. The default FallbackPolicy authorization policy cannot replace explicit annotation (project convention: every `/api/admin/*` controller must carry an explicit `[Authorize]`, as the extension point for a later `[Authorize(Roles = "admin")]` tightening).

| Test method | What it verifies |
| --- | --- |
| `MistakeController_HasAuthorizeAttribute` | MistakeController must carry the `[Authorize]` attribute (mistake list/review/migration are administrator operations) |
