# Admin Portal 部署文档

## 概述

Admin Portal 由后端 API 服务和前端 Web 应用两部分组成。本文档将详细介绍如何部署和配置这两部分。

## 目录

1. [系统架构](#系统架构)
2. [环境要求](#环境要求)
3. [后端部署](#后端部署)
4. [前端部署](#前端部署)
5. [配置说明](#配置说明)
6. [OSS 审计功能](#oss-审计功能)
7. [常见问题](#常见问题)

---

## 系统架构

Admin Portal 的系统架构如下：

```
┌─────────────────┐
│   前端 Web UI   │ (Vue 3 + Element Plus)
└────────┬────────┘
         │ HTTP/JSON
         ▼
┌─────────────────┐
│  后端 API 服务  │ (ASP.NET Core)
└────────┬────────┘
         │
    ┌────┴────┬──────────────┬──────────────┐
    │         │              │              │
    ▼         ▼              ▼              ▼
┌─────────┐ ┌──────┐ ┌─────────────┐ ┌───────────┐
│ 审计 DB │ │ OSS  │ │ Student gRPC│ │ Mistake   │
│(PostgreSQL) │ │存储  │ │   服务      │ │ gRPC 服务 │
└─────────┘ └──────┘ └─────────────┘ └───────────┘
                                     │
                              ┌──────┴──────┐
                              ▼             ▼
                       ┌────────────┐ ┌───────────┐
                       │ Identity   │ │ Teacher   │
                       │ 服务       │ │ Portal    │
                       └────────────┘ └───────────┘
```

---

## 环境要求

### 后端环境

- .NET 8.0+ SDK/Runtime
- PostgreSQL 12+ (用于审计数据库)
- 至少 512MB 可用内存
- 至少 100MB 可用磁盘空间

### 前端环境

- Node.js 18+ (仅用于构建)
- 或直接使用构建好的静态文件

### 依赖服务

- Student Service (gRPC) - 默认端口: 5005
- Mistake Service (gRPC) - 默认端口: 5006
- Identity Service - 默认端口: 5002
- Teacher Portal - 默认端口: 5004
- OSS/S3 兼容存储服务

---

## 后端部署

### 1. 构建后端

```bash
cd admin_portal/backend
dotnet publish -c Release -o ./publish
```

### 2. 配置数据库

确保 PostgreSQL 已运行，并创建数据库：

```sql
CREATE DATABASE ruoyu_study_admin;
```

### 3. 配置 appsettings.json

根据实际环境修改 `appsettings.json` 或使用环境变量覆盖配置。

### 4. 运行后端

```bash
cd ./publish
dotnet Admin.WebApi.dll
```

或使用 Docker（如果有 Dockerfile）：

```bash
# 假设已有 Dockerfile
docker build -t admin-portal-api .
docker run -p 5020:5020 --env-file .env admin-portal-api
```

---

## 前端部署

### 1. 构建前端

```bash
cd admin_portal/frontend
npm install
npm run build
```

构建产物将输出到 `dist` 目录。

### 2. 部署方式

#### 方式一：与后端集成部署（推荐）

将构建好的前端文件复制到后端的 `wwwroot` 目录：

```bash
# 在 backend 目录下创建 wwwroot 目录
mkdir -p admin_portal/backend/wwwroot

# 复制构建产物
cp -r admin_portal/frontend/dist/* admin_portal/backend/wwwroot/
```

然后重新发布或运行后端，后端会自动提供前端静态文件。

#### 方式二：独立部署

使用 Nginx、Apache 或其他 Web 服务器单独部署前端。

**Nginx 配置示例：**

```nginx
server {
    listen 80;
    server_name admin.example.com;

    root /path/to/admin_portal/frontend/dist;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api {
        proxy_pass http://localhost:5020;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 配置说明

### 后端配置 (appsettings.json)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  // 数据库连接字符串
  "ConnectionStrings": {
    "AuditDb": "Host=localhost;Port=5432;Database=ruoyu_study_admin;Username=postgres;Password=postgres"
  },
  
  // OSS 审计定时任务配置
  "OssAudit": {
    "ScheduledHour": 2,      // 每天执行审计的小时（24小时制）
    "ScheduledMinute": 0    // 每分钟执行审计的分钟
  },
  
  // 后端 API 端口配置
  "AdminApi": {
    "Port": 5020
  },
  
  // Student gRPC 服务地址
  "StudentGrpcService": {
    "Address": "http://localhost:5005"
  },
  
  // Mistake gRPC 服务地址
  "MistakeGrpcService": {
    "Address": "http://localhost:5006"
  },
  
  // Identity 服务配置
  "IdentityService": {
    "Address": "http://localhost:5002",
    "AppId": "your-app-id",
    "AppSecret": "your-app-secret"
  },
  
  // Teacher Portal 配置
  "TeacherPortal": {
    "Address": "http://localhost:5004",
    "AdminApiKey": "your-api-key"
  },
  
  // 前端 CORS 配置
  "AdminWeb": {
    "AllowedOrigins": [
      "http://localhost:5173",
      "https://admin.example.com"
    ]
  },
  
  // OSS/S3 存储配置
  "Oss": {
    "Endpoint": "localhost:8333",
    "AccessKey": "your-access-key",
    "SecretKey": "your-secret-key",
    "BucketName": "ruoyu-study"
  }
}
```

### 环境变量配置

可以使用环境变量覆盖配置，格式为：`配置路径__子键`

示例：

```bash
# Linux/Mac
export ConnectionStrings__AuditDb="Host=db.example.com;Port=5432;Database=ruoyu_study_admin;Username=user;Password=pass"
export AdminApi__Port=8080
export StudentGrpcService__Address="http://student-service:5005"

# Windows (PowerShell)
$env:ConnectionStrings__AuditDb="Host=db.example.com;Port=5432;Database=ruoyu_study_admin;Username=user;Password=pass"
```

### 前端配置

前端通常通过环境变量配置 API 地址（如果是独立部署）。如果与后端集成部署，则不需要额外配置。

**Vite 环境变量配置 (.env.production):**

```
VITE_API_BASE_URL=/api
```

---

## OSS 审计功能

### 功能概述

OSS 审计功能用于发现和清理 OSS 存储中未被引用的"僵尸"文件，帮助节省存储空间。

### 工作原理

1. **自动审计**：系统每天在指定时间（默认凌晨 2:00）自动执行审计
2. **手动触发**：也可以通过 API 手动触发审计
3. **审计流程**：
   - 扫描 OSS 中的所有对象
   - 检查对象是否被 Student Service 或 Mistake Service 引用
   - 未被引用的对象标记为"Pending"状态
4. **清理流程**：
   - 管理员可以查看审计记录
   - 确认后可以单个或批量删除"僵尸"文件
   - 删除前会再次验证文件是否仍未被引用

### 数据库表结构

```sql
CREATE TABLE oss_audit_records (
    id BIGSERIAL PRIMARY KEY,
    object_path VARCHAR(500) UNIQUE NOT NULL,
    bucket VARCHAR(100) NOT NULL,
    size BIGINT NOT NULL,
    last_modified BIGINT NOT NULL,
    status INTEGER NOT NULL DEFAULT 0,  -- 0: Pending, 1: Resolved, 2: Ignored
    created_at BIGINT NOT NULL,
    resolved_at BIGINT,
    note TEXT
);

CREATE INDEX idx_oss_audit_status ON oss_audit_records(status);
CREATE INDEX idx_oss_audit_bucket ON oss_audit_records(bucket);
CREATE INDEX idx_oss_audit_created_at ON oss_audit_records(created_at);
```

### 审计状态说明

- **Pending (0)**: 待处理，发现的未引用文件
- **Resolved (1)**: 已解决，文件已被删除
- **Ignored (2)**: 已忽略，管理员确认保留的文件

### 配置审计时间

在 `appsettings.json` 中修改：

```json
{
  "OssAudit": {
    "ScheduledHour": 2,
    "ScheduledMinute": 0
  }
}
```

---

## 常见问题

### 1. 后端无法连接到 gRPC 服务

**问题**: 日志中显示 gRPC 连接失败

**解决方案**:
- 检查 Student Service 和 Mistake Service 是否正常运行
- 确认 `appsettings.json` 中的地址配置正确
- 检查网络连通性和防火墙设置

### 2. CORS 错误

**问题**: 前端访问 API 时出现 CORS 错误

**解决方案**:
- 在 `appsettings.json` 的 `AdminWeb:AllowedOrigins` 中添加前端域名
- 如果是开发环境，可以留空数组以允许所有来源

### 3. 数据库连接失败

**问题**: 后端启动时无法连接到 PostgreSQL

**解决方案**:
- 检查 PostgreSQL 服务是否运行
- 确认连接字符串中的用户名、密码和数据库名正确
- 检查网络连通性

### 4. OSS 审计不执行

**问题**: 自动审计任务没有按时执行

**解决方案**:
- 检查 `OssAudit` 配置是否正确
- 查看日志确认 Worker 是否正常启动
- 尝试通过 API 手动触发审计

### 5. 前端页面刷新后 404

**问题**: 单页应用在刷新页面时出现 404

**解决方案**:
- 如果是独立部署，确保 Web 服务器配置了路由回退到 index.html
- 如果是与后端集成部署，确保后端的 SPA 中间件正常工作

### 6. 身份验证失败

**问题**: 无法访问需要身份验证的功能

**解决方案**:
- 确认 Identity Service 正常运行
- 检查 `IdentityService` 配置中的 `AppId` 和 `AppSecret` 是否正确
- 查看 IdentityProxyMiddleware 的日志

---

## 健康检查

后端没有专门的健康检查端点，但可以访问根路径来确认服务是否运行：

```bash
curl http://localhost:5020/
```

如果返回 "Student Admin WebAPI is running." 或看到前端页面，说明服务正常。

---

## 日志

后端使用 ASP.NET Core 内置日志系统，日志级别可以在 `appsettings.json` 中配置。

建议在生产环境中：
- 将 `Default` 设置为 `Information` 或 `Warning`
- 将 `Microsoft.AspNetCore` 设置为 `Warning`
- 配置日志持久化（如使用 Serilog、ELK 等）
