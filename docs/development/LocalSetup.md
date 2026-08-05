# 本地环境搭建 (LocalSetup)

## 前置依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 8.0+ | 后端编译运行 |
| Node.js | 18+ | 前端构建（可选） |
| PostgreSQL | 12+ | 审计数据库 |

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
| `ConnectionStrings:AuditDb` | `Host=localhost;Port=5432;Database=ruoyu_admin;Username=phil` | 审计数据库连接串（dev 兜底，生产由 Consul 覆盖） |
| `Database:Name` | `ruoyu_admin` | 数据库名（与 Consul `PostgreSql:*` 合成连接串时使用） |
| `StudentService:Url` | `http://localhost:5005` | Student HTTP 服务 |
| `MistakeService:Url` | `http://localhost:5007` | Mistake HTTP 服务 |
| `IdentityService:Authority` | `http://localhost:5002` | Identity HTTP 服务（JWT OIDC discovery + HTTP 代理） |
| `TeacherPortal:Url` | `http://localhost:5004` | Teacher Portal HTTP 服务 |
| `AssistantPortal:Url` | `http://localhost:5021` | Assistant Portal HTTP 服务 |

### 数据库连接策略

Admin Portal 通过 `SharedPostgreSqlConnectionStringFactory.BuildOrFallback` 组装连接串，与 mistake / student 等服务保持一致：

1. 启动时先从 `appsettings.json` 读取 Consul 连接参数，调用 `AddRuoyuConsulConfiguration` 加载共享配置
2. 从 Consul KV `config/ruoyu/shared.json` 获取 `PostgreSql:Host/Port/Username/Password`
3. 当 `PostgreSql:Host`、`PostgreSql:Username`、`Database:Name` 均非空时，与本地 `Database:Name` 合成 PostgreSQL 连接串
4. 否则回退到本地 `ConnectionStrings:AuditDb`

- 本地开发：`ConnectionStrings:AuditDb` 指向本地 PostgreSQL（`Host=localhost;Username=phil`，无密码），无需 Consul 即可运行
- 生产环境：由 Consul 的 `PostgreSql:*` 覆盖，`ConnectionStrings:AuditDb` 仅作兜底

3. 环境变量覆盖（可选）：
   ```bash
   export USE_LOCAL_OSS=1           # 使用本地文件系统替代 S3
   export OSS_LOCAL_PATH=data/oss   # 本地 OSS 存储路径
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

5. OSS 地址配置：

   | 配置键 | 说明 | 示例 |
   |--------|------|------|
   | `Oss:InternalEndpoint` | Admin 后端实际连接的 S3 地址 | `localhost:8333` |
   | `Oss:InternalSecure` | 内部连接是否使用 HTTPS | `false` |
   | `Oss:PublicBaseUrl` | 浏览器使用的预签名公共基础 URL | `https://oss.example.com/oss` |

   本地开发使用 `LocalFileOssService` 时不读取这些 S3 地址。Admin 前端 Nginx 不提供 `/oss/` 代理，公共对象由 User Web Nginx 统一代理。

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
| Mistake Service | 5007 | 必须 | 错题管理、图片预签名 URL 不可用；审计 Mistake 路径聚合跳过（可能误报） |
| Identity Service | 5002 | 可选 | 身份代理返回 502；账户批量查询返回空列表 |
| Teacher Portal | 5004 | 可选 | 教师门户代理返回 502/503 |
| Assistant Portal | 5021 | 可选 | 助教门户代理返回 502/503 |
| OSS 存储 | - | 必须（审计场景） | 审计浏览、僵尸清理不可用；图片查看通过下游 HTTP 服务获取预签名 URL |

> **Phase 4 变更**：Student Service 不可用时，影响范围扩大（新增图片预签名 URL、图片迁移、路径聚合）。Mistake Service 不可用时，审计 Mistake 路径聚合跳过而非完全失败。OSS 存储仅影响审计场景，图片查看已改为通过下游 HTTP 服务获取预签名 URL，不再由 Admin 直接读取对象内容。

> 详细配置说明见 [Deployment.md](./Deployment.md)
