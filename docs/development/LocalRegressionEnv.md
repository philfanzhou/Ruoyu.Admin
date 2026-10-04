# 本地非生产回归环境 (LocalRegressionEnv)

受管指派需要真实 Student/Mistake、PG/S3 与正常 TLS/注册身份，本文的旧下游替身不模拟 intake 幂等、lease 或 commit 后丢响应，不能替代该验收。配置和真实验证入口见[受管集成契约](../Integration/ManagedAssignment.md)；默认保持能力关闭。

本文档给出一套**在开发机上从干净 clone 到可执行 [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24) 真实环境回归**的完整配方：本地 PostgreSQL + SignaCore 本地 Identity + 三个下游引用来源的本地替身 + `USE_LOCAL_OSS=1` 本地对象目录。全程不依赖任何 rc 发布，Storage Audit 的处置动作触发的**真实删除只作用于本地目录**。

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

夹具与 #24 事件 × 结果表的对应：

| 夹具对象 | 预期分支 |
|----------|----------|
| `uploads/regression/orphan-object.jpg` | 无引用孤儿 → 单条 resolve **200**，原图 + 全部缩略图真实删除，该行从列表消失 |
| `uploads/regression/referenced-by-upload.jpg` | 被上传记录引用 → **400**「该文件仍被上传记录引用，不能删除。」 |
| `mistakes/regression/referenced-by-mistake.jpg` | 被错题记录引用 → **400**「该文件仍被错题记录引用，不能删除。」 |
| `uploads/regression/referenced-by-homework.jpg` | 被 Homework 引用 → **400**（归入 registered 分支，报「上传记录引用」文案） |
| `uploads/regression/ignored-object.jpg`（Status=2） | 非待处理行：checkbox `disabled`、操作列「—」、批量计数不统计 |

拒绝与失败行为：`USE_LOCAL_OSS` 未设为 `1` 时拒绝执行并提示；连接串缺失时拒绝执行；表不存在时失败并提示「先启动一次 Admin.WebApi 建表」。

## 步骤 5：本地替身（serve 模式）

```bash
dotnet run --project backend/Tools/LocalRegressionStubs -- serve
# 可选：--mistake-outage（或 MISTAKE_STUB_OUTAGE=1）模拟 Mistake 不可达，见下文
```

单进程监听 :5005 / :5007 / :5009，只实现审计引用聚合逐字依赖的三个只读查询（`OssAuditWorker` / `OssAuditController`）：

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

## 步骤 6：执行 #24 的回归项

登录 → 触发审计（或直接使用种子插入的待处理记录）→ 按 #24 事件 × 结果表逐行执行。两条不可达分支的触发方式：

1. **Student 或 Homework 不可达 → 502 / 审计中止**：直接停掉替身进程（三个来源同时不可达；Student 侧的 `HttpRequestException` 会最先触发「Student 或 Homework 服务不可用」的 502 与整轮 `Status=2` 中止，中止发生在扫描任何桶之前）。
2. **Mistake 不可达 → 502「Mistake 服务不可用，无法安全删除。」**：只停掉 5007 的 Mistake 替身（保留 5005/5009）即可——连接拒绝的 `HttpRequestException` 会从 `MistakeHttpClient.GetMistakeItemListAsync` 向上传播（[#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28) 修复后），删除前复核与审计聚合都得到确定性的 Mistake 失败分支。也可用 `--mistake-outage` 启动替身——`/api/mistakes` 返回 503 + 非 JSON 文本，走 `JsonException` 传播路径，结论相同。

> **历史缺陷说明**：[#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28) 修复前，`GetMistakeItemListAsync` 会捕获 `HttpRequestException` 并返回空集，「只停 Mistake」场景不会 502 也不会中止；本节方式 2 曾因此只依赖 `JsonException` 路径。修复后连接拒绝与非 JSON 两条路径结论一致。

### OssAuditWorker 在本地运行的三个隐藏动作

1. 启动约 **2 分钟后**的 legacy 清理会**真实删除** `uploads/homework/{guid}/{guid}/reviews/{guid}.jpg` 形态的文件及同名审计记录。夹具使用 `regression/` 路径段，**不会撞上**该形态；如需验证该清理，可自行放置符合形态的文件。
2. 同期 `CleanupUnauditedBucketAuditRecordsAsync` 会删除桶名不在 `uploads`/`mistakes` 的审计记录。种子只用这两个桶名。
3. 每日 `OssAudit:ScheduledHour/Minute`（默认 **02:00**）会再触发全量扫描。长时间挂机的本地环境会在凌晨自触发一轮审计（结果幂等：已有记录不会重复插入）。

### 审计中止不变量（不得为了跑通而降级）

Student、Homework 任一 `HttpRequestException`，或 Mistake 的异常传播到聚合方，都会让整轮审计以 `Status=2` 中止，且中止发生在扫描任何桶之前。替身未启动就触发审计（`POST /api/admin/oss-audit/trigger`）即应观察到该行为（Student 分支先触发）。**不要**为了让审计"跑完"而放宽任何来源的可达性要求。

## 常见问题

- **启动即抛 `InvalidOperationException`**：`IdentityService:AppId/AppSecret` 未注入（步骤 2）。
- **认证校验失败提示 RequireHttpsMetadata**：Authority/Issuer 用了 `http://` 但没设 `IdentityService:RequireHttpsMetadata=false`。
- **登录成功但接口 403 / 无 admin 权限**：测试用户不在 `AdminPortal:AdminUserIds` 白名单。
- **种子失败「表不存在」**：先完成步骤 3 让共享启动门完成基线迁移建表。
- **审计运行始终 `Status=2`**：替身未启动（步骤 5），或 `StudentService/MistakeService/HomeworkService:Url` 指向了错误端口。


## 可选 OIDC 基础回归（#38）

上述 #24 配方仍走旧密码/JWT。新握手默认关闭，显式启用与精确注册见 [LocalSetup.md](./LocalSetup.md#可选托管登录开发配置)。本地 callback 使用 `http://127.0.0.1:5020/api/auth/oidc/callback`，不要注册 localhost；生产 HTTPS/TLS 与代理 query 禁日志要求见 [Deployment.md](./Deployment.md#可选-signacore-托管登录)。Secret 仍只通过环境变量或 user-secrets 注入。

真 Program 的自动化握手由 `OidcTestAuthority` 隔离提供 Discovery/JWKS/token、测试 RSA 与虚构 canary，执行 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"`（需要 Docker）。包含严格签名与回调拒绝、取消、一次消费/并发、Cookie 引用/内存 token、过期/重启、匿名 SPA/健康以及默认开关 false 时新 Cookie 不放行 Admin API；它不表示生产 ADMIN Code 注册和下游 audience（Ruoyu.Study IKJ8MO）已就绪。

#42 增加默认 false 的 `AdminOidc:UseSessionForAdminApi`。在本地测试显式同时开启 OIDC 与它，配置管理员白名单后，真 Program 测试覆盖管理员会话/native image、401/403、任意 Authorization 拒绝、服务器 token 缺失/严格期限、GET csrf 的 Cookie/header/主体绑定、所有写方法（含 DELETE）、并发与取消、410 密码退役；三个未迁移代理新 Cookie 单独仍 401。专项命令为 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~SessionApi_"`，使用测试 Authority/虚构令牌、Testcontainers 隔离数据库与 mock OSS，不删除生产对象。单条/批量 OSS 处置在门禁失败时零 Delete，门禁成功后仍逐来源引用复核并对 Student/Homework/Mistake 不可达返回 502；审计 trigger 同样先过门禁。

前端保持旧流程，不能在生产提前整体切换此开关。session 最长 8 小时绝对、state 5 分钟，均为单进程有界内存，重启失效；access token 到期独立拒绝、不按8小时续期。后续代理/关联转发、续接/登出、SPA 切换分别由 #43/#44、#45/#46、#41 推进；旧 `POST /api/auth/logout` 仍用旧 Bearer，只清旧 JWT Cookie，不负责新会话或全局登出。密码 login 在边界 true 为固定 410，false 保持旧行为；匿名 claims/OIDC callback、SPA、health 保持原协议。CSRF 的 Cookie/TLS 与当前授权响应见 [API 契约](../api.md#可选管理员会话与-csrf42)。

### Prepared logout（#46）

`AdminOidc:UseSessionForLogout` 默认 false；true 必须 Enabled 与 UseSessionForAdminApi 同开，并精确注册/配置同 Admin RedirectUri origin 的 `AdminOidc:PostLogoutRedirectUri`，路径固定 `/api/auth/oidc/logout-callback`、生产 HTTPS、无 query/fragment/userinfo。数字 loopback 开发例外沿 OIDC；关闭时保持旧 Bearer logout，不撤新票据。

本人先 GET `/api/auth/logout/csrf` 取得 adminCsrf Cookie 与单值 X-CSRF-TOKEN，再 POST `/api/auth/logout`。专用入口只验证服务器 8h 有效身份票据/唯一 Authority iss/sub/stamp，允许 access token 到期或移出管理员白名单后退出本人，不授业务权限；普通 `/api/auth/csrf` 与管理 API 仍要求有效管理员 token。任何 Authorization 401，CSRF 缺/错/重复/异主体 400、零撤票/HTTP。受保护 Cookie 引用由服务器 TicketDataFormat 读取，锁内 Take 只允许一个并发 winner、Renew 不能复活；先清新 adminSession 和旧 adminAuthToken，再向固定 Authority `/oauth2/logout/requests` 发服务端 client_secret_post、服务器 id_token_hint、精确 PostLogout URI 及随机 state，不自动重试/跟随 redirect，不向浏览器发 ID token/secret。

成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。

完成回跳只支持 GET、单值 state + 独立 HttpOnly/Secure/SameSite=Lax/Path=callback 绑定 Cookie，进程内 4096 容量/5 分钟绝对时限/原子单用；正确 302 `/login?loggedOut=1`，缺/错/重复/到期/错浏览器 400 `logout_callback_invalid`，GET 不发起本地撤票。退出和回跳 no-store/no-cache/no-referrer，回滚不能恢复已撤票据/state。SignaCore 验证 ID token hint 的 iat 24h、忽略 exp，本地 8h 更短；下游已发令牌不保证立即失效。真实 PostLogout/audience 注册与 SPA 激活仍由维护者/#41/IKJ8MO 完成，fake Authority 专项不能代替生产联调。专项 `PreparedLogout_` 与 `AdminLogoutStoresTests` 使用隔离数据库/虚构凭据，无生产 OSS 删除。

### 令牌过期与显式重认证（#45）

在 `UseSessionForAdminApi=true` 模式，有效管理员票据且严格 `expires_at` 到期时，业务门禁为 `401 {"error":"reauthentication_required"}`，零下游/OSS 删除，不清服务器 ID token、不刷新或重放写请求。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false，仅合法 token 到期 false/true（保留显示名），匿名/撤票/8h失效/缺token/坏期限 false/false、显示名 null。当前非管理员403优先；任意 Authorization401。默认legacy状态JSON保持原两字段。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果，BFF不自动挑战。fake测试验证两种回跳时序与deadline前/精确/后/8h、并发，不能证明生产SignaCore时限；真实联调仍归 #41/IKJ8MO。

### 门户服务端凭据（#44）

`AdminOidc:UseSessionForPortalProxies` 默认 false，启用需同时 `Enabled` 与 `UseSessionForAdminApi`。Teacher/Assistant 全代理前缀在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询只跟随 `UseSessionForAdminApi`：门户开关关闭也使用服务器 token，失败门禁在 Student/门户调用前拒绝；默认API关闭保持legacy Bearer。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产激活仍受 #41/IKJ8MO 门禁。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

`AdminOidc:UseSessionForIdentityProxy` 默认 false，true 必须同时启用 `Enabled` 和 `UseSessionForAdminApi`。全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。默认 legacy 和 AppSecret 剥离仍保持；门户由独立的 `UseSessionForPortalProxies` 开关控制。生产 audience/角色注册与 SPA 激活仍由 #41/IKJ8MO 验证。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。
