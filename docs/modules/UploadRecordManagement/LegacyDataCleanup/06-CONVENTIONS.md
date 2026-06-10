# LegacyDataCleanup 约定

## 命名约定

- 端点路由：`legacy-check/{id}` 和 `legacy-clean/{id}`（kebab-case）
- ReviewerId 固定值：`"admin-legacy-cleanup"`（标识由管理员遗留清理流程触发的审核完成操作）

## 错误处理约定

- 所有 endpoint 使用 try-catch 包裹，异常统一返回 500 + `ErrorResponse`
- gRPC 返回 `Success=false` 时返回 400 + `ErrorResponse`
- 前置条件不满足时返回 400 + 中文描述消息
- `ErrorResponse` 格式：`{ "message": "错误描述" }`

## 日志约定

- 使用 `ILogger<OssUploadRecordController>` 记录日志
- 信息日志记录每个步骤的执行：
  - `Starting legacy data cleanup for upload record: {RecordId}`
  - `Step 1: Calling CompleteUploadReview for {RecordId}`
  - `Step 2: Removing {Count} image paths from upload record {RecordId}`
  - `Step 3: Calling DeleteUploadRecordAfterReview for {RecordId}`
  - `Legacy data cleanup completed successfully for {RecordId}`
- 警告日志记录步骤失败：
  - `CompleteUploadReview failed for {RecordId}: {Message}`
  - `DeleteUploadRecordAfterReview failed for {RecordId}: {Message}`
- 日志消息使用英文

## 流程约定

### LegacyCheck 只读操作
- 不修改任何数据
- 仅查询 Mistake 服务获取关联错题状态

### LegacyClean 多步骤事务
- 严格按步骤顺序执行：验证 → CompleteUploadReview → RemoveImagesFromRecord(可选) → DeleteUploadRecordAfterReview
- 步骤间无事务保证：若中间步骤失败，已执行的步骤不会回滚 [推断]
- RemoveImagesFromRecord 为可选步骤，仅当 CompleteUploadReview 返回 RemovedImagePaths 时执行

## 中文消息约定

- LegacyCheck/LegacyClean 的面向用户的 message 使用中文
- 日志消息使用英文
- ErrorResponse 中的 message 混合使用：前置条件验证用中文，gRPC 失败用英文（来自 gRPC 响应）

## 判断逻辑约定

- `allReviewed`：`mistakeList.Items.All(i => i.ReviewStatus == Confirmed || i.ReviewStatus == Rejected)`
- `hasPending`：`mistakeList.Items.Any(i => i.ReviewStatus == PendingReview)`
- 两个条件同时检查：`!allReviewed || hasPending` 时拒绝清理
- `hasUploadPathImage`：检查 `sourceRegions` 中是否有 `SourceImagePath.StartsWith("uploads/")`
