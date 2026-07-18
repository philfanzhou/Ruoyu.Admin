# 系统上下文

## 服务定位

Admin Portal 是 Ruoyu.Study 平台的**管理后台服务**，为管理员提供统一的学生管理、错题管理、OSS 审计和上传记录管理等操作入口。它本身不持有核心业务数据，而是作为聚合层代理和编排下游微服务。

**核心职责**：
- 学生 CRUD 及身份账户关联管理
- 错题记录查询、更新和图片迁移
- OSS 僵尸对象审计（定时 + 手动触发）
- 上传记录查看、分配、旋转、移除、遗留数据清理
- 身份服务和教师门户的 HTTP 反向代理
- 枚举选项聚合查询

## 系统上下文图

```
                    ┌─────────────────────────┐
                    │     管理员 (Admin)       │
                    │  通过浏览器访问管理后台    │
                    └───────────┬─────────────┘
                                │ HTTPS
                                ▼
                    ┌─────────────────────────┐
                    │   Nginx 容器             │
                    │   前端静态文件 + 反向代理  │
                    │   /oss/ → SeaweedFS      │
                    └───────────┬─────────────┘
                                │ HTTP/JSON
                                ▼
┌───────────────────────────────────────────────────────────┐
│                    Admin Portal API                        │
│                  (ASP.NET Core 8.0)                        │
│                   Port: 5020                               │
│                                                           │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│  │ Controllers │  │  Background  │  │   Middlewares   │  │
│  │  (REST API) │  │   Services   │  │  (Proxy)        │  │
│  └──────┬──────┘  └──────┬───────┘  └────────┬────────┘  │
│         │                │                    │           │
│  ┌──────┴────────────────┴────────────────────┴────────┐  │
│  │              AuditDbContext (EF Core)                │  │
│  │           OssAuditRecords / OssAuditRuns            │  │
│  └──────────────────────┬──────────────────────────────┘  │
│                         │                                  │
│  ┌──────────────────────┴──────────────────────────────┐  │
│  │              IOssService (S3 / Local)                │  │
│  │         [仅用于审计 List + 清理 Delete]              │  │
│  └─────────────────────────────────────────────────────┘  │
└───────────┬──────────────────┬──────────────┬─────────────┘
            │ gRPC             │ gRPC         │ HTTP Proxy
            │ :5005            │ :5006        │
            ▼                  ▼              ▼
   ┌────────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
   │ Student Service│ │Mistake Service│ │Identity Svc  │ │Teacher Portal│
   │ (gRPC :5005)  │ │(gRPC :5006)  │ │(HTTP :5002)  │ │(HTTP :5004)  │
   └────────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
```

## 上游（调用方）

### 管理员浏览器 → Nginx 容器

| 属性 | 值 |
|------|------|
| 调用方 | 管理员浏览器 |
| 协议 | HTTPS |
| 说明 | 管理员通过浏览器访问 Nginx 容器，Nginx 提供前端静态文件服务和 API/OSS 反向代理 |

### Nginx 容器 → Admin Portal API

| 属性 | 值 |
|------|------|
| 调用方 | Nginx 容器 |
| 协议 | HTTP/JSON |
| 路由前缀 | `/api/admin/*` |
| 说明 | Nginx 将 API 请求代理到 Admin Portal 后端 |

### Nginx 容器 → SeaweedFS（OSS 代理）

| 属性 | 值 |
|------|------|
| 调用方 | Nginx 容器 |
| 协议 | HTTP |
| 路由前缀 | `/oss/*` |
| 代理目标 | `http://ruoyu-seaweedfs:8333` |
| 路径重写 | strip `/oss/` 前缀 |
| 说明 | 前端通过 Nginx 代理访问 OSS 预签名 URL 中的对象，`proxy_set_header Host ruoyu-seaweedfs:8333` 确保 S3 签名验证通过 |

## 下游（被调用方）

### Student Service（gRPC）

| 属性 | 值 |
|------|------|
| 服务名 | StudentManagementGrpcService / StudentLearningGrpcService |
| 协议 | gRPC (HTTP/2, h2c) |
| 默认地址 | `http://localhost:5005` |
| 配置键 | `StudentGrpcService:Address` |
| 使用的 gRPC 方法 | `ListStudents`, `GetStudent`, `CreateStudent`, `UpdateStudent`, `DeleteStudent`, `GetIdentityAccountsByStudentId`, `LinkIdentityAccountToStudent`, `UnlinkIdentityAccountFromStudent`, `GetStudentOpenSubjects`, `SetStudentOpenSubjects`, `GetAvailableSubjects`, `GetRegisteredOssPaths`, `ListOssObjects`, `DeleteOssObject`, `GetAllUploadRecords`, `GetUploadRecord`, `MarkUploadRecordCompleted`, `RotateUploadImage`, `RemoveImageFromRecord`, `RemoveImagesFromRecord`, `DeleteUploadRecordAfterReview`, `AnalyzeUploadRecord`, `GetPresignedUrl`, `MigrateImagesToMistake` |
| 不可用时行为 | gRPC `StatusCode.Unavailable` → 返回 500/502；审计任务中止（Student 服务不可用时标记为 Failed） |

### Mistake Service（gRPC）

| 属性 | 值 |
|------|------|
| 服务名 | MistakeGrpcService |
| 协议 | gRPC (HTTP/2, h2c) |
| 默认地址 | `http://localhost:5006` |
| 配置键 | `MistakeGrpcService:Address` |
| 使用的 gRPC 方法 | `GetMistakeItemList`, `GetMistakeItem`, `GetMistakeItemsByUpload`, `UpdateMistakeItem`, `GetAllReferencedImagePaths`, `SubmitMistakeUpload`, `CompleteUploadReview`, `GetPresignedUrl` |
| 不可用时行为 | 审计任务继续但可能产生误报；Resolve 操作返回 502；查询操作返回 500 |

### Identity Service（HTTP）

| 属性 | 值 |
|------|------|
| 协议 | HTTP/JSON（反向代理） |
| 默认地址 | `http://localhost:5002` |
| 配置键 | `IdentityService:Address`, `IdentityService:AppId`, `IdentityService:AppSecret` |
| 代理中间件 | `IdentityProxyMiddleware` |
| 路由映射 | `/api/identity/*` → `{Address}/api/*` |
| 认证方式 | 请求头 `X-Admin-AppId` + `X-Admin-AppSecret` |
| 超时 | 30 秒 |
| 不可用时行为 | 返回 502 + `{"message":"Identity service unreachable"}` |

### Teacher Portal（HTTP）

| 属性 | 值 |
|------|------|
| 协议 | HTTP/JSON（反向代理） |
| 默认地址 | `http://localhost:5004` |
| 配置键 | `TeacherPortal:Url` |
| 代理中间件 | `TeacherPortalProxyMiddleware` |
| 路由映射 | `/api/teacher-portal/admin/*` → `{Url}/api/admin/*` |
|  | `/api/teacher-portal/auth/*` → `{Url}/api/auth/*` |
|  | `/api/teacher-portal/*` → `{Url}/api/admin/*` |
| 认证方式 | 透传调用方 `Authorization: Bearer`（下游 `[Authorize(Roles="admin")]` 校验） |
| 超时 | 10 秒 |
| 未配置时行为 | 返回 503 + `{"message":"Teacher portal not configured"}` |
| 不可用时行为 | 返回 502 + `{"message":"Teacher portal service unreachable"}` |

### OSS 存储

| 属性 | 值 |
|------|------|
| 协议 | S3 兼容 API / 本地文件系统 |
| 配置键 | `Oss:Endpoint`, `Oss:AccessKey`, `Oss:SecretKey`, `Oss:BucketName`, `Oss:PublicEndpoint` |
| 环境变量切换 | `USE_LOCAL_OSS=1` → 使用 `LocalFileOssService` |
| 使用场景 | 审计 ListObjects、清理 DeleteObject（权限降级为只读+有限写） |
| Nginx 代理 | `/oss/` → SeaweedFS（strip `/oss/` 前缀），`Oss:PublicEndpoint` 控制预签名 URL 替换 |

## 服务边界

Admin Portal 的服务边界清晰定义如下：

| 职责 | 归属 | 说明 |
|------|------|------|
| 学生数据存储 | Student Service | Admin Portal 仅代理 CRUD，不存储学生数据 |
| 错题数据存储 | Mistake Service | Admin Portal 仅代理查询/更新，不存储错题数据 |
| 身份认证数据 | Identity Service | Admin Portal 仅代理请求，不存储账户数据 |
| 教师端数据 | Teacher Portal | Admin Portal 仅代理请求，不存储教师端数据 |
| OSS 审计数据 | Admin Portal（本地） | 审计记录和运行记录由 Admin Portal 自有数据库管理 |
| OSS 对象管理 | Student Service + OSS | Admin Portal 通过 Student Service 的 gRPC 方法间接操作 OSS |
| 图片预签名 URL | Student/Mistake Service | Admin Portal 通过 gRPC GetPresignedUrl 获取预签名 URL，302 重定向 |
| 图片迁移 | Student Service | Admin Portal 通过 Student gRPC MigrateImagesToMistake 代理迁移 |
