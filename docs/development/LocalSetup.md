# 本地环境搭建 (LocalSetup)

## 前置依赖

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 10.0+ | 后端编译运行 |
| Node.js | 18+ | 前端构建（可选） |
| PostgreSQL | 12+ | 审计数据库 |

## 后端配置

1. 复制配置文件：
   ```bash
   cd backend
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
| `IdentityService:Authority` | （无；Development 默认 `http://localhost:5002`，见 `appsettings.Development.json`） | Identity HTTP 服务（JWT OIDC discovery + HTTP 代理）。`appsettings.json` 不含 `IdentityService` 节，本地必须经环境变量 / user-secrets 注入 `AppId`、`AppSecret` 等 |
| `TeacherPortal:Url` | `http://localhost:5004` | Teacher Portal HTTP 服务 |
| `AssistantPortal:Url` | `http://localhost:5021` | Assistant Portal HTTP 服务 |

### 可选托管登录开发配置

默认 `AdminOidc:Enabled=false`、`AdminOidc:UseSessionForAdminApi=false`，旧密码/JWT 前端与 API 保持原样。只在验证 #38 的基础握手时显式设 `AdminOidc__Enabled=true`、`AdminOidc__RedirectUri=http://127.0.0.1:5020/api/auth/oidc/callback`，并在本地 SignaCore 注册同一精确 URI 的 Confidential Code 客户端与 openid profile/S256；复用其 AppId/AppSecret，通过环境变量或 user-secrets 保存。Authority/Issuer 使用本地数字 loopback；`localhost` 不符合此 OIDC 切片的开发注册边界。生产要求 HTTPS，固定 URI 不取入站 Host/转发头。

新入口是 `/api/auth/oidc/start`、框架 callback 和 `/api/auth/session`；票据/令牌只在进程内，Cookie 只含引用，8 小时绝对到期、重启失效。默认不以它授权管理 API。要验证 #42，显式再设 `AdminOidc__UseSessionForAdminApi=true` 并配置当前 `AdminPortal:AdminUserIds`：管理 API 拒入站 Authorization、使用服务器 token 期限门禁与管理员白名单（401/403）；写请求先 GET `/api/auth/csrf`，带独立 Cookie 与单值 `X-CSRF-TOKEN`（失败 400）。仅用 mock OSS/隔离数据库回归，不做生产删除；新 Cookie 不放行三个未迁移代理。此时密码 login 为 410、零 Identity 转发；旧 logout 只清旧 JWT Cookie，不撤销新票据。不切 SPA、不做 refresh/上游 logout。完整配置、安全与上线门禁见 [Deployment.md](./Deployment.md#可选-signacore-托管登录)。

自动化完整握手使用 `backend/Tests/Integration/OidcTestAuthority.cs` 的测试 RSA、Discovery/JWKS/token 替身与 Testcontainers PostgreSQL，`dotnet test Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"`；不连接生产 Identity。

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
cd frontend
npm install
```

前端开发服务器默认 `http://localhost:5173`，API 请求代理到后端 5020 端口。

## 下游服务依赖

Admin Portal 依赖以下下游服务运行：

| 服务 | 端口 | 必要性 | 不可用时影响 |
|------|------|--------|-------------|
| Student Service | 5005 | 必须 | 学生管理、上传记录、图片预签名 URL、图片迁移均不可用；审计路径聚合不可用 |
| Mistake Service | 5007 | 必须 | 错题管理、图片预签名 URL 不可用；审计引用聚合按不变量整轮中止（连接拒绝与非 JSON 失败均向上传播，见 [#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28)） |
| Identity Service | 5002 | 可选 | 身份代理返回 502；账户批量查询返回空列表 |
| Teacher Portal | 5004 | 可选 | 教师门户代理返回 502/503 |
| Assistant Portal | 5021 | 可选 | 助教门户代理返回 502/503 |
| OSS 存储 | - | 必须（审计场景） | 审计浏览、僵尸清理不可用；图片查看通过下游 HTTP 服务获取预签名 URL |

> **Phase 4 变更**：Student Service 不可用时，影响范围扩大（新增图片预签名 URL、图片迁移、路径聚合）。Mistake Service 不可用时，审计按不变量整轮中止而非继续（历史「跳过聚合」表述与当前实现的不变量冲突；连接拒绝与非 JSON 失败均向上传播，见 [#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28)）。OSS 存储仅影响审计场景，图片查看已改为通过下游 HTTP 服务获取预签名 URL，不再由 Admin 直接读取对象内容。
>
> 需要一套可执行的本地全栈（含 Identity 与三个引用来源替身）时，见 [LocalRegressionEnv.md](./LocalRegressionEnv.md)。

> 详细配置说明见 [Deployment.md](./Deployment.md)

### 门户服务端凭据（#44）

`AdminOidc:UseSessionForPortalProxies` 默认 false，启用需同时 `Enabled` 与 `UseSessionForAdminApi`。Teacher/Assistant 全代理前缀在认证/授权前共用管理员/CSRF 门禁，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询只跟随 `UseSessionForAdminApi`：门户开关关闭也使用服务器 token，失败门禁在 Student/门户调用前拒绝；默认API关闭保持legacy Bearer。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产激活仍受 #41/IKJ8MO 门禁。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

`AdminOidc:UseSessionForIdentityProxy` 默认 false，true 必须同时启用 `Enabled` 和 `UseSessionForAdminApi`。全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。默认 legacy 和 AppSecret 剥离仍保持；门户由独立的 `UseSessionForPortalProxies` 开关控制。生产 audience/角色注册与 SPA 激活仍由 #41/IKJ8MO 验证。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。
