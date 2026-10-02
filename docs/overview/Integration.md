# 集成文档

> **⚠️ 本文档整体过期，尚未重新核对。**
>
> 下面的集成矩阵仍按 **gRPC** 描述，且带有「Phase 4 计划变更（尚未实施）」的历史标注。实际实现已全部改为 HTTP/JSON，gRPC 已整体移除；矩阵也**缺少 Homework（:5009）**这一路下游——`HomeworkReferenceClient` 为 Storage Audit 聚合图片引用，是审计的必需依赖之一。
>
> 当前实现事实以代码为准：`backend/Admin.WebApi/Program.cs`（下游注册）、`backend/Admin.WebApi/Controllers/`（对外端点）、三个 `*ProxyMiddleware.cs`（代理转发）、`backend/Ruoyu.Admin.ServiceClients/`（Student / Mistake 契约镜像）、`backend/Admin.WebApi/Services/HomeworkReferenceClient.cs`。
>
> 下方「失败语义总结」中 Storage Audit 相关的行已按当前代码更正，其余行仍可能过期。重新核对整份矩阵是独立的待办项。

## 可观测性基础（ServiceMantle）

> 本节为当前实现事实（`backend/Admin.WebApi/Program.cs`），随 ServiceMantle 接入（#33）落地。

- **启用能力**：`AddServiceMantle(ServiceId.Parse("ruoyu-admin"), InstanceId.Parse(...))` 注册服务身份与请求日志 scope；`AddOpenTelemetryInstrumentation()` 以默认选项启用 ASP.NET Core / HttpClient / Runtime 三项 instrumentation（无任何 exporter，trace/metric 不出进程）。`UseServiceMantleCorrelationId()` 是管线**第一个中间件**（位于 `UseCors` 之前），CORS、静态文件、SPA 回退、认证、三个代理与全部 `/api` 响应都获得 `x-correlation-id` 响应头（`OnStarting` 注入，静态文件响应同样覆盖）。
- **服务身份**：`ServiceId` 固定为 `ruoyu-admin`（部署级稳定标识，与 Serilog 展示名 `Ruoyu.Admin` 有意区分）。`ServiceVersion` 不显式传入，由入口程序集 informational version 解析（当前 1.0.0）。
- **实例身份与部署语义**：`InstanceId` 形如 `ruoyu-admin-<32 位小写 hex>`，**每次宿主构建新生成、重启即换，不是持久身份**，不得用于跨重启的关联或审计主体。
- **Correlation ID 规则**：入站 `x-correlation-id` 仅在「单值且形状合法」（≤64 字符、ASCII 字母数字加 `.`/`_`/`-`、首字符为字母数字）时逐字回显；缺失、超长、非法字符、逗号拼接或重复头整体丢弃并生成新的 32 位小写 hex 值；请求头永不改写。
- **日志字段交互**：现有 Serilog enricher 顺序（`FromLogContext` 在前、全局 `WithProperty("ServiceName"/"ServiceVersion"/"InstanceId")` 在后）保持不变，全局字段继续覆盖请求 scope 的同名字段，Loki 字段值与既有查询契约不变；新增的 `CorrelationId` 属性经 `FromLogContext` 进入控制台与 Loki 输出。身份字段统一归属见 #35。
- **健康端点**（随 #34 落地，#66 改为共享 EF Core 快照）：`AddServiceMantleHealthEndpoints(options => options.ProbeTimeout = 3s)` 注册健康服务，端点由 `app.MapGroup(string.Empty).AllowAnonymous()` + `MapServiceMantleHealthEndpoints()` 映射为 `GET /health/live`（恒 200 `{"status":"live"}`）、`GET /health/ready` 与 `GET /health`（readiness 别名）。共享 `StartupDatabaseGate` 在 WebHost/worker 启动前执行唯一一次准备与迁移；共享单例 `StartupDatabaseReceipt` 为 NotStarted/Running 时零数据库访问、503 `ruoyu-admin.startup_incomplete`，Failed 时零数据库访问、503 `ruoyu-admin.startup_failed`。Succeeded 后 scoped `EfCoreHealthSnapshotSource<AuditDbContext>` 读取 EF 映射表列的零行 `SELECT`；只证明表列可读，不证明约束、索引或数据正确。PostgreSQL classifier 区分连接失败 `ruoyu-admin.database_unreachable` 与 schema 故障 `ruoyu-admin.schema_unavailable`，未知异常/超时仍由库安全回答 `health.probe_failed`/`health.probe_timeout`。每请求重新采样；下游刻意不参与就绪判定，响应不含连接串/主机/异常文本。匿名路由组与 SPA 排除 `/health` 的谓词保持，`/api/*` 仍要求认证。完整门与兼容说明见 [迁移文档](../database/migrations.md#66-发布兼容说明)，探测约定见 [部署文档](../development/Deployment.md)。
- **Bootstrap 边界**：不传 `bootstrapFilePath`，不注册任何 Bootstrap 数据库提供者、安装或管理能力；启动零磁盘写入。
- **安全响应头基线**（随 #55 落地）：`AddSecurityResponseHeaders()` 注册服务 + `UseServiceMantleSecurityResponseHeaders()`（`UseCors` 之后、静态文件/SPA/会话中间件之前；WebApplication 隐式路由先行，故中间件位置任意）+ `[RequireSecurityResponseHeaders]`/`AdminSecurityResponseHeadersConvention` 标记 10 个 JSON 控制器（AdminAuth、AdminCsrf、AdminOidc、EnumOptions、IdentityAccounts、Mistake、OssAudit、OssUploadRecord、Students、StudentAssociations），并为中间件自有路由（`/api/auth/oidc/callback`、`/api/auth/logout/csrf`、`/api/auth/oidc/logout-callback`）映射仅承载元数据的 marker-only 端点（多方法覆盖，处理器按设计不可达）。被标记端点在响应头未发送时的所有经过路由的响应（成功、业务 4xx、401/403 challenge、5xx、会话边界/登出中间件短路）都带六项固定单值头：`Cache-Control: no-store`、`Pragma: no-cache`、`X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、`Referrer-Policy: no-referrer`、CSP `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`。原 4 处手写头（AdminCsrfController、AdminSessionBoundary.RejectAsync、AdminLogoutMiddleware、UseAdminOidcResponseHeaders 的头部部分）已删除；`UseAdminOidcResponseHeaders` 的 OIDC-disabled 503 门保留为 `UseAdminOidcGate`。刻意不标记：`ImageController`（浏览器原生图片重定向）、三个代理中间件转发的响应、SPA/静态文件、健康端点。
- **exporter 接入边界**：OTLP / Prometheus exporter 的接入留给后续任务（#35 共享日志迁移按其正文扩展）。代理出站请求会新增标准 W3C `traceparent`/`tracestate` 传播头（HttpClient instrumentation 默认行为），下游按未知头忽略。

## 集成矩阵

| # | 方向 | 类型 | 目标服务 | 协议 | 地址配置 | 用途 | 使用的 API/方法 |
|---|------|------|----------|------|----------|------|----------------|
| 1 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 学生管理 | `StudentLearningGrpcService`: ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, GetIdentityAccountsByStudentId, LinkIdentityAccountToStudent, UnlinkIdentityAccountFromStudent, GetStudentOpenSubjects, SetStudentOpenSubjects, GetAvailableSubjects |
| 2 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 学习/上传记录 | `StudentLearningGrpcService`: GetUploadRecord, MarkUploadRecordUnderReview, RotateUploadImage, RemoveImageFromRecord, RemoveImagesFromRecord, DeleteUploadRecordAfterReview, ResetUploadRecordStatus |
| 2a | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 管理端上传记录（待迁移） | `StudentManagementGrpcService`: GetAllUploadRecords, GetStudent, AnalyzeUploadRecord（待 Task 4.6 迁移至通用接口） |
| 3 | 出站 | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (默认 `:5006`) | 错题管理 | `MistakeGrpcService`: GetMistakeItemList, GetMistakeItem, GetMistakeItemsByUpload, UpdateMistakeItem, SubmitMistakeUpload, CompleteUploadReview |
| 4 | 出站 | HTTP Proxy | Identity Service | HTTP/JSON | `IdentityService:Authority` (默认 `:5002`) | 身份认证代理 | `IdentityProxyMiddleware`: `/api/identity/*` → `{Authority}/api/*` |
| 5 | 出站 | HTTP Client | Identity Service | HTTP/JSON | `IdentityService:Authority` | 批量查询账户 | `POST {Authority}/api/gateway/users/batch` (带 `X-Admin-AppId` + `X-Admin-AppSecret`) |
| 6 | 出站 | HTTP Proxy | Teacher Portal | HTTP/JSON | `TeacherPortal:Url` (默认 `:5004`) | 教师端代理 | `TeacherPortalProxyMiddleware`: `/api/teacher-portal/*` → `{Url}/api/*`（透传调用方 `Authorization: Bearer`，下游 `[Authorize(Roles="admin")]` 校验） |
| 6a | 出站 | HTTP Proxy | Assistant Portal | HTTP/JSON | `AssistantPortal:Url` (默认 `:5021`) | 助教端代理 | `AssistantPortalProxyMiddleware`: `/api/assistant-portal/*` → `{Url}/api/*`（透传调用方 `Authorization: Bearer`，下游 `[Authorize(Roles="admin")]` 校验） |
| 7 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 图片预签名 URL | `StudentLearningGrpcService`: GetPresignedUrl |
| 8 | 出站 | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (默认 `:5006`) | 图片预签名 URL | `MistakeGrpcService`: GetPresignedUrl |
| 9 | 出站 | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (默认 `:5005`) | 图片迁移 | `StudentLearningGrpcService`: MigrateImagesToMistake |
| 10 | 出站 | S3 API | OSS 存储 | S3 / 本地文件 | `Oss:*` 配置 | 审计浏览+僵尸清理+迁移辅助 | `IOssService`: ListObjectsAsync, DeleteAsync（自动清理关联缩略图）, CopyObjectAsync, GetPresignedUrl, ObjectExists；无路径前缀校验（审计需访问所有路径前缀） |
| 11 | 入站 | HTTP | 前端 Web UI | HTTP/JSON | `AdminApi:Port` (默认 `:5020`) | 管理 API | 所有 `/api/admin/*` 路由 |

## 失败语义总结

### 下游调用失败

> 实际协议为 HTTP/JSON，小节标题沿用原文以保留链接；下表 Storage Audit 相关行已按当前代码更正。

| 场景 | 服务 | 失败处理 | HTTP 响应 |
|------|------|----------|-----------|
| Student Service 不可用 | Student | 审计任务标记为 Failed（路径聚合不可用）；Resolve / BatchResolve 返回 502 且不删；CRUD 操作返回 500；图片预签名 URL 返回 502；图片迁移返回 500 | 502 Bad Gateway / 500 Internal Server Error |
| Homework Service 不可用 | Homework | 审计任务标记为 Failed（`ErrorMessage="Homework service unavailable"`），在扫描任何桶之前中止；Resolve / BatchResolve 返回 502 且不删（复核的 `GetRegisteredOssPathsAsync` 同时聚合 Student 与 Homework） | 502 Bad Gateway / 500 Internal Server Error |
| Mistake Service 不可用 | Mistake | 审计任务标记为 Failed（引用聚合不完整，不得产出可处置结论）；Resolve / BatchResolve 返回 502 且整批不删；查询操作返回 500；图片预签名 URL 返回 502 | 502 Bad Gateway / 500 Internal Server Error |
| gRPC InvalidArgument | Student/Mistake | 返回客户端错误 | 400 Bad Request |
| gRPC NotFound | Student/Mistake | 返回资源不存在 | 404 Not Found |
| gRPC 通用异常 | Student/Mistake | 记录日志，返回 500 | 500 Internal Server Error |

### HTTP 代理失败

| 场景 | 服务 | 失败处理 | HTTP 响应 |
|------|------|----------|-----------|
| Identity Service 不可达 | Identity | 返回 502 | `{"message":"Identity service unreachable"}` |
| Teacher Portal 不可达 | Teacher Portal | 返回 502 | `{"message":"Teacher portal service unreachable"}` |
| Teacher Portal 未配置 | Teacher Portal | 返回 503 | `{"message":"Teacher portal not configured"}` |
| Assistant Portal 不可达 | Assistant Portal | 返回 502 | `{"message":"Assistant portal service unreachable"}` |
| Assistant Portal 未配置 | Assistant Portal | 返回 503 | `{"message":"Assistant portal not configured"}` |
| Identity 批量查询失败 | Identity | 记录警告，返回空列表 | 200 OK + `[]` |

### OSS 操作失败

| 场景 | 失败处理 | 影响 |
|------|----------|------|
| OSS ListObjects 失败 | 审计任务标记为 Failed | 审计无法完成 |
| OSS DeleteObject 失败（审计清理） | 返回 500 | 审计记录已标记 Resolved 但 OSS 文件未删除 |
| OSS DeleteObject 失败（缩略图清理） | 忽略错误，继续删除原图 | 缩略图可能残留，但不影响原图删除 |
| OSS CopyObject 失败（迁移辅助） | 返回 500 | OSS 对象移动不完整 |
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
| Assistant Portal HTTP Client | 10 秒 | `HttpClient.Timeout` |
| gRPC Client | 默认 [待确认] | `AddGrpcClient` 默认配置 |
| OSS 操作 | 默认 [待确认] | `IOssService` 实现决定 |
