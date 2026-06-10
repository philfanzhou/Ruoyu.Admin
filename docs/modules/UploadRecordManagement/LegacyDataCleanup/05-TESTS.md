# LegacyDataCleanup 测试

## 现有测试覆盖

测试文件：`test/Admin.WebApi.Tests/Controllers/OssUploadRecordControllerLegacyTests.cs`

### LegacyCheck 测试

1. **LegacyCheck_NoMistakes_ReturnsNotLegacy**
   - Given 上传记录没有关联错题（GetMistakeItemsByUpload 返回空列表）
   - When 调用 LegacyCheck
   - Then 返回 200，isLegacy=false，message="该记录没有关联错题"

2. **LegacyCheck_AllReviewed_ReturnsLegacy**
   - Given 上传记录关联的错题全部已审核（Confirmed + Rejected），且有图片在 uploads/ 路径下
   - When 调用 LegacyCheck
   - Then 返回 200，isLegacy=true，mistakeCount=2，hasUploadPathImage=true

3. **LegacyCheck_AllReviewed_NoUploadPath_ReturnsLegacyWithNoUploadPath**
   - Given 上传记录关联的错题全部已审核，图片已在 mistakes/ 路径下
   - When 调用 LegacyCheck
   - Then 返回 200，isLegacy=true，hasUploadPathImage=false

4. **LegacyCheck_HasPendingReview_ReturnsNotLegacy**
   - Given 上传记录有部分错题待审核
   - When 调用 LegacyCheck
   - Then 返回 200，isLegacy=false，pendingCount=1

5. **LegacyCheck_GrpcException_Returns500**
   - Given GetMistakeItemsByUpload 抛出 RpcException
   - When 调用 LegacyCheck
   - Then 返回 500

### LegacyClean 测试

6. **LegacyClean_NoMistakes_ReturnsBadRequest**
   - Given 上传记录没有关联错题
   - When 调用 LegacyClean
   - Then 返回 400

7. **LegacyClean_HasPendingReview_ReturnsBadRequest**
   - Given 上传记录有待审核的错题
   - When 调用 LegacyClean
   - Then 返回 400

8. **LegacyClean_AllReviewed_CompleteReviewFails_ReturnsBadRequest**
   - Given 所有错题已审核，但 CompleteUploadReview 返回 Success=false
   - When 调用 LegacyClean
   - Then 返回 400

9. **LegacyClean_AllReviewed_Success**
   - Given 所有错题已审核，CompleteUploadReview 和 DeleteUploadRecordAfterReview 均成功
   - When 调用 LegacyClean
   - Then 返回 200，success=true，uploadRecordId="test-record-id"

10. **LegacyClean_CompleteReviewSuccess_DeleteFails_ReturnsBadRequest**
    - Given CompleteUploadReview 成功，但 DeleteUploadRecordAfterReview 返回 Success=false
    - When 调用 LegacyClean
    - Then 返回 400

11. **LegacyClean_GrpcException_Returns500**
    - Given GetMistakeItemsByUpload 抛出 RpcException
    - When 调用 LegacyClean
    - Then 返回 500

## 缺失测试（建议补充）

- **Given** 所有错题已审核且 CompleteUploadReview 返回 RemovedImagePaths，**When** 调用 LegacyClean，**Then** 调用 RemoveImagesFromRecord 后再调用 DeleteUploadRecordAfterReview
- **Given** 所有错题已审核且 CompleteUploadReview 返回空 RemovedImagePaths，**When** 调用 LegacyClean，**Then** 跳过 RemoveImagesFromRecord，直接调用 DeleteUploadRecordAfterReview
- **Given** 关联错题的 StudentId 为空，**When** 调用 LegacyClean，**Then** 返回 400
- **Given** RemoveImagesFromRecord 抛出异常，**When** 调用 LegacyClean，**Then** 返回 500
