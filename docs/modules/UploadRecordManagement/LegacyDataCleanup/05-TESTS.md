# LegacyDataCleanup Tests

## Existing test coverage

Test file: `test/Admin.WebApi.Tests/Controllers/OssUploadRecordControllerLegacyTests.cs`

### LegacyCheck tests

1. **LegacyCheck_NoMistakes_ReturnsNotLegacy**
   - Given the upload record has no associated mistakes (GetMistakeItemsByUpload returns an empty list)
   - When LegacyCheck is called
   - Then it returns 200 with isLegacy=false and message="This record has no associated mistakes"

2. **LegacyCheck_AllReviewed_ReturnsLegacy**
   - Given all the upload record's associated mistakes are reviewed (Confirmed + Rejected), with images under the uploads/ path
   - When LegacyCheck is called
   - Then it returns 200 with isLegacy=true, mistakeCount=2, hasUploadPathImage=true

3. **LegacyCheck_AllReviewed_NoUploadPath_ReturnsLegacyWithNoUploadPath**
   - Given all the associated mistakes are reviewed and the images are already under the mistakes/ path
   - When LegacyCheck is called
   - Then it returns 200 with isLegacy=true and hasUploadPathImage=false

4. **LegacyCheck_HasPendingReview_ReturnsNotLegacy**
   - Given the upload record has some pending-review mistakes
   - When LegacyCheck is called
   - Then it returns 200 with isLegacy=false and pendingCount=1

5. **LegacyCheck_GrpcException_Returns500**
   - Given GetMistakeItemsByUpload throws an RpcException
   - When LegacyCheck is called
   - Then it returns 500

### LegacyClean tests

6. **LegacyClean_NoMistakes_ReturnsBadRequest**
   - Given the upload record has no associated mistakes
   - When LegacyClean is called
   - Then it returns 400

7. **LegacyClean_HasPendingReview_ReturnsBadRequest**
   - Given the upload record has pending-review mistakes
   - When LegacyClean is called
   - Then it returns 400

8. **LegacyClean_AllReviewed_CompleteReviewFails_ReturnsBadRequest**
   - Given all mistakes are reviewed but CompleteUploadReview returns Success=false
   - When LegacyClean is called
   - Then it returns 400

9. **LegacyClean_AllReviewed_Success**
   - Given all mistakes are reviewed and both CompleteUploadReview and DeleteUploadRecordAfterReview succeed
   - When LegacyClean is called
   - Then it returns 200 with success=true and uploadRecordId="test-record-id"

10. **LegacyClean_CompleteReviewSuccess_DeleteFails_ReturnsBadRequest**
    - Given CompleteUploadReview succeeds but DeleteUploadRecordAfterReview returns Success=false
    - When LegacyClean is called
    - Then it returns 400

11. **LegacyClean_GrpcException_Returns500**
    - Given GetMistakeItemsByUpload throws an RpcException
    - When LegacyClean is called
    - Then it returns 500

12. **LegacyClean_WithRemovedImagePaths_CallsRemoveImagesFromRecord**
    - Given all mistakes are reviewed and CompleteUploadReview returns RemovedImagePaths
    - When LegacyClean is called
    - Then RemoveImagesFromRecord is called before DeleteUploadRecordAfterReview

13. **LegacyClean_EmptyRemovedImagePaths_SkipsRemoveImagesFromRecord**
    - Given all mistakes are reviewed and CompleteUploadReview returns empty RemovedImagePaths
    - When LegacyClean is called
    - Then RemoveImagesFromRecord is skipped and DeleteUploadRecordAfterReview is called directly

14. **LegacyClean_EmptyStudentId_ReturnsBadRequest**
    - Given the associated mistakes' StudentId is empty
    - When LegacyClean is called
    - Then it returns 400

15. **LegacyClean_RemoveImagesThrowsException_Returns500**
    - Given RemoveImagesFromRecord throws
    - When LegacyClean is called
    - Then it returns 500
