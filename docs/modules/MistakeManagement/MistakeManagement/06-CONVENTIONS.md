# MistakeManagement 约定

## 命名约定

- Controller 路由：`api/admin/mistakes`（kebab-case）
- 请求模型：PascalCase，定义在 Controller 文件内（如 `UpdateMistakeRequest`）
- 响应字段：camelCase（匿名对象属性）

## 错误处理约定

- 所有 endpoint 使用 try-catch 包裹，异常统一返回 500 + `ErrorResponse`
- `ErrorResponse` 格式：`{ "message": "错误描述" }`
- MigrateImages 中单张图片迁移失败时记录 Error 日志并保留原路径，不中断整体流程

## gRPC 调用约定

- 错题操作通过 `MistakeGrpcServiceClient` 调用
- 学生姓名通过 `StudentManagementGrpcServiceClient.GetStudent` 获取
- 图片迁移通过 `StudentLearningGrpcServiceClient.MigrateImagesToMistake` 调用（不再直接操作 OSS）

## 日志约定

- 使用 `ILogger<MistakeController>` 记录日志
- 错误日志：`_logger.LogError(ex, "Failed to ...")`
- 迁移信息日志：`_logger.LogInformation("Migrated image from {OldPath} to {NewPath}", oldPath, newPath)`
- 迁移错误日志：`_logger.LogError(ex, "Failed to migrate image {Path}", oldPath)`
- 日志消息使用英文

## 分页约定

- GetMistakeItems 支持分页，参数校验：page ≤ 0 → 1，size ≤ 0 → 10，size > 100 → 100
- GetMistakesByUploadId 不支持分页，返回全部关联错题，total 为客户端计数

## 学生姓名获取约定

- 逐个调用 `StudentManagement.GetStudent`（非批量）
- 查询失败时回退显示 studentId
- 空白 studentId 跳过查询

## MigrateImages 约定

- 图片迁移委托给 Student gRPC `MigrateImagesToMistake`，路径映射由服务端处理
- 同一请求内相同路径只迁移一次（pathMapping 缓存）
- 迁移结果处理：成功 → 更新 region.SourceImagePath；失败 → 保留原路径，记录 Error 日志
- 最后调用 UpdateMistakeItem 批量更新所有 sourceRegions（包括未迁移的）

## 响应字段差异约定

- 列表接口（GetMistakeItems、GetMistakesByUploadId）：不返回 sourceRegions，返回 imageCount 和 firstImagePath
- 详情接口（GetMistakeItem）：返回完整 sourceRegions（含 boundingBox）
- 更新接口（UpdateMistakeItem）：返回精简字段（id, studentId, studentName, subject, grade, sourceUploadId, reviewStatus）
