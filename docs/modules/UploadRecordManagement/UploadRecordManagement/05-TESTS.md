# UploadRecordManagement Tests

## Existing test coverage

### OssUploadRecordControllerTests (implemented)

| Test method | What it verifies |
| --- | --- |
| `GetAllUploadRecords_Success_ReturnsPagedDataWithStudentNames` | GetAllUploadRecords returns paged data and student names |
| `GetAllUploadRecords_StudentQueryFails_FallbackToStudentId` | falls back to the studentId when the student query fails |
| `GetAllUploadRecords_GrpcException_Returns500` | a gRPC exception returns 500 |
| `GetAllUploadRecords_StatusNegativeOne_SendsUnspecifiedInRequest` | status=-1 sends Unspecified in the gRPC request |
| `GetAllUploadRecords_StatusOne_SendsUploadedInRequest` | status=1 sends Uploaded in the gRPC request |
| `ResetUploadRecordStatus_Success_Returns200` | resetting an upload record's status succeeds |
| `ResetUploadRecordStatus_GrpcSuccessFalse_Returns400` | a gRPC Success=false returns 400 |
| `ResetUploadRecordStatus_GrpcException_Returns500` | a gRPC exception returns 500 |
| `AssignUploadRecord_Success_AllAssignmentsValid` | succeeds when every assignment is valid |
| `AssignUploadRecord_RecordNotFound_Returns400` | a missing upload record returns 400 |
| `AssignUploadRecord_SomeImageIndicesOutOfRange_SkipsWithWarnings` | out-of-range image indices are skipped with warnings |
| `AssignUploadRecord_SubmitMistakeFails_ReturnsAllSuccessFalseWithWarnings` | a SubmitMistake failure yields allSuccess=false with warnings |
| `AssignUploadRecord_GrpcException_Returns500` | a gRPC exception returns 500 |
| `Assign_EmptyImageIndices_SkipsAssignmentWithWarning` | an empty imageIndices list skips the assignment with a warning |
| `Assign_DuplicateImageIndices_SubmitsOnceWithDuplicatedPaths` | duplicate indices submit once with duplicated paths |
| `Assign_OutOfRangeImageIndices_SkipsWithWarning` | out-of-range indices are skipped with a warning |
| `Assign_NegativeImageIndices_SkipsInvalidIndices` | negative indices are skipped with a warning |
| `Assign_MixedValidAndInvalidIndices_OnlySubmitsValidOnes` | mixed indices submit only the valid paths |
| `Assign_AllImagesAssigned_RemainingImageCountIsZero` | remainingImageCount=0 after assigning everything |
| `Assign_PartialAssignment_RemainingImageCountReflectsUnassigned` | a partial assignment's remainingImageCount reflects the unassigned count |
| `Assign_MultipleAssignmentsWithOverlap_RemainingImageCountDeduplicates` | overlapping indices compute remainingImageCount after deduplication |
| `Assign_SubmitFails_ReturnsAllSuccessFalseWithWarnings` | a SubmitMistake failure yields allSuccess=false |
| `Assign_RecordFetchFails_Returns500` | a GetUploadRecord exception returns 500 |
| `Assign_MultipleAssignments_PartialFailure_ContinuesWithRemaining` | a partial failure continues with the remaining assignments |
| `GetImage_EmptyPath_Returns400` | an empty path returns 400 |
| `GetImage_ImageExists_Returns200WithFileContent` | an existing image returns 200 with the file content |
| `GetImage_NullStream_Returns404` | a null OSS stream returns 404 |
| `GetImage_OssServiceException_Returns500` | an OssService exception returns 500 |
| `RotateImage_InvalidGuid_Returns400` | an illegal GUID id returns 400 |
| `RotateImage_EmptyStudentId_Returns400` | an empty StudentId returns 400 |
| `RotateImage_GrpcSuccess_Returns200` | a gRPC success returns 200 |
| `RotateImage_GrpcSuccessFalse_Returns400` | a gRPC Success=false returns 400 |
| `RotateImage_GrpcException_Returns500` | a gRPC exception returns 500 |
| `RemoveImageFromRecord_EmptyStudentId_Returns400` | an empty studentId returns 400 |
| `RemoveImageFromRecord_GrpcSuccess_Returns200WithDetails` | a gRPC success returns 200 with the details |
| `RemoveImageFromRecord_GrpcSuccessFalse_Returns400` | a gRPC Success=false returns 400 |
| `RemoveImageFromRecord_GrpcException_Returns500` | a gRPC exception returns 500 |
| `AnalyzeUploadRecord_Success_Returns200WithAnalysisResults` | a successful analysis returns 200 with the analysis results |
| `AnalyzeUploadRecord_GrpcException_Returns500` | a gRPC exception returns 500 |

### Authorization regression tests (implemented)

The test code lives in `Tests/Controllers/ControllerAuthorizationTests.cs`; it verifies through reflection that the controller-level `[Authorize]` attribute exists, preventing accidental removal during refactoring from breaking authorization. The default FallbackPolicy authorization policy cannot replace explicit annotation (project convention: every `/api/admin/*` controller must carry an explicit `[Authorize]`, as the extension point for a later `[Authorize(Roles = "admin")]` tightening).

| Test method | What it verifies |
| --- | --- |
| `OssUploadRecordController_HasAuthorizeAttribute` | OssUploadRecordController must carry the `[Authorize]` attribute (image download, status reset, assignment, rotation, and legacy cleanup are all administrator operations) |

## Unit tests — Given-When-Then format

### GetAllUploadRecords

- **Given** StudentManagement.GetAllUploadRecords returns paged data, **When** GetAllUploadRecords is called, **Then** it returns 200 and the record list including student names
- **Given** StudentManagement.GetAllUploadRecords returns paged data and some student queries fail, **When** GetAllUploadRecords is called, **Then** the failed students' studentName falls back to the studentId
- **Given** StudentManagement.GetAllUploadRecords throws, **When** GetAllUploadRecords is called, **Then** it returns 500
- **Given** status=-1 is passed, **When** GetAllUploadRecords is called, **Then** the gRPC request's Status is Unspecified
- **Given** status=1 is passed, **When** GetAllUploadRecords is called, **Then** the gRPC request's Status is Uploaded

> Note: GetAllUploadRecords still uses `StudentManagementGrpcService` (an Admin-only interface); update the tests after the migration to the generic interface in Task 4.6.

### ResetUploadRecordStatus

- **Given** StudentLearning.ResetUploadRecordStatus returns Success=true, **When** ResetUploadRecordStatus is called, **Then** it returns 200 with success=true
- **Given** StudentLearning.ResetUploadRecordStatus returns Success=false, **When** ResetUploadRecordStatus is called, **Then** it returns 400
- **Given** StudentLearning.ResetUploadRecordStatus throws, **When** ResetUploadRecordStatus is called, **Then** it returns 500

### AssignUploadRecord

- **Given** the upload record exists and every assignment's image indices are valid, **When** AssignUploadRecord is called, **Then** SubmitMistakeUpload is called for each assignment, MarkUploadRecordCompleted is called, and it returns 200
- **Given** the upload record does not exist, **When** AssignUploadRecord is called, **Then** it returns 400
- **Given** some assignments' image indices are out of range, **When** AssignUploadRecord is called, **Then** the invalid assignments are skipped with warnings
- **Given** SubmitMistakeUpload returns failure, **When** AssignUploadRecord is called, **Then** a warning is recorded and allSuccess=false
- **Given** the gRPC call throws, **When** AssignUploadRecord is called, **Then** it returns 500

#### Assignment parsing — imageIndices boundary checks

- **Given** an assignment's imageIndices is an empty list, **When** AssignUploadRecord is called, **Then** the assignment is skipped with a warning containing "No valid images", and SubmitMistakeUpload is not called
- **Given** an assignment's imageIndices contains duplicate indices (for example [0, 0]), **When** AssignUploadRecord is called, **Then** SubmitMistakeUpload is called once and imagePaths contains the duplicated paths
- **Given** an assignment's imageIndices are all out of range (for example [5, 10] with a 2-image record), **When** AssignUploadRecord is called, **Then** the assignment is skipped with a warning containing the out-of-range indices, and SubmitMistakeUpload is not called
- **Given** an assignment's imageIndices contains negatives (for example [-1, -2]), **When** AssignUploadRecord is called, **Then** the invalid indices are skipped with a warning
- **Given** an assignment's imageIndices mixes valid and invalid indices (for example [0, 5, 2] with a 3-image record), **When** AssignUploadRecord is called, **Then** only the valid indices' image paths are submitted

#### remainingImageCount computation

- **Given** all images are assigned, **When** AssignUploadRecord is called, **Then** remainingImageCount=0 and totalImageCount equals the record's total image count
- **Given** some images are assigned (1 of 3), **When** AssignUploadRecord is called, **Then** remainingImageCount=2 and totalImageCount=3
- **Given** multiple assignments contain overlapping indices (the same image referenced by several assignments), **When** AssignUploadRecord is called, **Then** remainingImageCount counts the assigned images after deduplication

#### Assignment status checks

- **Given** SubmitMistakeUpload returns Success=false, **When** AssignUploadRecord is called, **Then** allSuccess=false and warnings contains the error message
- **Given** the GetUploadRecord gRPC call throws, **When** AssignUploadRecord is called, **Then** it returns 500
- **Given** SubmitMistakeUpload fails for some of several assignments, **When** AssignUploadRecord is called, **Then** the remaining assignments are still processed, allSuccess=false, and remainingImageCount counts only the successfully assigned images

### GetImage

- **Given** the path parameter is empty, **When** GetImage is called, **Then** it returns 400
- **Given** the image exists in OSS, **When** GetImage is called, **Then** it returns the image binary stream with the correct Content-Type
- **Given** the image does not exist in OSS, **When** GetImage is called, **Then** it returns 404
- **Given** IOssService throws, **When** GetImage is called, **Then** it returns 500

### RotateImage

- **Given** id is not a legal GUID, **When** RotateImage is called, **Then** it returns 400
- **Given** StudentId is empty, **When** RotateImage is called, **Then** it returns 400
- **Given** StudentLearning.RotateUploadImage returns Success=true, **When** RotateImage is called, **Then** it returns 200
- **Given** StudentLearning.RotateUploadImage returns Success=false, **When** RotateImage is called, **Then** it returns 400
- **Given** the gRPC call throws, **When** RotateImage is called, **Then** it returns 500

### RemoveImageFromRecord

- **Given** studentId is empty, **When** RemoveImageFromRecord is called, **Then** it returns 400
- **Given** StudentLearning.RemoveImageFromRecord returns Success=true, **When** RemoveImageFromRecord is called, **Then** it returns 200 with recordDeleted/remainingImageCount
- **Given** StudentLearning.RemoveImageFromRecord returns Success=false, **When** RemoveImageFromRecord is called, **Then** it returns 400
- **Given** the gRPC call throws, **When** RemoveImageFromRecord is called, **Then** it returns 500

### AnalyzeUploadRecord

- **Given** StudentManagement.AnalyzeUploadRecord returns analysis results, **When** AnalyzeUploadRecord is called, **Then** it returns 200 with the grouping information
- **Given** StudentManagement.AnalyzeUploadRecord throws, **When** AnalyzeUploadRecord is called, **Then** it returns 500

> Note: AnalyzeUploadRecord still uses `StudentManagementGrpcService` (an Admin-only interface); update the tests after the migration to the generic interface in Task 4.6.
