# 部署与运维

## 构建与启动

- 集成镜像：`backend/Admin.WebApi/Dockerfile`，先构建 Vue 前端，再把产物复制到 API 的 `wwwroot`。构建入口 `./scripts/build.sh`，产物 `ruoyu.admin:${IMAGE_TAG}`（`IMAGE_TAG` 默认 `20260502`）。
- 启动脚本：仓库根 `start.sh`。
- 容器：`ruoyu-admin`，容器内监听 5020，默认映射到宿主机 5020（宿主端口与容器端口一致，与平台其他 API 的映射惯例相同；monorepo 时代的 10901 已废弃）。
- 容器使用 Docker 默认 bridge，不依赖 `ruoyu-net` 或容器名解析；跨主机依赖通过 Consul KV 中的局域网地址访问。
- Dockerfile 的构建上下文是**仓库根**，受仓库根 `.dockerignore` 约束。
- 发布镜像：推送 `X.Y.Z-rc.N`（测试版）或 `X.Y.Z`（正式版）tag 后，CI（`.github/workflows/ci.yml`）会把镜像发布到 GHCR：`ghcr.io/philfanzhou/ruoyu.admin`。正式版同时移动 `X.Y` 与 `latest`；rc 只保留不可变版本标签。部署主机可以不从源码构建，直接 `docker pull ghcr.io/philfanzhou/ruoyu.admin:<version>`。
- 历史注：monorepo 继承的独立前端镜像（`frontend/Dockerfile` + `scripts/build-web.sh`，产物 `ruoyu.admin.web`）已于 2026-09-25 移除——前后端分离模式从未被任何部署使用，产品形态就是 API+SPA 单容器。

### 服务标识

迁出 monorepo 时服务标识从 `Ruoyu.Study.AdminPortal` 改为 `Ruoyu.Admin`：

| 位置 | 键 | 影响 |
|------|-----|------|
| `Program.cs` / `AdminLoggingExtensions` | 固定 `service=Ruoyu.Admin` | Loki 日志标签。**已有的 Grafana 查询、面板和告警若按 `service="Ruoyu.Study.AdminPortal"` 过滤，必须同步改名**，否则新日志查不到 |
| `appsettings.json` | `Consul:ServiceName` | 仅作标识。Consul KV 读取只使用 `Consul:KvPrefix`（`config/ruoyu`），`RuoyuConsulKvLoader.BuildPrefixes` 不消费 `ServiceName`，因此改名不影响配置加载 |

`Consul:ServiceName` 可用环境变量 `CONSUL_SERVICE_NAME` 覆盖（见 `RuoyuConsulOptions.Bind`）。Loki 标签由代码固定，旧 `Serilog` 配置覆盖已移除，不能再用它改流标签。

### 日志管线与字段

`ServiceMantle.Logging 0.3.0`（正式版；#66 将所有直接 ServiceMantle 包引用统一到同版）提供 Console + 可选 Loki，共享 Information 最低级别和两个 Warning override：`Microsoft.AspNetCore`、`Microsoft.EntityFrameworkCore.Database.Command`。`Logging:LogLevel` 是框架 ILogger 的前置过滤；它可以进一步抑制事件，不能突破上述管线下限。

`Loki:Uri` 仍由 Consul / appsettings 提供，仓库回退 `http://ruoyu-loki:3100`；空值关闭远端 sink，错误绝对 URI 启动失败且只返回固定错误代码。内网 HTTP 通过 `AllowInsecureHttp=true` 显式信任，完整日志以明文传输，仅用于可信网络；跨信任边界必须用 HTTPS。无授权头配置。Loki 不可达时异步有界重试，业务请求继续可用；有界队列、关机冲刷不保证零丢失。流标签固定 `service=Ruoyu.Admin`，sink 另加低基数 `level`，`{service="Ruoyu.Admin"}` 查询不变。

| 请求日志字段 | 原管线 | 当前管线 |
|---|---|---|
| ServiceName | `Ruoyu.Admin` | `ruoyu-admin` |
| ServiceVersion | 固定 `1.0.0` | 入口程序集 informational version |
| InstanceId | MachineName | 每宿主 `ruoyu-admin-<guid>` |
| CorrelationId | 无 | 与响应 `x-correlation-id` 相同 |
| MachineName / ThreadId | 自动 enrich | 不再自动附加 |

请求 scope 的身份字段进入真实 Console/Loki 管线；无请求 scope 的启动日志不承诺自动身份字段。结构化字段强制脱敏，启动诊断仍通过 `StartupDiagnosticsFormatter`；消息模板字面量/自由文本不保证万能 secret 检测，调用方不得直接插入凭据或个人数据。

## 配置加载

Admin API 启动时通过 Consul `config/ruoyu/*` 加载 PostgreSQL、OSS 和下游服务共享配置。`start.sh` 注入 Consul 地址、数据库名 `ruoyu_admin` 和 Identity 应用凭据。仓库中的 `127.0.0.1` 只是占位默认值，**不是任何真实部署的地址**；部署时通过 `CONSUL_HTTP_ADDR` 指向实际 Consul 地址。

### 可选 SignaCore 托管登录

`AdminOidc:Enabled` 与 `AdminOidc:UseSessionForAdminApi` 均默认 false，前端、管理 API、代理继续旧 JWT。显式开启两项后，`/api/admin/*` 使用服务器会话、当前管理员白名单与写请求 CSRF；三个未迁移代理仍为旧 Bearer，新 Cookie 单独访问返回 401。默认关闭能力保留，生产激活仍依赖 IKJ8MO 与后续 #41/#43–#46。

启用时复用 `IdentityService:Authority/AppId/AppSecret`，新增 `AdminOidc:RedirectUri`，精确注册 `https://admin.example.com/api/auth/oidc/callback`（占位示例）。URI 必须与配置 byte-for-byte 相同，路径固定、无 query/fragment/userinfo/wildcard；生产 HTTPS。只有 Development/Testing 允许 `http://127.0.0.1:<port>/api/auth/oidc/callback` 或 `http://[::1]:<port>/api/auth/oidc/callback`；不允许 localhost。Authority 去结尾 `/` 后与 Discovery issuer 严格相同；OIDC 不使用 JWT AdditionalValidIssuers 放宽信任。错误启用配置启动失败，消息只列配置键。

按 [SignaCore HostedLogin](https://github.com/philfanzhou/SignaCore/blob/52c68c812335dd6a967cb90042ac91c21543d387/docs/integrations/HostedLogin.md) 注册 Confidential、PerApplication audience、精确 Redirect URI 和 Code + openid profile + S256，不启用 refresh。Admin 从 Discovery 取授权/token/JWKS 端点，不发送 PAR、prompt 或其他不支持字段；兑换为 client_secret_post，固定外部 RedirectUri 与原 verifier，不信任入站 Host/X-Forwarded-Host 拼地址。TLS 可在可信代理终止，回跳仍固定 HTTPS，Cookie Secure；本服务不增加任意转发头信任。

反向代理必须避免 callback query（尤其 code）进入 access log/analytics，并保证原始单值 query 透传。Admin 协议 handler 禁用可能包含 token/URL/Cookie 的框架详细日志；启用 OIDC 时另外抑制会记录原始 query 的 `Microsoft.AspNetCore.Hosting.Diagnostics` 请求日志（包含其余路由的此类日志），OIDC 入口不导出 ASP.NET Core trace；应用调用方仍不得将敏感值插入自由文本。AppSecret 只通过环境变量/user-secrets/Consul 安全配置，不提交配置文件。

单实例内存 state 最长 5 分钟、43 字符随机引用、单次原子消费；票据 8 小时绝对到期、不滑动，所有 token 留服务器。两类存储各最多 4096 项、每分钟回收；满载固定失败，重启丢失，不能多副本共用会话。数据保护 keys 即使还在也不会恢复已丢失 ticket；无新表/迁移。上游 token 吊销、全局登出与会话续接留后续任务；不要将本地 session 状态作为管理员授权证明。

阶段验证 `AdminOidc:UseSessionForAdminApi=true` 必须同时启用 OIDC；错误组合在启动期失败。新会话管理员身份为已验证 Authority 的唯一 iss/sub 与服务器 stamp，白名单为空或当前移除即 403；入站 Authorization、缺票据/服务器 token 或到期期限为 JSON 401。unsafe 管理请求必须先 GET `/api/auth/csrf`，携带其独立 `adminCsrf` Cookie 与单值 `X-CSRF-TOKEN`；错误为 JSON 400，业务/OSS 删除前拒绝。生产 CSRF Cookie 同为 Secure/HttpOnly/Path=/、SameSite=Lax，仅已验证开发数字 loopback 允许 HTTP；不更改 CORS、不增加跨域凭据。详细响应见 [API 契约](../api.md#可选管理员会话与-csrf42)。

阶段开关 true 时密码 login 固定 410、不读取/转发密码；false 保持旧路径。旧 logout 只清旧 JWT Cookie、仍走旧 Bearer，并未撤销新会话；prepared logout 属 #46。前端与 Student 关联门户 token 尚未迁移，不将新会话开关直接作为全栈上线开关。关闭此开关可回到 legacy，但不会把新 Cookie 当旧 JWT，也不会恢复丢失的内存票据。

生产能力激活还依赖 Ruoyu.Study 的应用 Code 配置与下游 audience 迁移（IKJ8MO）。本项仅用测试 Authority 验证，不代表已修改生产注册或完成平台 E2E。

### OSS

```json
{
  "Oss": {
    "InternalEndpoint": "10.20.30.40:8333",
    "InternalSecure": false,
    "AccessKey": "seaweedfs_admin",
    "SecretKey": "seaweedfs_admin",
    "BucketName": "ruoyu-study",
    "PublicBaseUrl": "https://oss.example.com/oss"
  }
}
```

- `InternalEndpoint/InternalSecure`：Admin 后端审计、清理、下载和迁移辅助使用的实际 S3 连接。
- `PublicBaseUrl`：`GetPresignedUrlAsync` 使用的公共签名地址。
- 浏览器访问 `/oss/` 时由 User Web Nginx 统一代理；Admin Nginx 不维护 SeaweedFS 上游地址。
- `USE_LOCAL_OSS=1` 时使用 `LocalFileOssService`，目录由 `OSS_LOCAL_PATH` 指定，默认 `data/oss`。

### 下游服务

| 依赖 | 仓库占位默认值 | 协议 |
|------|----------|------|
| Student | `http://127.0.0.1:5005` | HTTP |
| Mistake | `http://127.0.0.1:5007` | HTTP |
| Identity | `http://127.0.0.1:5002` | HTTP |
| Teacher Portal | `http://127.0.0.1:5004` | HTTP |
| Assistant Portal | `http://127.0.0.1:5021` | HTTP |
| SeaweedFS | `Oss:InternalEndpoint` | S3 |

上表均为占位值，实际地址一律由部署配置下发，本仓库不记录任何真实环境地址。

下游地址分别来自 Consul 的 `StudentService:Url`、`MistakeService:Url`、`IdentityService:Authority`、`TeacherPortal:Url` 和 `AssistantPortal:Url`。跨主机迁移时需要更新 live KV（需要时再同步 seed KV），并重启 Admin Portal 才会加载新值。

## 数据库启动门

ServiceMantle 0.3.0 的共享生命周期门先于 WebHost 和 `OssAuditWorker.StartAsync`，固定 PostgreSQL MultiInstance、真实 advisory lock 和 30 秒锁等待/准备预算。已有库不连接 maintenance，不需要 CREATEDB；缺库默认拒绝，只有 `Database:AllowCreate=true` 才以相同凭据派生 postgres maintenance 创建并复查连接。非法开关/不可解析连接在门注册前拒绝，无 I/O；失败只输出安全码、取消不记成功。未知结构仍由原 `AuditMigrationExecutor` 拒绝。完整启动与 #66 错误码/回执兼容及回滚说明见 [迁移文档](../database/migrations.md#66-发布兼容说明)。

## 健康检查

ServiceMantle 健康端点（随 #34 落地，#54 起具备真实就绪证据源；匿名可达，不受 SPA 回退影响）：

```bash
curl -s http://localhost:5020/health/live
# 200 {"status":"live"}
curl -s http://localhost:5020/health/ready
# 200 {"status":"ready","phase":"completed","migrationStatus":"succeeded","databaseStatus":"reachable","errorCode":null}
```

- **`/health/live`**：进程存活即恒 200，不读取任何状态，用于容器存活探测。响应携带 `x-correlation-id`。
- **`/health/ready` 与别名 `/health`（#54 起可用于就绪探针与流量门禁）**：就绪 = 本进程共享 `StartupDatabaseGate` 成功（单例 `StartupDatabaseReceipt` 为 Succeeded）**且**一次 3 秒有界的只读 AuditDb 探测（对 EF 映射表执行零行 `SELECT`，不读行、不跑 DDL）成功。健康时返回上例 200；失败时 503 + 固定安全 `errorCode`：未启动/运行中 `ruoyu-admin.startup_incomplete`、启动失败 `ruoyu-admin.startup_failed`、数据库不可达 `ruoyu-admin.database_unreachable`、映射表/列不可读 `ruoyu-admin.schema_unavailable`、探测超时 `health.probe_timeout`、探测失败 `health.probe_failed`。响应与日志不含连接串、主机或异常文本；NotStarted/Running/Failed 零数据库访问，Succeeded 才使用 scoped EF Core 快照。探测只证明映射表列可读，不证明约束、索引或数据正确。每次请求重新采样，数据库恢复后下一个请求即恢复 ready。
- **下游服务（Student/Mistake/Homework/Identity/代理目标）刻意不参与就绪判定**，避免下游抖动导致级联摘除。
- 端点匿名访问：库映射的端点本身不带授权元数据，Admin 通过根路由组的 `AllowAnonymous` 约定豁免全局 FallbackPolicy；`/api/*` 仍要求认证（下条命令验证）。

传统探测方式仍然有效：

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5020/
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5020/api/admin/students
```

首页应返回 200；未携带登录凭据访问受保护的 Admin API 应返回 401。再结合 `docker inspect ruoyu-admin` 的运行状态、重启次数和启动日志判断服务是否稳定。

## 旧批改派生图清理

包含 ADR-0007 的 Admin API 在启动后延迟 2 分钟，自动扫描 `uploads/homework` 并删除严格匹配旧 `.../reviews/{revisionId}.jpg` UUID 路径规则的批改派生图，关联缩略图和残留 OSS 审计记录同步删除。该流程可重试且不匹配学生提交原图；部署后检查日志 `Obsolete homework review image cleanup completed` 获取删除数量或失败告警。

## 数据库备份与恢复

```bash
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_admin | gzip > backup_admin.sql.gz
gunzip -c backup_admin.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_admin
```

### Prepared logout（#46）

`AdminOidc:UseSessionForLogout` 默认 false；true 必须 Enabled 与 UseSessionForAdminApi 同开，并精确注册/配置同 Admin RedirectUri origin 的 `AdminOidc:PostLogoutRedirectUri`，路径固定 `/api/auth/oidc/logout-callback`、生产 HTTPS、无 query/fragment/userinfo。数字 loopback 开发例外沿 OIDC；关闭时保持旧 Bearer logout，不撤新票据。

本人先 GET `/api/auth/logout/csrf` 取得 adminCsrf Cookie 与单值 X-CSRF-TOKEN，再 POST `/api/auth/logout`。专用入口只验证服务器 8h 有效身份票据/唯一 Authority iss/sub/stamp，允许 access token 到期或移出管理员白名单后退出本人，不授业务权限；普通 `/api/auth/csrf` 与管理 API 仍要求有效管理员 token。任何 Authorization 401，CSRF 缺/错/重复/异主体 400、零撤票/HTTP。受保护 Cookie 引用由服务器 TicketDataFormat 读取，锁内 Take 只允许一个并发 winner、Renew 不能复活；先清新 adminSession 和旧 adminAuthToken，再向固定 Authority `/oauth2/logout/requests` 发服务端 client_secret_post、服务器 id_token_hint、精确 PostLogout URI 及随机 state，不自动重试/跟随 redirect，不向浏览器发 ID token/secret。

成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。

完成回跳只支持 GET、单值 state + 独立 HttpOnly/Secure/SameSite=Lax/Path=callback 绑定 Cookie，进程内 4096 容量/5 分钟绝对时限/原子单用；正确 302 `/login?loggedOut=1`，缺/错/重复/到期/错浏览器 400 `logout_callback_invalid`，GET 不发起本地撤票。退出和回跳 no-store/no-cache/no-referrer，回滚不能恢复已撤票据/state。SignaCore 验证 ID token hint 的 iat 24h、忽略 exp，本地 8h 更短；下游已发令牌不保证立即失效。真实 PostLogout/audience 注册与 SPA 激活仍由维护者/#41/IKJ8MO 完成，fake Authority 专项不能代替生产联调。专项 `PreparedLogout_` 与 `AdminLogoutStoresTests` 使用隔离数据库/虚构凭据，无生产 OSS 删除。

### 令牌过期与显式重认证（#45）

在 `UseSessionForAdminApi=true` 模式，有效管理员票据且严格 `expires_at` 到期时，业务门禁为 `401 {"error":"reauthentication_required"}`，零下游/OSS 删除，不清服务器 ID token、不刷新或重放写请求。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false，仅合法 token 到期 false/true（保留显示名），匿名/撤票/8h失效/缺token/坏期限 false/false、显示名 null。当前非管理员403优先；任意 Authorization401。默认legacy状态JSON保持原两字段。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果，BFF不自动挑战。fake测试验证两种回跳时序与deadline前/精确/后/8h、并发，不能证明生产SignaCore时限；真实联调仍归 #41/IKJ8MO。

### 门户服务端凭据（#44）

`AdminOidc:UseSessionForPortalProxies` 默认 false，启用需同时 `Enabled` 与 `UseSessionForAdminApi`。Teacher/Assistant 全代理前缀关闭时在认证前固定 `503 session_portal_proxy_disabled`，零出站；开启时在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询只跟随 `UseSessionForAdminApi`：门户开关关闭也使用服务器 token，失败门禁在 Student/门户调用前拒绝；API开关关闭在任何Student/门户调用与认证之前固定 `503 session_api_disabled`，不采用browser Bearer。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产激活仍受 #41/IKJ8MO 门禁。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

`AdminOidc:UseSessionForIdentityProxy` 默认 false，true 必须同时启用 `Enabled` 和 `UseSessionForAdminApi`。全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。默认 legacy 和 AppSecret 剥离仍保持；门户由独立的 `UseSessionForPortalProxies` 开关控制。生产 audience/角色注册与 SPA 激活仍由 #41/IKJ8MO 验证。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。

门户传输退役浏览器凭据兼容（#74）：两 middleware 只接受 TrustedSessionKey 的有效 server token，settings 为必需依赖，直接调用缺可信项也401。统一 remaining 的 admin/auth/其他三类映射，始终 RequestAborted、剥离浏览器Authorization/Cookie/Host/CSRF/gateway，隔离Set-Cookie；取消传播且无应用层重试。关联查询只随API-session，即使门户proxy开关false也用server token；当前账户筛选、单侧失败保另一侧、无关联短路与Student失败响应保持。此中间镜像不部署，最终须组合#41/#75；整版回滚不恢复本版浏览器凭据路径或复活撤票。
