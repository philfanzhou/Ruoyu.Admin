# MistakeManagement 测试

## 现有测试覆盖

### MistakeControllerTests（已实现）

| 测试方法 | 验证内容 |
| --- | --- |
| `GetMistakeItems_Success_ReturnsPaginatedItemsWithStudentNames` | GetMistakeItems 返回分页数据和学生姓名 |
| `GetMistakeItems_StudentQueryFails_FallsBackToStudentId` | 学生查询失败时回退为 studentId |
| `GetMistakeItems_Page0_ResetsTo1` | page=0 时重置为 1 |
| `GetMistakeItems_Size0_ResetsTo10` | size=0 时重置为 10 |
| `GetMistakeItems_Size200_ClampedTo100` | size=200 时限制为 100 |
| `GetMistakeItems_ReviewStatus1_SendsConfirmedInGrpcRequest` | reviewStatus=1 时 gRPC 请求为 Confirmed |
| `GetMistakeItems_GrpcException_Returns500` | gRPC 异常返回 500 |
| `GetMistakeItem_Success_ReturnsItemWithSourceRegionsAndBoundingBox` | GetMistakeItem 返回包含 sourceRegions 和 boundingBox 的详情 |
| `GetMistakeItem_BoundingBoxNull_InResponseStillNull` | boundingBox 为 null 时响应中也为 null |
| `GetMistakeItem_StudentQueryFails_FallsBackToStudentId` | 学生查询失败时回退为 studentId |
| `GetMistakeItem_GrpcException_Returns500` | gRPC 异常返回 500 |
| `GetMistakesByUploadId_Success_ReturnsItemsWithStudentNames` | GetMistakesByUploadId 返回错题列表和学生姓名 |
| `GetMistakesByUploadId_GrpcException_Returns500` | gRPC 异常返回 500 |
| `UpdateMistakeItem_Success_ReturnsItemWithStudentName` | UpdateMistakeItem 返回更新结果和学生姓名 |
| `UpdateMistakeItem_NullFields_SendsEmptyStringAndZeroInGrpcRequest` | null 字段在 gRPC 请求中为空字符串或 0 |
| `UpdateMistakeItem_GrpcException_Returns500` | gRPC 异常返回 500 |
| `MigrateImages_AllPathsAlreadyInMistakes_ReturnsMigratedCount0` | 所有路径已在 mistakes/ 下时 migratedCount=0 |
| `MigrateImages_PathsInUploads_CallsMigrateGrpc_ReturnsMigratedCount` | 有路径在 uploads/ 下时调用 gRPC 迁移并返回 migratedCount |
| `MigrateImages_SameOldPathMultipleTimes_MigratedOnlyOnce` | 同一旧路径出现多次时只迁移一次 |
| `MigrateImages_MigrateGrpcThrows_Returns500` | MigrateImagesToMistake gRPC 异常返回 500 |
| `MigrateImages_GetMistakeItemThrows_Returns500` | GetMistakeItem 异常返回 500 |
| `MigrateImages_UpdateMistakeItemThrows_Returns500` | UpdateMistakeItem 异常返回 500 |

## 单元测试 — Given-When-Then 格式

### GetMistakeItems

- **Given** Mistake.GetMistakeItemList 返回分页数据，**When** 调用 GetMistakeItems，**Then** 返回 200 和包含学生姓名的错题列表
- **Given** Mistake.GetMistakeItemList 返回分页数据且部分学生查询失败，**When** 调用 GetMistakeItems，**Then** 失败学生的 studentName 回退为 studentId
- **Given** page=0，**When** 调用 GetMistakeItems，**Then** page 被重置为 1
- **Given** size=0，**When** 调用 GetMistakeItems，**Then** size 被重置为 10
- **Given** size=200，**When** 调用 GetMistakeItems，**Then** size 被限制为 100
- **Given** 传入 reviewStatus=1，**When** 调用 GetMistakeItems，**Then** gRPC 请求的 ReviewStatus 为 Confirmed
- **Given** Mistake.GetMistakeItemList 抛出异常，**When** 调用 GetMistakeItems，**Then** 返回 500

### GetMistakeItem

- **Given** Mistake.GetMistakeItem 返回包含 sourceRegions 和 boundingBox 的数据，**When** 调用 GetMistakeItem，**Then** 返回 200 和包含 sourceRegions 的详情
- **Given** Mistake.GetMistakeItem 返回 boundingBox 为 null 的 region，**When** 调用 GetMistakeItem，**Then** 返回的 boundingBox 为 null
- **Given** 学生查询失败，**When** 调用 GetMistakeItem，**Then** studentName 回退为 studentId
- **Given** Mistake.GetMistakeItem 抛出异常，**When** 调用 GetMistakeItem，**Then** 返回 500

### GetMistakesByUploadId

- **Given** Mistake.GetMistakeItemsByUpload 返回错题列表，**When** 调用 GetMistakesByUploadId，**Then** 返回 200 和包含学生姓名的错题列表
- **Given** Mistake.GetMistakeItemsByUpload 抛出异常，**When** 调用 GetMistakesByUploadId，**Then** 返回 500

### UpdateMistakeItem

- **Given** Mistake.UpdateMistakeItem 返回更新后的数据，**When** 调用 UpdateMistakeItem，**Then** 返回 200 和包含学生姓名的更新结果
- **Given** 请求中 StudentId/Subject/Grade 为 null，**When** 调用 UpdateMistakeItem，**Then** gRPC 请求中对应字段为空字符串或 0
- **Given** Mistake.UpdateMistakeItem 抛出异常，**When** 调用 UpdateMistakeItem，**Then** 返回 500

### MigrateImages

- **Given** 所有图片路径已在 mistakes/ 下，**When** 调用 MigrateImages，**Then** 返回 200，migratedCount=0
- **Given** 有图片在 uploads/ 下，**When** 调用 MigrateImages，**Then** 调用 StudentLearning gRPC MigrateImagesToMistake，更新 sourceRegions，返回 migratedCount
- **Given** 同一旧路径出现多次，**When** 调用 MigrateImages，**Then** 只迁移一次（使用缓存）
- **Given** MigrateImagesToMistake gRPC 抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
- **Given** GetMistakeItem 抛出异常，**When** 调用 MigrateImages，**Then** 返回 500
- **Given** UpdateMistakeItem（更新 sourceRegions）抛出异常，**When** 调用 MigrateImages，**Then** 返回 500

## 鉴权回归测试（已实现）

测试代码位于 `Tests/Controllers/ControllerAuthorizationTests.cs`，通过反射验证 controller 级 `[Authorize]` 属性存在，防止重构时误删导致鉴权失效。FallbackPolicy 兜底不足以替代显式标注（项目约定：所有 `/api/admin/*` controller 必须显式 `[Authorize]`，作为后续 `[Authorize(Roles = "admin")]` 收紧的扩展点）。

| 测试方法 | 验证内容 |
| --- | --- |
| `MistakeController_HasAuthorizeAttribute` | MistakeController 必须携带 `[Authorize]` 属性（错题列表/审核/迁移为管理员操作） |
