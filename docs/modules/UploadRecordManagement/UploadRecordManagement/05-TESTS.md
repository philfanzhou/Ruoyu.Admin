# UploadRecordManagement 测试

## 现有测试覆盖

### OssUploadRecordControllerTests（已实现）

| 测试方法 | 验证内容 |
| --- | --- |
| `GetAllUploadRecords_Success_ReturnsPagedDataWithStudentNames` | GetAllUploadRecords 返回分页数据和学生姓名 |
| `GetAllUploadRecords_StudentQueryFails_FallbackToStudentId` | 学生查询失败时回退为 studentId |
| `GetAllUploadRecords_GrpcException_Returns500` | gRPC 异常返回 500 |
| `GetAllUploadRecords_StatusNegativeOne_SendsUnspecifiedInRequest` | status=-1 时 gRPC 请求为 Unspecified |
| `GetAllUploadRecords_StatusOne_SendsUploadedInRequest` | status=1 时 gRPC 请求为 Uploaded |
| `ResetUploadRecordStatus_Success_Returns200` | 重置上传记录状态成功 |
| `ResetUploadRecordStatus_GrpcSuccessFalse_Returns400` | gRPC 返回 Success=false 时返回 400 |
| `ResetUploadRecordStatus_GrpcException_Returns500` | gRPC 异常返回 500 |
| `AssignUploadRecord_Success_AllAssignmentsValid` | 所有 assignment 有效时成功 |
| `AssignUploadRecord_RecordNotFound_Returns400` | 上传记录不存在返回 400 |
| `AssignUploadRecord_SomeImageIndicesOutOfRange_SkipsWithWarnings` | 部分图片索引超出范围时跳过并返回 warnings |
| `AssignUploadRecord_SubmitMistakeFails_ReturnsAllSuccessFalseWithWarnings` | SubmitMistake 失败时 allSuccess=false 并返回 warnings |
| `AssignUploadRecord_GrpcException_Returns500` | gRPC 异常返回 500 |
| `Assign_EmptyImageIndices_SkipsAssignmentWithWarning` | imageIndices 空列表跳过并返回 warning |
| `Assign_DuplicateImageIndices_SubmitsOnceWithDuplicatedPaths` | 重复索引提交包含重复路径 |
| `Assign_OutOfRangeImageIndices_SkipsWithWarning` | 越界索引跳过并返回 warning |
| `Assign_NegativeImageIndices_SkipsInvalidIndices` | 负数索引跳过并返回 warning |
| `Assign_MixedValidAndInvalidIndices_OnlySubmitsValidOnes` | 混合索引仅提交有效路径 |
| `Assign_AllImagesAssigned_RemainingImageCountIsZero` | 全部分配后 remainingImageCount=0 |
| `Assign_PartialAssignment_RemainingImageCountReflectsUnassigned` | 部分分配 remainingImageCount 反映未分配数 |
| `Assign_MultipleAssignmentsWithOverlap_RemainingImageCountDeduplicates` | 重叠索引 remainingImageCount 去重计算 |
| `Assign_SubmitFails_ReturnsAllSuccessFalseWithWarnings` | SubmitMistake 失败 allSuccess=false |
| `Assign_RecordFetchFails_Returns500` | GetUploadRecord 异常返回 500 |
| `Assign_MultipleAssignments_PartialFailure_ContinuesWithRemaining` | 部分失败继续处理剩余 assignment |
| `GetImage_EmptyPath_Returns400` | path 为空返回 400 |
| `GetImage_ImageExists_Returns200WithFileContent` | 图片存在返回 200 和文件内容 |
| `GetImage_NullStream_Returns404` | OSS 流为 null 返回 404 |
| `GetImage_OssServiceException_Returns500` | OssService 异常返回 500 |
| `RotateImage_InvalidGuid_Returns400` | id 不是合法 GUID 返回 400 |
| `RotateImage_EmptyStudentId_Returns400` | StudentId 为空返回 400 |
| `RotateImage_GrpcSuccess_Returns200` | gRPC 成功返回 200 |
| `RotateImage_GrpcSuccessFalse_Returns400` | gRPC 返回 Success=false 时返回 400 |
| `RotateImage_GrpcException_Returns500` | gRPC 异常返回 500 |
| `RemoveImageFromRecord_EmptyStudentId_Returns400` | studentId 为空返回 400 |
| `RemoveImageFromRecord_GrpcSuccess_Returns200WithDetails` | gRPC 成功返回 200 和详情 |
| `RemoveImageFromRecord_GrpcSuccessFalse_Returns400` | gRPC 返回 Success=false 时返回 400 |
| `RemoveImageFromRecord_GrpcException_Returns500` | gRPC 异常返回 500 |
| `AnalyzeUploadRecord_Success_Returns200WithAnalysisResults` | 分析上传记录成功返回 200 和分析结果 |
| `AnalyzeUploadRecord_GrpcException_Returns500` | gRPC 异常返回 500 |

### 鉴权回归测试（已实现）

测试代码位于 `Tests/Controllers/ControllerAuthorizationTests.cs`，通过反射验证 controller 级 `[Authorize]` 属性存在，防止重构时误删导致鉴权失效。FallbackPolicy 兜底不足以替代显式标注（项目约定：所有 `/api/admin/*` controller 必须显式 `[Authorize]`，作为后续 `[Authorize(Roles = "admin")]` 收紧的扩展点）。

| 测试方法 | 验证内容 |
| --- | --- |
| `OssUploadRecordController_HasAuthorizeAttribute` | OssUploadRecordController 必须携带 `[Authorize]` 属性（图片下载、状态重置、分配、旋转、遗留清理均为管理员操作） |

## 单元测试 — Given-When-Then 格式

### GetAllUploadRecords

- **Given** StudentManagement.GetAllUploadRecords 返回分页数据，**When** 调用 GetAllUploadRecords，**Then** 返回 200 和包含学生姓名的记录列表
- **Given** StudentManagement.GetAllUploadRecords 返回分页数据且部分学生查询失败，**When** 调用 GetAllUploadRecords，**Then** 失败学生的 studentName 回退为 studentId
- **Given** StudentManagement.GetAllUploadRecords 抛出异常，**When** 调用 GetAllUploadRecords，**Then** 返回 500
- **Given** 传入 status=-1，**When** 调用 GetAllUploadRecords，**Then** gRPC 请求的 Status 为 Unspecified
- **Given** 传入 status=1，**When** 调用 GetAllUploadRecords，**Then** gRPC 请求的 Status 为 Uploaded

> 注：GetAllUploadRecords 当前仍使用 `StudentManagementGrpcService`（Admin 专用接口），待 Task 4.6 迁移至通用接口后更新测试。

### ResetUploadRecordStatus

- **Given** StudentLearning.ResetUploadRecordStatus 返回 Success=true，**When** 调用 ResetUploadRecordStatus，**Then** 返回 200 和 success=true
- **Given** StudentLearning.ResetUploadRecordStatus 返回 Success=false，**When** 调用 ResetUploadRecordStatus，**Then** 返回 400
- **Given** StudentLearning.ResetUploadRecordStatus 抛出异常，**When** 调用 ResetUploadRecordStatus，**Then** 返回 500

### AssignUploadRecord

- **Given** 上传记录存在且所有 assignment 图片索引有效，**When** 调用 AssignUploadRecord，**Then** 为每个 assignment 调用 SubmitMistakeUpload，调用 MarkUploadRecordCompleted，返回 200
- **Given** 上传记录不存在，**When** 调用 AssignUploadRecord，**Then** 返回 400
- **Given** 部分 assignment 的图片索引超出范围，**When** 调用 AssignUploadRecord，**Then** 跳过无效 assignment，返回 warnings
- **Given** SubmitMistakeUpload 返回失败，**When** 调用 AssignUploadRecord，**Then** 记录警告，allSuccess=false
- **Given** gRPC 调用抛出异常，**When** 调用 AssignUploadRecord，**Then** 返回 500

#### Assignment 解析 — imageIndices 边界校验

- **Given** assignment 的 imageIndices 为空列表，**When** 调用 AssignUploadRecord，**Then** 跳过该 assignment，返回 warning 包含 "No valid images"，SubmitMistakeUpload 未被调用
- **Given** assignment 的 imageIndices 包含重复索引（如 [0, 0]），**When** 调用 AssignUploadRecord，**Then** SubmitMistakeUpload 被调用一次，imagePaths 包含重复路径
- **Given** assignment 的 imageIndices 全部越界（如 [5, 10]，记录仅 2 张图片），**When** 调用 AssignUploadRecord，**Then** 跳过该 assignment，返回 warning 包含越界索引，SubmitMistakeUpload 未被调用
- **Given** assignment 的 imageIndices 包含负数（如 [-1, -2]），**When** 调用 AssignUploadRecord，**Then** 跳过无效索引，返回 warning
- **Given** assignment 的 imageIndices 混合有效和无效索引（如 [0, 5, 2]，记录有 3 张图片），**When** 调用 AssignUploadRecord，**Then** 仅提交有效索引对应的图片路径

#### remainingImageCount 计算

- **Given** 所有图片均已分配，**When** 调用 AssignUploadRecord，**Then** remainingImageCount=0，totalImageCount=记录图片总数
- **Given** 部分图片已分配（如 3 张中分配 1 张），**When** 调用 AssignUploadRecord，**Then** remainingImageCount=2，totalImageCount=3
- **Given** 多个 assignment 包含重叠索引（同一张图片被多个 assignment 引用），**When** 调用 AssignUploadRecord，**Then** remainingImageCount 按去重后的已分配图片数计算

#### 分配状态校验

- **Given** SubmitMistakeUpload 返回 Success=false，**When** 调用 AssignUploadRecord，**Then** allSuccess=false，warnings 包含错误信息
- **Given** GetUploadRecord gRPC 调用抛出异常，**When** 调用 AssignUploadRecord，**Then** 返回 500
- **Given** 多个 assignment 中部分 SubmitMistakeUpload 失败，**When** 调用 AssignUploadRecord，**Then** 继续处理剩余 assignment，allSuccess=false，remainingImageCount 仅计入成功分配的图片

### GetImage

- **Given** path 参数为空，**When** 调用 GetImage，**Then** 返回 400
- **Given** OSS 中存在该图片，**When** 调用 GetImage，**Then** 返回图片二进制流和正确的 Content-Type
- **Given** OSS 中不存在该图片，**When** 调用 GetImage，**Then** 返回 404
- **Given** IOssService 抛出异常，**When** 调用 GetImage，**Then** 返回 500

### RotateImage

- **Given** id 不是合法 GUID，**When** 调用 RotateImage，**Then** 返回 400
- **Given** StudentId 为空，**When** 调用 RotateImage，**Then** 返回 400
- **Given** StudentLearning.RotateUploadImage 返回 Success=true，**When** 调用 RotateImage，**Then** 返回 200
- **Given** StudentLearning.RotateUploadImage 返回 Success=false，**When** 调用 RotateImage，**Then** 返回 400
- **Given** gRPC 调用抛出异常，**When** 调用 RotateImage，**Then** 返回 500

### RemoveImageFromRecord

- **Given** studentId 为空，**When** 调用 RemoveImageFromRecord，**Then** 返回 400
- **Given** StudentLearning.RemoveImageFromRecord 返回 Success=true，**When** 调用 RemoveImageFromRecord，**Then** 返回 200 和 recordDeleted/remainingImageCount
- **Given** StudentLearning.RemoveImageFromRecord 返回 Success=false，**When** 调用 RemoveImageFromRecord，**Then** 返回 400
- **Given** gRPC 调用抛出异常，**When** 调用 RemoveImageFromRecord，**Then** 返回 500

### AnalyzeUploadRecord

- **Given** StudentManagement.AnalyzeUploadRecord 返回分析结果，**When** 调用 AnalyzeUploadRecord，**Then** 返回 200 和分组信息
- **Given** StudentManagement.AnalyzeUploadRecord 抛出异常，**When** 调用 AnalyzeUploadRecord，**Then** 返回 500

> 注：AnalyzeUploadRecord 当前仍使用 `StudentManagementGrpcService`（Admin 专用接口），待 Task 4.6 迁移至通用接口后更新测试。
