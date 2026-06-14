# 集成文档

## 集成矩阵

| # | 方向 | 类型 | 目标服务 | 协议 | 地址配置 | 用途 | 使用的 API/方法 |
|---|------|------|----------|------|----------|------|----------------|
| 1 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 学生管理 | `StudentManagementGrpcService`: ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, GetIdentityAccountsByStudentId, LinkIdentityAccountToStudent, UnlinkIdentityAccountFromStudent, GetStudentOpenSubjects, SetStudentOpenSubjects, GetAvailableSubjects, GetRegisteredOssPaths, ListOssObjects, DeleteOssObject, GetAllUploadRecords, AnalyzeUploadRecord |
| 2 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 学习/上传记录 | `StudentLearningGrpcService`: GetUploadRecord, MarkUploadRecordCompleted, RotateUploadImage, RemoveImageFromRecord, RemoveImagesFromRecord, DeleteUploadRecordAfterReview, ResetUploadRecordStatus |
| 3 | 出站 | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (默认 `:5006`) | 错题管理 | `MistakeGrpcService`: GetMistakeItemList, GetMistakeItem, GetMistakeItemsByUpload, UpdateMistakeItem, GetAllReferencedImagePaths, SubmitMistakeUpload, CompleteUploadReview |
| 4 | 出站 | HTTP Proxy | Identity Service | HTTP/JSON | `IdentityService:Address` (默认 `:5002`) | 身份认证代理 | `IdentityProxyMiddleware`: `/api/identity/*` → `{Address}/api/*` |
| 5 | 出站 | HTTP Client | Identity Service | HTTP/JSON | `IdentityService:Address` | 批量查询账户 | `POST {Address}/api/gateway/users/batch` (带 `X-Admin-AppId` + `X-Admin-AppSecret`) |
| 6 | 出站 | HTTP Proxy | Teacher Portal | HTTP/JSON | `TeacherPortal:Address` (默认 `:5004`) | 教师端代理 | `TeacherPortalProxyMiddleware`: `/api/teacher-portal/*` → `{Address}/api/*` (带 `X-Admin-Key`) |
| 7 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 图片预签名 URL | `StudentLearningGrpcService`: GetPresignedUrl |
| 8 | 出站 | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (默认 `:5006`) | 图片预签名 URL | `MistakeGrpcService`: GetPresignedUrl |
| 9 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 图片迁移 | `StudentLearningGrpcService`: MigrateImagesToMistake |
| 10 | 出站 | S3 API | OSS 存储 | S3 / 本地文件 | `Oss:*` 配置 | 审计列表+清理删除 | `IOssService`: ListObjectsAsync, DeleteAsync |
| 11 | 入站 | HTTP | 前端 Web UI | HTTP/JSON | `AdminApi:Port` (默认 `:5020`) | 管理 API | 所有 `/api/admin/*` 路由 |

## 失败语义总结

### gRPC 调用失败

| 场景 | 服务 | 失败处理 | HTTP 响应 |
|------|------|----------|-----------|
| Student Service 不可用 | Student | 审计任务标记为 Failed；Resolve 操作返回 502；CRUD 操作返回 500 | 502 Bad Gateway / 500 Internal Server Error |
| Mistake Service 不可用 | Mistake | 审计任务继续（记录警告，可能产生误报）；Resolve 操作返回 502；查询操作返回 500 | 502 Bad Gateway / 500 Internal Server Error |
| gRPC InvalidArgument | Student/Mistake | 返回客户端错误 | 400 Bad Request |
| gRPC NotFound | Student | 返回资源不存在 | 404 Not Found |
| gRPC 通用异常 | Student/Mistake | 记录日志，返回 500 | 500 Internal Server Error |

### HTTP 代理失败

| 场景 | 服务 | 失败处理 | HTTP 响应 |
|------|------|----------|-----------|
| Identity Service 不可达 | Identity | 返回 502 | `{"message":"Identity service unreachable"}` |
| Teacher Portal 不可达 | Teacher Portal | 返回 502 | `{"message":"Teacher portal service unreachable"}` |
| Teacher Portal 未配置 | Teacher Portal | 返回 503 | `{"message":"Teacher portal not configured"}` |
| Identity 批量查询失败 | Identity | 记录警告，返回空列表 | 200 OK + `[]` |

### OSS 操作失败

| 场景 | 失败处理 | 影响 |
|------|----------|------|
| OSS ListObjects 失败 | 审计任务标记为 Failed | 审计无法完成 |
| OSS DeleteObject 失败（审计清理） | 返回 500 | 审计记录已标记 Resolved 但 OSS 文件未删除 |
| gRPC GetPresignedUrl 失败 | 返回 502 Bad Gateway | 图片无法预览 |
| gRPC MigrateImagesToMistake 失败 | 返回 500 | 图片迁移不完整 |

### 数据库操作失败

| 场景 | 失败处理 | 影响 |
|------|----------|------|
| 审计运行并发写入 | DbUpdateException → 退出当前审计 | 保证同一时间只有一个审计运行 |
| 审计记录 Resolve 后删除失败 | 返回 500 | OSS 文件已删除但审计记录未清理 [推断] |

## 重试策略

当前实现**没有**自动重试机制：

- gRPC 调用：无重试，失败直接返回错误
- HTTP 代理：无重试，失败直接返回 502
- OSS 操作：无重试，失败直接返回错误
- 审计定时任务：失败后等待下一次定时触发（每天一次），或手动触发

## 超时配置

| 连接 | 超时 | 配置方式 |
|------|------|----------|
| Identity HTTP Client | 30 秒 | `HttpClient.Timeout` |
| Teacher Portal HTTP Client | 10 秒 | `HttpClient.Timeout` |
| gRPC Client | 默认 [待确认] | `AddGrpcClient` 默认配置 |
| OSS 操作 | 默认 [待确认] | `IOssService` 实现决定 |
