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

当前 SPA 使用服务器会话，受控实例须显式启用 `AdminOidc:Enabled`、`UseSessionForAdminApi`、`UseSessionForIdentityProxy`、`UseSessionForPortalProxies`、`UseSessionForLogout` 五项开关（后四项同属 `AdminOidc`），配置当前管理员白名单与 ADMIN 应用 audience，并完成实际下游信任及精确 Code/Logout 注册。五项默认均为 false，`start.sh` 不自动启用。密码入口在任何配置下永久 `410 legacy_login_disabled`；管理 API/图片/普通 CSRF 关闭时固定 `503 session_api_disabled`，代理关闭时分别固定 `503 session_identity_proxy_disabled` / `503 session_portal_proxy_disabled`，退出关闭时固定 `503 session_logout_disabled`，不能通过旧 Cookie 或 Authorization 恢复。当前 SPA 对关闭能力显示不可用，不回退密码或浏览器令牌。

开发 SPA 使用 `http://127.0.0.1:8090`，完整 `/api/*` 由 Vite 代理到后端硬编码 5020。注册与 BFF 配置必须使用浏览器原点的 `http://127.0.0.1:8090/api/auth/oidc/callback` 与 `/api/auth/oidc/logout-callback`，不能注册内部 5020、localhost 或通配符。集成模式改用其实际同源地址；生产仅 HTTPS 与正常 CA。复用 `IdentityService:Authority/AppId/AppSecret` 的 Confidential、PerApplication ADMIN audience、Code + openid profile + S256 客户端，不启用 refresh；Secret 只通过环境变量或 user-secrets 注入。

Cookie 只含不透明引用，ticket 8 小时绝对期限、token 到期显式重新登录，重启丢失。unsafe 请求由共享客户端单飞取普通 CSRF；退出使用独立 logout CSRF，即使 token 到期或被移出白名单仍允许本人退出。三个代理和图片均依赖当前服务器授权。完整 TLS、白名单、日志、单实例限制与回退条件见 [Deployment.md](./Deployment.md#可选-signacore-托管登录)。

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

前端开发服务器默认端口 8090；托管登录访问 `http://127.0.0.1:8090`，API 请求代理到后端 5020 端口。

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

### Prepared logout（#46）

`AdminOidc:UseSessionForLogout` 默认 false；true 必须 Enabled 与 UseSessionForAdminApi 同开，并精确注册/配置同 Admin RedirectUri origin 的 `AdminOidc:PostLogoutRedirectUri`，路径固定 `/api/auth/oidc/logout-callback`、生产 HTTPS、无 query/fragment/userinfo。数字 loopback 开发例外沿 OIDC；关闭时固定 `503 session_logout_disabled`，不落旧 Bearer action，也不撤新票据。

本人先 GET `/api/auth/logout/csrf` 取得 adminCsrf Cookie 与单值 X-CSRF-TOKEN，再 POST `/api/auth/logout`。专用入口只验证服务器 8h 有效身份票据/唯一 Authority iss/sub/stamp，允许 access token 到期或移出管理员白名单后退出本人，不授业务权限；普通 `/api/auth/csrf` 与管理 API 仍要求有效管理员 token。任何 Authorization 401，CSRF 缺/错/重复/异主体 400、零撤票/HTTP。受保护 Cookie 引用由服务器 TicketDataFormat 读取，锁内 Take 只允许一个并发 winner、Renew 不能复活；先清新 adminSession 和旧 adminAuthToken，再向固定 Authority `/oauth2/logout/requests` 发服务端 client_secret_post、服务器 id_token_hint、精确 PostLogout URI 及随机 state，不自动重试/跟随 redirect，不向浏览器发 ID token/secret。

成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。

完成回跳只支持 GET、单值 state + 独立 HttpOnly/Secure/SameSite=Lax/Path=callback 绑定 Cookie，进程内 4096 容量/5 分钟绝对时限/原子单用；正确 302 `/login?loggedOut=1`，缺/错/重复/到期/错浏览器 400 `logout_callback_invalid`，GET 不发起本地撤票。退出和回跳 no-store/no-cache/no-referrer，回滚不能恢复已撤票据/state。SignaCore 验证 ID token hint 的 iat 24h、忽略 exp，本地 8h 更短；下游已发令牌不保证立即失效。真实 PostLogout/audience 注册与 SPA 激活仍由维护者/#41/IKJ8MO 完成，fake Authority 专项不能代替生产联调。专项 `PreparedLogout_` 与 `AdminLogoutStoresTests` 使用隔离数据库/虚构凭据，无生产 OSS 删除。

### 令牌过期与显式重认证（#45）

在 `UseSessionForAdminApi=true` 模式，有效管理员票据且严格 `expires_at` 到期时，业务门禁为 `401 {"error":"reauthentication_required"}`，零下游/OSS 删除，不清服务器 ID token、不刷新或重放写请求。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false，仅合法 token 到期 false/true（保留显示名），匿名/撤票/8h失效/缺token/坏期限 false/false、显示名 null。当前非管理员403优先；任意 Authorization401。关闭会话能力时固定503，不返回旧两字段状态JSON。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果，BFF不自动挑战。fake测试验证两种回跳时序与deadline前/精确/后/8h、并发，不能证明生产SignaCore时限；真实联调仍归 #41/IKJ8MO。

### 门户服务端凭据（#44）

`AdminOidc:UseSessionForPortalProxies` 默认 false，启用需同时 `Enabled` 与 `UseSessionForAdminApi`。Teacher/Assistant 全代理前缀在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询只跟随 `UseSessionForAdminApi`：门户开关关闭也使用服务器 token，失败门禁在 Student/门户调用前拒绝；API开关关闭时在认证与出站之前固定 `503 session_api_disabled`，不采用browser Bearer。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产激活仍受 #41/IKJ8MO 门禁。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

`AdminOidc:UseSessionForIdentityProxy` 默认 false，true 必须同时启用 `Enabled` 和 `UseSessionForAdminApi`。全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。默认 legacy 和 AppSecret 剥离仍保持；门户由独立的 `UseSessionForPortalProxies` 开关控制。生产 audience/角色注册与 SPA 激活仍由 #41/IKJ8MO 验证。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。
