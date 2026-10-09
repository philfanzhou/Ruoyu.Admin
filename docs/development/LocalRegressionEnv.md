# 本地非生产回归环境 (LocalRegressionEnv)

> 前半部分保留 #24 历史旧完整版本的现场配方；其密码/Bearer 登录步骤不适用于当前 main。当前版本必须组合 #41 托管 SPA，密码入口永久410、认证配置非法时启动拒绝、API拒绝任何 Authorization；最新受控部署配置见 [Deployment.md](./Deployment.md#signacore-托管登录唯一模式)。不能通过开启旧路径恢复当前版本认证。

受管指派需要真实 Student/Mistake、PG/S3 与正常 TLS/注册身份，本文的旧下游替身不模拟 intake 幂等、lease 或 commit 后丢响应，不能替代该验收。配置和真实验证入口见[受管集成契约](../Integration/ManagedAssignment.md)；默认保持能力关闭。

本文档给出一套**在开发机上从干净 clone 到可执行 [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24) 真实环境回归**的完整配方：本地 PostgreSQL + SignaCore 本地 Identity + 三个下游引用来源的本地替身 + `USE_LOCAL_OSS=1` 本地对象目录。该配方保留旧完整版本的登录、下游旧DTO和本地对象目录回归；当前 StorageReferences v1 的真实三方验收须另用生产Host、PG、正常TLS和专属RS256，不能由这些替身代替。所有审计resolve都拒绝删除。

> **安全边界（不可放宽）**
> - 夹具触发的任何 `DeleteAsync` 只能指向 `OSS_LOCAL_PATH`（本地目录）。种子脚本在 `USE_LOCAL_OSS` 未设置为 `1` 时拒绝执行。
> - 任何凭据（Identity `AppId`/`AppSecret`、数据库口令）一律走环境变量或 user-secrets，不进版本控制。
> - 替身与种子脚本不持有、不转发任何真实下游凭据。
> - **删除回归严禁指向生产桶。**

## 进程与端口总览

| 进程 | 端口 | 说明 |
|------|------|------|
| Admin.WebApi（本仓库） | 5020（硬编码） | 被回归的管理端 BFF；SPA 生产模式由容器内 nginx 提供，本地用前端 dev 服务器或拷贝 `wwwroot` |
| SignaCore Identity（本地实例） | 5002 | 登录链路的必要组成，不做替身（见下） |
| 本地替身（单进程） | 5005 / 5007 / 5009 | Student / Mistake / Homework 三个只读引用查询 |
| PostgreSQL（本地实例） | 5432 | 仅 `ruoyu_admin` 两张审计表 |

Teacher Portal（5004）与 Assistant Portal（5021）不在本配方内：#24 对这两个页面只验证「代理 502/503」的降级行为，未配置时即为此行为；如需正向用例再按各自服务文档自行接入。

## 步骤 0：前置依赖

- .NET SDK 10（与 CI 的 `10.0.x` 一致）
- 本地 PostgreSQL 12+（`initdb` + `postgres` 运行中，或 docker 单容器）
- 可选：Node 20 + `frontend/` 的 `npm ci`（若要以前端 dev 服务器访问 UI）

## 步骤 1：PostgreSQL

1. 启动本地 PostgreSQL（示例：`docker run -d --name ruoyu-admin-pg -p 5432:5432 -e POSTGRES_PASSWORD=ruoyu-dev postgres:16`）。
2. 先手工创建数据库 `ruoyu_admin`，或在本地显式设置 `Database__AllowCreate=true` 允许共享启动门创建缺库（默认 false，拒绝码 `database_target_preparation.creation_not_allowed`）。建表由共享 `StartupDatabaseGate` 在真实 advisory lock 下调用 `AuditMigrationExecutor` 执行 EF Core 基线迁移；种子脚本只在启动门完成后写入夹具。
3. `appsettings.json` 的兜底连接串硬编码 `Username=phil`，**必须**按机器用环境变量覆盖（Admin.WebApi 与种子脚本读同一个键）：

```bash
export ConnectionStrings__AuditDb="Host=localhost;Port=5432;Database=ruoyu_admin;Username=ruoyu-dev;Password=ruoyu-dev"
```

## 步骤 2：SignaCore Identity 本地实例（登录回归的必要组成）

#24 的第一项就是登录/登出，且所有 `/api/*` 都在 FallbackPolicy 保护下，因此 Identity 不是可选项。按 [SignaCore](https://github.com/philfanzhou/SignaCore) 仓库的 `docs/development/LocalSetup.md` 与 `FirstRunSetup.md` 启动其本地实例（默认 `http://localhost:5002`），并：

1. 在 Identity 侧注册 `admin_portal` 应用，取得本地 `AppId` / `AppSecret`。
2. 创建一个本地测试用户，记下其 Identity **user id**（GUID）。
3. 把该 user id 加入 `AdminPortal:AdminUserIds` 白名单——`AdminAuthController.Callback` 只对白名单内用户返回 `role:admin`；白名单外用户能登录但拿不到 admin 角色。

Admin.WebApi 的两个启动硬门必须同时满足：

- `IdentityService:AppId` / `AppSecret` 缺失 → 启动即抛 `InvalidOperationException`（`Program.cs`）。
- Authority 或 Issuer 用 `http://` 且 `RequireHttpsMetadata=true` → `IdentityAuthenticationOptionsValidator` 校验失败。本地一律设 `IdentityService:RequireHttpsMetadata=false`。

本地注入方式（二选一，均不进版本控制）：环境变量

```bash
export IdentityService__AppId="<本地 AppId>"
export IdentityService__AppSecret="<本地 AppSecret>"
export IdentityService__Authority="http://localhost:5002"
export IdentityService__Issuer="http://localhost:5002"
export IdentityService__Audience="PlatformAudience"   # 须与 SignaCore 签发 JWT 的 aud 一致
export IdentityService__RequireHttpsMetadata="false"
export AdminPortal__AdminUserIds='["<本地测试用户的 Identity user id>"]'
```

或 user-secrets（在 `backend/Admin.WebApi/` 下 `dotnet user-secrets set "IdentityService:AppId" ...`）。注意 `appsettings.json` **不含** `IdentityService` 节；`appsettings.Development.json` 已含 Authority/Issuer/Audience/RequireHttpsMetadata 的本地默认值，以 Development 环境启动时只需再补 AppId/AppSecret。

## 步骤 3：启动 Admin.WebApi（本地 OSS 模式）

```bash
cd backend
export USE_LOCAL_OSS=1
export OSS_LOCAL_PATH=data/oss        # 默认即此；相对 CWD（backend/）解析
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

首次启动由共享启动门执行 EF Core 基线迁移建表，成功后才监听并启动 worker（种子脚本要求表已存在）。已有库只 Observe，不需要 CREATEDB；未知结构拒绝接管。readiness 使用共享单例回执与 scoped EF Core 只读映射探针；未启动/运行中/失败回执零数据库访问，正常就绪 JSON 保持。#66 错误码/回执兼容与回滚见 [迁移文档](../database/migrations.md#66-发布兼容说明)。验证：`curl http://localhost:5020/` 返回运行文案；`http://localhost:5020/swagger`（Development）可见。

前端二选一：`cd frontend && npm run dev`（8090，代理 `/api/*` 到 5020），或 `npm run build` 后把 `dist/*` 拷入 `backend/Admin.WebApi/wwwroot/` 走集成模式。

## 步骤 4：种子脚本（夹具数据）

```bash
cd <仓库根>
USE_LOCAL_OSS=1 \
OSS_LOCAL_PATH=data/oss \
ConnectionStrings__AuditDb="$ConnectionStrings__AuditDb" \
dotnet run --project backend/Tools/LocalRegressionStubs -- seed
```

种子写入三类内容（全部确定性，**重复执行两次结果一致**）：

| 类别 | 内容 |
|------|------|
| 本地对象目录 | 5 个对象 + 各 3 个缩略图（`uploads/regression/...`、`mistakes/regression/...`） |
| `ruoyu_admin` | 1 条已完成审计 run（`TriggerType=regression-seed`）+ 4 条待处理（Status=0）记录 + 1 条已忽略（Status=2）记录 |
| 夹具 JSON | `data/regression/fixtures.json`（替身 serve 模式的数据源） |

种子中的旧Status0/2保持历史；当前视图没有选择或删除操作。完整v1引用证明使单条/非空批量resolve返回409 `cleanup_not_authorized`，配置缺失或来源不全为502 `references_unavailable`，对象与审计记录原样保留。status3是新扫描的“未观察到引用”只读观察，不能ignore或转成删除许可。

拒绝与失败行为：`USE_LOCAL_OSS` 未设为 `1` 时拒绝执行并提示；连接串缺失时拒绝执行；表不存在时失败并提示「先启动一次 Admin.WebApi 建表」。

## 步骤 5：本地替身（serve 模式）

```bash
dotnet run --project backend/Tools/LocalRegressionStubs -- serve
# 可选：--mistake-outage（或 MISTAKE_STUB_OUTAGE=1）模拟 Mistake 不可达，见下文
```

单进程监听 :5005 / :5007 / :5009，仅实现保留的旧HTTP客户端DTO查询；实际审计worker/controller不再调用这些查询，固定使用StorageReferences共享collector：

| 端口 | 端点 | 聚合方 |
|------|------|--------|
| 5005 | `GET /api/uploads?page=&pageSize=`（`items[].imageEntries[].path` + `totalCount`） | `StudentHttpClient.GetAllUploadRecordsAsync` |
| 5007 | `GET /api/mistakes?page=&size=`（`items[].sourceRegions[].sourceImagePath` + `pageMeta.totalCount`） | `MistakeHttpClient.GetMistakeItemListAsync` |
| 5009 | `GET /api/admin/storage/image-references?page=&size=`（`success` envelope + `data.paths[]` + `data.hasMore`） | `HomeworkReferenceClient.GetAllImagePathsAsync` |

替身不模拟下游鉴权、限流与错误形态。其响应与 ServiceClients DTO 的绑定由 `backend/Tests/Services/LocalRegressionStubsContractTests.cs` 在 CI 中锁定。验证：

```bash
curl -s 'http://localhost:5005/api/uploads?page=1&pageSize=100'   # items[].imageEntries[].path
curl -s 'http://localhost:5007/api/mistakes?page=1&size=100'      # items[].sourceRegions[].sourceImagePath
curl -s 'http://localhost:5009/api/admin/storage/image-references?page=1&size=200'
```

## 步骤 6：当前审计回归边界

旧替身没有实现StorageReferences v1。仅配置旧Student/Mistake/Homework URL时，trigger可受理后Run失败为Status2，零S3列举；resolve为502。不能通过空集fallback或复用旧offset查询恢复“成功”。真实报告回归按[StorageAudit](../modules/OssAudit/StorageAudit.md)配置三个生产provider和正常TLS，显式启用专属服务信任，成功Run保存全部metadata而所有resolve仍为409。

worker不再执行startup旧Homework review图或非审计桶记录清理；符合旧路径形态的对象和原审计历史也保留。每日`OssAudit:ScheduledHour/Minute`仍触发同一报告扫描，任何来源失败都在列举之前中止；成功扫描不重复插入已有ObjectPath。

## 常见问题

当前 Mistake 出站会话回归不使用上述旧匿名替身证明授权。`MistakeService:UseSessionToken` 默认 false，true 必须显式根 origin（path 为 `/`），http 与 https 同等接受（结构规则不变，禁止 localhost）。运行 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~MistakeSession"` 验证全 14 方法和真 Program 握手、票据缓存后换/撤票、并发、未知结果、取消与后续 Student/S3 零写；fake Authority 与 TCP receiver 仅证明自动化边界。最终版本还须在自有隔离环境用官方 SignaCore、真实当前 Mistake/Student 与实际 S3 取图。严格目标 reviewer/SourceIntake/managed immutable/pins 保护不得为回归关闭；真实目标 401/403 为 Admin 安全 502，失败/取消不自动重试，未知写结果不能声称未提交。配置及回滚见 [Deployment](./Deployment.md#mistake-出站会话ikjnxa)。

- **启动即抛 `InvalidOperationException`**：`IdentityService:AppId/AppSecret` 未注入（步骤 2）。
- **认证校验失败提示 RequireHttpsMetadata**：Authority/Issuer 用了 `http://` 但没设 `IdentityService:RequireHttpsMetadata=false`。
- **登录成功但接口 403 / 无 admin 权限**：测试用户不在 `AdminPortal:AdminUserIds` 白名单。
- **种子失败「表不存在」**：先完成步骤 3 让共享启动门完成基线迁移建表。
- **审计运行始终 `Status=2`**：未配置/未启用StorageReferences，或三个正常TLS生产provider中任一合同、身份、分页或连接失败；旧替身不满足此合同。


## 可选 OIDC 基础回归（#38）

上述 #24 stub/mock 配方描述旧后端密码/JWT，可搭配匹配的旧 SPA 回归，不是当前托管登录的联合验收。当前 SPA 须按 [LocalSetup.md](./LocalSetup.md#托管登录开发配置) 配置必需认证参数、使用官方 Provider 与实际 Identity/Teacher/Assistant 下游。Vite 的 callback 和 post-logout 必须使用浏览器 `http://127.0.0.1:8090` 原点；直接集成后端才使用 5020，均不注册 localhost。生产 HTTPS 代理必须保持 BFF 看到 HTTPS（当前无转发头信任），使用正常 CA 的 HTTPS 上游，并验证两种 CSRF 入口；OSS 公共代理保留原签名 bucket/key。完整配置与 query 禁日志要求见 [Deployment.md](./Deployment.md#signacore-托管登录唯一模式)。Secret 仍只通过环境变量或 user-secrets 注入。

真 Program 的自动化握手由 `OidcTestAuthority` 隔离提供 Discovery/JWKS/token、测试 RSA 与虚构 canary，执行 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"`（需要 Docker）。包含严格签名与回调拒绝、取消、一次消费/并发、Cookie 引用/内存 token、过期/重启、匿名 SPA/健康以及默认开关 false 时新 Cookie 不放行 Admin API；它不表示生产 ADMIN Code 注册和下游 audience（Ruoyu.Study IKJ8MO）已就绪。

Session API 专项使用合法必需认证配置与 fake OIDC：真 Program 覆盖管理员会话/native image、401/403、任何 Authorization 拒绝、服务器 token 缺失/严格期限、GET csrf 的 Cookie/header/主体绑定、所有写方法（含 DELETE）、并发与取消、410 密码退役。五旧 false/畸形输入验证启动拒绝，absent/规范 true 只验证迁移输入。专项命令为 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~SessionApi_"`，使用隔离数据库与 mock OSS，不删除生产对象。审计resolve保持三provider collector完整409/不可达502及零Delete。

当前版本只运行服务器会话托管登录。必须配置 `IdentityService:Authority/AppId/AppSecret`、`AdminOidc:RedirectUri/PostLogoutRedirectUri`、当前管理员白名单与 ADMIN 应用 audience，并完成真实下游信任及精确 Code/Logout 注册。五个旧键 `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` 不再选择运行模式：应删除；仅缺省或规范小写 `true` 可通过迁移检验，显式 `false`、空值或畸形值在所有环境启动拒绝。密码入口永久 `410 legacy_login_disabled`，API/图片/三个代理/关联查询始终使用同一管理员会话与 CSRF 边界，任何入站 Authorization 固定401；不存在关闭能力或浏览器 JWT 回退模式。

### Prepared logout（#46，#83 起由 SignaCore 包承担）

退出始终使用 prepared logout，端点为包拥有的 `POST /api/auth/oidc/logout`。必需的 `AdminOidc:PostLogoutRedirectUri` 与 RedirectUri 同 origin，路径固定 `/api/auth/oidc/logout/return`、无 query/fragment/userinfo；SignaCore 应用注册的 PostLogout 回调需由维护者改为该值。http 与 https 同等接受（结构规则不变），公网部署应使用 HTTPS（建议而非强制）；非 POST logout 固定405，每次 POST 尝试都清理旧 `adminAuthToken` Cookie。

SPA 先 GET `/api/auth/oidc/csrf`（公开签发 `{"token":"..."}`，轮换共享 `adminCsrf` Cookie），再以导航式 form POST 提交 `__RequestVerificationToken`（或单值 `X-CSRF-TOKEN` header）。缺/错 token 固定 `400 {"outcome":"csrf_rejected"}` 且不触碰会话；登出不以 access token 期限或管理员白名单阻止本人退出。包在按会话键串行的门内先移除票据、删除会话 Cookie，再向 Authority `/oauth2/logout/requests` 发 HTTP Basic 认证 + 服务器 id_token_hint + PostLogout URI + 一次性 state，不重试不跟随 redirect；成功 302 验证过的同源 logout_uri，失败/超时/取消固定 `200 {"outcome":"local_only"}`。回跳 `/api/auth/oidc/logout/return?state=...` 以绑定 Cookie（5 分钟）原子单用，正确 302 `/login`，其余固定 400 HTML。


成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。


### 令牌过期与显式重认证（#45）

在唯一会话模式，有效管理员票据且严格 `expires_at` 到期时，业务门禁为 `401 {"error":"reauthentication_required"}`，零下游/OSS 删除，不清服务器 ID token、不刷新或重放写请求。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false，仅合法 token 到期 false/true（保留显示名），匿名/撤票/8h失效/缺token/坏期限 false/false、显示名 null。当前非管理员403优先；任意 Authorization401。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果，BFF不自动挑战。fake测试验证两种回跳时序与deadline前/精确/后/8h、并发，不能证明生产SignaCore时限；真实联调仍归 #41/IKJ8MO。

### 门户服务端凭据（#44）

Teacher/Assistant 全代理前缀在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询共用相同管理员边界，只转发服务器 token，失败门禁在 Student/门户调用前拒绝。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产 rollout 不在 #75 实现范围；当前组合的正式验证使用官方 Provider 与真实下游。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。AppSecret 剥离始终保持，三个代理共用相同会话边界。生产平台注册不由本项执行；正式组合门禁归 #75。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。
