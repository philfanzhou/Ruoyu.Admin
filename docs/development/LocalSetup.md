# 本地环境搭建 (LocalSetup)

## 前置依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 8.0+ | 后端编译运行 |
| Node.js | 18+ | 前端构建（可选） |
| PostgreSQL | 12+ | 审计数据库（生产模式） |

> 测试环境可使用 SQLite，无需 PostgreSQL。

## 后端配置

1. 复制配置文件：
   ```bash
   cd src/admin_portal/backend
   # appsettings.json 已包含开发默认值，可直接使用
   ```

2. 关键配置项（`appsettings.json`）：

| 配置键 | 默认值 | 说明 |
|--------|--------|------|
| `AdminApi:Port` | 5020 | API 监听端口 |
| `ConnectionStrings:AuditDb` | PostgreSQL 连接串 | 审计数据库；改为 `Data Source=admin.db` 使用 SQLite |
| `StudentGrpcService:Address` | `http://localhost:5005` | Student gRPC 服务 |
| `MistakeGrpcService:Address` | `http://localhost:5006` | Mistake gRPC 服务 |
| `IdentityService:Address` | `http://localhost:5002` | Identity HTTP 服务 |
| `TeacherPortal:Address` | `http://localhost:5004` | Teacher Portal HTTP 服务 |

3. 环境变量覆盖（可选）：
   ```bash
   export USE_LOCAL_OSS=1           # 使用本地文件系统替代 S3
   export OSS_LOCAL_PATH=data/oss   # 本地 OSS 存储路径
   export ConnectionStrings__AuditDb="Data Source=admin.db"  # 使用 SQLite
   ```

4. IOssService 凭证权限说明（Phase 4 变更）：

   Admin Portal 保留 `IOssService` 用于审计场景，但凭证权限降级为只读 + 有限写：

   | 操作 | 权限 | 用途 |
   |------|------|------|
   | `ListObjects` | 只读 | 审计浏览 |
   | `GetPresignedUrl` | 只读 | 生成预签名 URL |
   | `ObjectExists` | 只读 | 审计校验 |
   | `Download` | 只读 | 图片查看（改造后走预签名 URL，可后续移除） |
   | `Delete` | 写 | 僵尸文件清理 |
   | `CopyObject` | 写 | 迁移辅助 |

   > 本地开发使用 `LocalFileOssService`（`USE_LOCAL_OSS=1`）时无权限限制；生产环境需配置对应权限的 OSS 凭证。

5. PublicEndpoint 配置（oss-nginx-proxy 变更）：

   `Oss:PublicEndpoint` 为可选配置，用于将预签名 URL 的内部 SeaweedFS 地址替换为外部可访问的 Nginx 代理地址。本地开发通常不需要配置此项。

   | 配置键 | 说明 | 示例 |
   |--------|------|------|
   | `Oss:PublicEndpoint` | 公共访问端点（可选） | `https://admin.example.com` |

   > 非空时，预签名 URL 的 scheme+host 替换为 `{PublicEndpoint}/oss`，前端通过 Nginx `/oss/` 代理访问 OSS。

## 前端配置

```bash
cd src/admin_portal/frontend
npm install
```

前端开发服务器默认 `http://localhost:5173`，API 请求代理到后端 5020 端口。

## 下游服务依赖

Admin Portal 依赖以下下游服务运行：

| 服务 | 端口 | 必要性 | 不可用时影响 |
|------|------|--------|-------------|
| Student Service | 5005 | 必须 | 学生管理、上传记录、图片预签名 URL、图片迁移均不可用；审计路径聚合不可用 |
| Mistake Service | 5006 | 必须 | 错题管理、图片预签名 URL 不可用；审计 Mistake 路径聚合跳过（可能误报） |
| Identity Service | 5002 | 可选 | 身份代理返回 502；账户批量查询返回空列表 |
| Teacher Portal | 5004 | 可选 | 教师门户代理返回 502/503 |
| OSS 存储 | - | 必须（审计场景） | 审计浏览、僵尸清理不可用；图片查看走 gRPC 预签名 URL 不受影响 |

> **Phase 4 变更**：Student Service 不可用时，影响范围扩大（新增图片预签名 URL、图片迁移、路径聚合）。Mistake Service 不可用时，审计 Mistake 路径聚合跳过而非完全失败。OSS 存储仅影响审计场景，图片查看已改为 gRPC 预签名 URL 不再直接依赖 OSS。

> 详细配置说明见 [deployment.md](../deployment.md)
