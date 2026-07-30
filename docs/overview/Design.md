# 设计文档

> **Phase 4 计划变更（尚未实施）**：本文档已更新为 Phase 4 目标状态。主要变更：
> - `StudentManagementGrpcServiceClient` 移除，所有 Student gRPC 调用统一通过 `StudentLearningGrpcServiceClient`
> - `IOssService` 权限降级为只读 + 有限写（僵尸清理 Delete + 迁移辅助 CopyObject）
> - Admin Portal 自行实现路径聚合逻辑（从通用 gRPC 分页获取数据后聚合）
> - OSS 审计使用直接 `IOssService.ListObjectsAsync` + 通用 gRPC 路径聚合

## 服务级架构

### 分层架构

```
┌─────────────────────────────────────────────────────────────┐
│                        前端 (Vue 3)                         │
│                  Vue 3 + Element Plus + Vite                │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTP/JSON
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core 8.0                          │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   中间件层 (Middleware)                │  │
│  │  ┌─────────────────┐  ┌────────────────────────────┐ │  │
│  │  │  CORS           │  │  IdentityProxyMiddleware    │ │  │
│  │  └─────────────────┘  └────────────────────────────┘ │  │
│  │  ┌─────────────────┐  ┌────────────────────────────┐ │  │
│  │  │  Swagger        │  │  TeacherPortalProxyMiddleware│ │  │
│  │  └─────────────────┘  └────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   控制器层 (Controllers)               │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ StudentsController│  │ MistakeController         │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ OssAuditController│ │ OssUploadRecordController  │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ ImageController   │  │ EnumOptionsController      │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐                                 │  │
│  │  │IdentityAccounts  │                                 │  │
│  │  │  Controller      │                                 │  │
│  │  └──────────────────┘                                 │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   服务层 (Services)                    │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ OssAuditWorker   │  │ IOssService               │ │  │
│  │  │ (BackgroundService)│ │ (S3OssService /          │ │  │
│  │  │                  │  │  LocalFileOssService)     │ │  │
│  │  │ 路径聚合：       │  │  [权限降级：只读 +       │ │  │
│  │  │  Student 通用gRPC│  │   有限写(Delete/Copy)]   │ │  │
│  │  │  Mistake 通用gRPC│  │  ListObjects(审计浏览)   │ │  │
│  │  └──────────────────┘  │  Delete(僵尸清理)        │ │  │
│  │                        │  CopyObject(迁移辅助)    │ │  │
│  │                        └───────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   数据层 (Data)                        │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ AuditDbContext   │  │ DatabaseInitializer       │ │  │
│  │  │ (EF Core)        │  │ (手动 DDL)                │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## 技术栈

| 层次 | 技术 | 版本 | 说明 |
|------|------|------|------|
| 运行时 | .NET | 8.0 | ASP.NET Core Web API |
| ORM | Entity Framework Core | 8.0 | Npgsql (PostgreSQL) 提供程序 |
| gRPC 客户端 | Grpc.Net.ClientFactory | 2.62.0 | 连接 Student/Mistake 服务 |
| 数据库 | PostgreSQL | 12+ | 通过 Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11 |
| API 文档 | Swashbuckle.AspNetCore | 6.5.0 | Swagger UI (仅 Development 环境) |
| 对象存储 | S3 兼容 API / 本地文件 | - | 通过 IOssService 抽象（权限降级：只读 + 有限写，用于审计浏览 ListObjects、僵尸清理 Delete、迁移辅助 CopyObject） |
| 前端 | Vue 3 + Element Plus | - | SPA，可集成部署到 wwwroot |
| 构建工具 | Vite | - | 前端构建 |
| 测试 | xUnit + Moq + FluentAssertions | - | 单元测试 + coverlet 覆盖率 |

## 依赖关系图

```
                    Admin.WebApi (ASP.NET Core 8.0)
                         │
            ┌────────────┼────────────────┐
            │            │                │
            ▼            ▼                ▼
   Ruoyu.Study.    Ruoyu.Study.    Ruoyu.Study.
   Student.Contract Mistake.Contract  Common
   (gRPC Protos)   (gRPC Protos)    (Constants)
            │            │                │
            │            │                ├─ IOssService
            │            │                │  ├─ S3OssService
            │            │                │  └─ LocalFileOssService
            │            │                │  [权限降级：只读 + 有限写]
            │            │                │  ListObjects / Delete / CopyObject
            │            │                │
            │            │                ├─ DatabaseInitializer
            │            │                │
            │            │                └─ Constants
            │            │                   ├─ GradeConstants
            │            │                   ├─ SubjectConstants
            │            │                   ├─ UploadStatusConstants
            │            │                   ├─ ClassificationConstants
            │            │                   └─ ReviewStatusConstants
            │            │
            ▼            ▼
     Student gRPC    Mistake gRPC
     :5005           :5006
  (StudentLearning    (MistakeGrpcService
   GrpcService         仅通用接口)
   仅通用接口)
```

> **Phase 4 变更说明**：`StudentManagementGrpcServiceClient` 已移除，所有 Student gRPC 调用统一通过 `StudentLearningGrpcServiceClient`。Admin 专用接口（ListOssObjects、GetRegisteredOssPaths、DeleteOssObject 等）不再由 Student 服务提供，改为 Admin Portal 自行实现（直接 IOssService + 通用 gRPC）。

## 关键设计决策

### 1. 数据库初始化：DatabaseInitializer 而非 EF Migrations

**决策**：使用 `DatabaseInitializer` + `EnsureCreated` 而非 EF Core Migrations。

**原因**：
- `OssAuditRuns` 表需要 `GENERATED ALWAYS AS IDENTITY`，EF Core 的 `EnsureCreated` 不支持此特性
- 项目表结构简单（仅 2 张表），不需要复杂的迁移管理
- `CREATE TABLE IF NOT EXISTS` 语义保证幂等启动

### 2. 审计并发控制：数据库记录互斥

**决策**：通过 `OssAuditRuns` 表中 `Status=0` 的记录实现互斥。

**原因**：
- 无需引入分布式锁
- 数据库级别的原子性保证
- 双重检查（SaveChanges + 查询）确保可靠

### 3. 代理中间件而非 BFF 框架

**决策**：使用自定义中间件（`IdentityProxyMiddleware`、`TeacherPortalProxyMiddleware`）实现 HTTP 反向代理。

**原因**：
- 代理逻辑简单（路径映射 + 认证头注入）
- 无需引入 YARP 等重量级框架
- 精确控制请求/响应处理

### 4. IOssService 抽象（权限降级）

**决策**：Admin Portal 保留 `IOssService`，但权限降级为只读 + 有限写。

**原因**：
- 图片下载改为通过 Student/Mistake gRPC `GetPresignedUrl` 获取预签名 URL + 302 重定向
- 图片迁移改为通过 Student gRPC `MigrateImagesToMistake` 代理
- `IOssService` 用于审计场景的 `ListObjectsAsync`、清理场景的 `DeleteAsync`、迁移辅助的 `CopyObjectAsync`
- 凭证权限降级为只读 + 有限写（允许 List + Delete + CopyObject）

**变更影响**：
- `ImageController`：不再使用 `IOssService`，改用 gRPC `GetPresignedUrl`
- `MistakeController.MigrateImages`：不再使用 `IOssService`，改用 Student gRPC `MigrateImagesToMistake`
- `OssAuditWorker`：`ListOssObjectsPaged` 改为直接调用 `IOssService.ListObjectsAsync`；路径聚合改用通用 gRPC 分页获取后自行聚合
- `OssAuditController.ResolveRecord`：保留 `IOssService` 用于 DeleteObject

**oss-path-autonomy 变更**：
- `IOssService.DeleteAsync` 自动清理关联缩略图：`S3OssService.DeleteAsync` 内部调用 `ThumbnailHelper.IsThumbnailPath` 判断是否为缩略图，若为原图则调用 `ThumbnailHelper.GetAllThumbnailPaths` 获取所有缩略图路径并逐一删除后再删除原图；若为缩略图则只删除该文件本身，不触发递归清理
- Admin Portal 的 `S3OssService` 不配置路径前缀校验（`allowedPrefixes` 为 null），因为审计需要访问所有路径前缀（uploads/、mistakes/、questions/）
- 缩略图路径规则变更：从 `uploads/thumbnails/` 改为与原图同目录，路径格式 `{dirname}/{stem}_{size}.jpg`
- `OssUploadRecordController`：改用通用 gRPC + 直接 OSS 操作

### 4a. Admin Portal 自实现路径聚合（Phase 4 新增）

**决策**：Admin Portal 自行实现 OSS 审计中的路径聚合逻辑，不再依赖下游服务的专用接口。

**原因**：
- `StudentManagementGrpcService.GetRegisteredOssPaths` 被移除，Admin Portal 通过 Student 通用 gRPC（如 `GetAllUploadRecords`）分页获取上传记录后，自行提取和聚合图片路径
- `MistakeGrpcService.GetAllReferencedImagePaths` 被移除，Admin Portal 通过 Mistake 通用 gRPC（如 `GetMistakeItemList`）分页遍历错题条目后，自行提取和聚合图片路径
- 路径聚合是 Admin 审计特有需求，不应要求其他服务提供专用接口

**实现方式**：
- Student 路径聚合：调用 `StudentLearningGrpcService.GetAllUploadRecords` 分页获取所有上传记录，提取 `ImagePaths` 字段去重聚合
- Mistake 路径聚合：调用 `MistakeGrpcService.GetMistakeItemList` 分页遍历所有错题条目，提取 `ImagePath` 字段去重聚合

### 5. 前后端集成部署

**决策**：支持前端构建产物部署到后端 `wwwroot` 目录，由 ASP.NET Core 提供静态文件服务。

**原因**：
- 简化部署（单一服务）
- SPA 路由回退处理
- 支持 `__APP_TITLE__` 运行时替换

### 5a. Nginx 容器前后端分离部署（oss-nginx-proxy 变更）

**决策**：Admin Portal 新增 Nginx 容器，实现前后端分离部署 + OSS 反向代理。

**原因**：
- 前端静态文件由 Nginx 直接提供，性能优于 ASP.NET Core 静态文件中间件
- Nginx `/oss/` location 代理到 SeaweedFS，前端无需直连内部 S3 端点
- `proxy_set_header Host ruoyu-seaweedfs:8333` 确保 S3 签名验证通过
- 缓存头 `Cache-Control: public, max-age=3600` 减少 SeaweedFS 请求压力

**Nginx 配置要点**：

```nginx
location /oss/ {
    proxy_pass http://ruoyu-seaweedfs:8333/;
    proxy_set_header Host ruoyu-seaweedfs:8333;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    add_header Cache-Control "public, max-age=3600";
}
```

- `proxy_pass` 末尾 `/` 实现 strip `/oss/` 前缀：`/oss/ruoyu-study/mistakes/xxx/image.jpg` → `/ruoyu-study/mistakes/xxx/image.jpg`
- `proxy_set_header Host` 必须设为 SeaweedFS 内部地址，否则 S3 签名验证失败
- `Cache-Control` 设置 1 小时公共缓存，适用于预签名 URL 的对象访问

**PublicEndpoint 配置**：

`OssOptions.PublicEndpoint` 控制预签名 URL 的 scheme+host 替换。非空时，`GetPresignedUrlAsync` 将内部 SeaweedFS 地址替换为 `{PublicEndpoint}/oss`，使前端通过 Nginx 代理访问 OSS 对象。

| 配置键 | 说明 | 示例 |
|--------|------|------|
| `Oss:PublicEndpoint` | 公共访问端点（可选） | `https://admin.example.com` |

> 仍支持原有的前后端集成部署模式（wwwroot），Nginx 容器为新增部署选项。

### 6. gRPC 合约引用方式

**决策**：直接引用 proto 文件并设置 `GrpcServices="Client"`，通过 `ProtoRoot` 解决 import 路径问题。

**实现方式**：
```xml
<Protobuf Include="..\..\src\services\ruoyu.student\src\Contract\Protos\student.proto"
          GrpcServices="Client"
          ProtoRoot="..\..\src\services\ruoyu.student\src\Contract" />
<Protobuf Include="..\..\src/services/ruoyu.mistake\src\Contract\Protos\mistake.proto"
          GrpcServices="Client"
          ProtoRoot="..\..\src/services/ruoyu.mistake\src\Contract" />
<Protobuf Include="..\..\src/services/ruoyu.mistake\src\Contract\Protos\mistake.common.proto"
          GrpcServices="None"
          ProtoRoot="..\..\src/services/ruoyu.mistake\src\Contract" />
```

**原因**：
- Admin Portal 只需要 gRPC Client 代码，不需要 Server 端基类
- `GrpcServices="Client"` 只生成 `xxxClient` 类，避免生成无用的 Server 端代码
- `ProtoRoot` 指定 proto 文件的根目录，使 `mistake.proto` 中的 `import "Protos/mistake.common.proto"` 能正确解析
- 不再依赖 Contract 项目的 `GrpcServices="Both"` 设置，避免服务器构建时因生成不需要的 Server 代码而失败

**注意事项**：
- `ProtoRoot` 必须指向 Contract 项目的根目录（包含 `Protos/` 子目录的父目录），而不是 `Protos/` 目录本身
- 如果 Contract 项目的 proto 文件新增了 import 依赖，Admin.WebApi.csproj 也需要同步添加对应的 `<Protobuf>` 引用（`GrpcServices="None"`）

## 配置结构

所有配置通过 `appsettings.json` + 环境变量管理，详见 [Deployment.md](../development/Deployment.md)。

| 配置节 | 说明 |
|--------|------|
| `AdminApi:Port` | 服务监听端口 |
| `ConnectionStrings:AuditDb` | 审计数据库连接串（dev 兜底，生产由 Consul 覆盖） |
| `Database:Name` | 数据库名（与 Consul `PostgreSql:*` 合成连接串，`ruoyu_study_admin`） |
| `StudentGrpcService:Address` | Student gRPC 服务地址 |
| `MistakeGrpcService:Address` | Mistake gRPC 服务地址 |
| `IdentityService:*` | Identity 服务地址和认证信息 |
| `TeacherPortal:*` | Teacher Portal 地址和 API Key |
| `OssAudit:*` | 审计定时任务配置 |
| `Oss:*` | OSS 存储配置（含 `PublicEndpoint`） |
| `AdminWeb:AllowedOrigins` | CORS 允许的来源 |
