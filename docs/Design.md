# 设计文档

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
│  │  └──────────────────┘  │  LocalFileOssService)     │ │  │
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
| ORM | Entity Framework Core | 8.0 | Npgsql (PostgreSQL) / SQLite 提供程序 |
| gRPC 客户端 | Grpc.Net.ClientFactory | 2.62.0 | 连接 Student/Mistake 服务 |
| 数据库 (生产) | PostgreSQL | 12+ | 通过 Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11 |
| 数据库 (测试) | SQLite | - | 通过 Microsoft.EntityFrameworkCore.Sqlite 8.0.11 |
| API 文档 | Swashbuckle.AspNetCore | 6.5.0 | Swagger UI (仅 Development 环境) |
| 对象存储 | S3 兼容 API / 本地文件 | - | 通过 IOssService 抽象 |
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
   (gRPC Protos)   (gRPC Protos)    (OSS, DB, Constants)
            │            │                │
            │            │                ├─ IOssService
            │            │                │  ├─ S3OssService
            │            │                │  └─ LocalFileOssService
            │            │                │
            │            │                ├─ DatabaseInitializer
            │            │                │
            │            │                └─ Constants
            │            │                   ├─ GradeConstants
            │            │                   ├─ SubjectConstants
            │            │                   ├─ UploadStatusConstants
            │            │                   ├─ ClassificationConstants
            │            │                   ├─ ReviewStatusConstants
            │            │                   └─ ErrorTypeConstants
            │            │
            ▼            ▼
     Student gRPC    Mistake gRPC
     :5005           :5006
```

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

### 4. IOssService 抽象

**决策**：通过 `IOssService` 接口抽象 OSS 操作，支持 S3 和本地文件两种实现。

**原因**：
- 测试环境使用本地文件系统，无需 S3 服务
- 通过环境变量 `USE_LOCAL_OSS=1` 切换实现
- 统一的下载、上传、复制、删除接口

### 5. 前后端集成部署

**决策**：支持前端构建产物部署到后端 `wwwroot` 目录，由 ASP.NET Core 提供静态文件服务。

**原因**：
- 简化部署（单一服务）
- SPA 路由回退处理
- 支持 `__APP_TITLE__` 运行时替换

## 配置结构

所有配置通过 `appsettings.json` + 环境变量管理，详见 [deployment.md](deployment.md)。

| 配置节 | 说明 |
|--------|------|
| `AdminApi:Port` | 服务监听端口 |
| `ConnectionStrings:AuditDb` | 审计数据库连接字符串 |
| `StudentGrpcService:Address` | Student gRPC 服务地址 |
| `MistakeGrpcService:Address` | Mistake gRPC 服务地址 |
| `IdentityService:*` | Identity 服务地址和认证信息 |
| `TeacherPortal:*` | Teacher Portal 地址和 API Key |
| `OssAudit:*` | 审计定时任务配置 |
| `Oss:*` | OSS 存储配置 |
| `AdminWeb:AllowedOrigins` | CORS 允许的来源 |
