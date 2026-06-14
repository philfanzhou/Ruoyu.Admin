# UploadRecordManagement 约定

## 命名约定

- Controller 路由：`api/admin/oss-upload-records`（kebab-case）
- 请求模型：PascalCase，定义在 Controller 文件内（如 `RotateImageRequest`、`ResetStatusRequest`、`ImageAssignment`、`AssignUploadRecordRequest`）
- 响应字段：camelCase（匿名对象属性）

## 错误处理约定

- 所有 endpoint 使用 try-catch 包裹，异常统一返回 500 + `ErrorResponse`
- gRPC 返回 `Success=false` 时返回 400 + `ErrorResponse`
- 参数校验失败返回 400 + `ErrorResponse`
- `ErrorResponse` 格式：`{ "message": "错误描述" }`

## gRPC 调用约定

- 上传记录操作优先通过 `StudentLearningGrpcServiceClient`（通用接口）调用
- 以下操作仍通过 `StudentManagementGrpcServiceClient`（Admin 专用接口）调用，待 Task 4.6 迁移至通用接口：
  - `GetAllUploadRecords`：管理端分页查询所有上传记录（无需 studentId）
  - `AnalyzeUploadRecord`：管理端 VL 分析（Admin 专用调试接口）
  - `GetStudent`：查询学生姓名（用于 GetAllUploadRecords 返回数据中填充 studentName）
- 错题创建通过 `MistakeGrpcServiceClient` 调用
- 学生姓名通过逐个调用 `StudentManagement.GetStudent` 获取（非批量），查询失败时回退为 studentId
- 图片操作通过 `IOssService` 调用

## 日志约定

- 使用 `ILogger<OssUploadRecordController>` 记录日志
- 错误日志：`_logger.LogError(ex, "Failed to ...: {RecordId}", id)`
- 信息日志：`_logger.LogInformation("Successfully created mistake items {ItemIds} for upload record {RecordId}", ...)`
- 日志消息使用英文

## 数据转换约定

- gRPC Timestamp 到 Unix 秒数：通过 `ParseTimestampToUnixSeconds` 私有方法，支持数字字符串和 ISO 8601 格式
- UploadStatus 枚举：前端传入 int，转换为 `SProto.UploadStatus` 枚举
- Content-Type：根据文件扩展名自动判断，默认 `application/octet-stream`

## 分配流程约定

1. 先获取上传记录（GetUploadRecord）
2. 按图片索引提取路径
3. 逐个 assignment 调用 SubmitMistakeUpload
4. 最后调用 MarkUploadRecordCompleted
5. 返回 createdItems、remainingImageCount、totalImageCount

## 安全约定

- RotateImage 验证 recordId 必须为合法 GUID
- RemoveImageFromRecord 要求 studentId 查询参数
- GetImage 要求 path 查询参数非空
