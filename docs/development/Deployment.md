# 部署与运维

受管错题指派默认关闭，启用前须配置合法 HTTPS Mistake origin 与现有可信身份，确认实际 Student metadata 和 Mistake intake 已交付。错误 bool / 缺 HTTPS / 缺必要 trust 在业务写入前拒绝启动；关闭不删除上游历史。配置、TLS/token 接线与回退边界见[受管集成契约](../Integration/ManagedAssignment.md)。

## 构建与启动

- 集成镜像：`backend/Admin.WebApi/Dockerfile`，先构建 Vue 前端，再把产物复制到 API 的 `wwwroot`。构建入口 `./scripts/build.sh`，产物 `ruoyu.admin:${IMAGE_TAG}`（`IMAGE_TAG` 默认 `20260502`）。
- 启动脚本：仓库根 `start.sh`。
- 容器：`ruoyu-admin`，容器内监听 5020，默认映射到宿主机 5020（宿主端口与容器端口一致，与平台其他 API 的映射惯例相同；monorepo 时代的 10901 已废弃）。
- 容器使用 Docker 默认 bridge，不依赖 `ruoyu-net` 或容器名解析；跨主机依赖通过 Consul KV 中的局域网地址访问。
- Dockerfile 的构建上下文是**仓库根**，受仓库根 `.dockerignore` 约束。
- 发布镜像：推送 `vX.Y.Z-rc.N`（测试版）或 `vX.Y.Z`（正式版）tag 后，CI（`.github/workflows/ci.yml`）会把镜像发布到 GHCR：`ghcr.io/philfanzhou/ruoyu.admin`。镜像 tag 不带 `v` 前缀（保持 `X.Y.Z` 形态），GitHub Release 标题即 tag 本身。正式版同时移动 `X.Y` 与 `latest`；rc 只保留不可变版本标签。部署主机可以不从源码构建，直接 `docker pull ghcr.io/philfanzhou/ruoyu.admin:<version>`。
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

### 认证只读预检与升级

`dotnet Admin.WebApi.dll --validate-auth-config` 先读取正式启动相同的 appsettings/environment/command-line 和 Consul 有效配置，再复用同一认证校验；成功 stdout 固定 `RUOYU_ADMIN_AUTH_CONFIG_VALID`、exit0，失败 stderr 固定 `RUOYU_ADMIN_AUTH_CONFIG_INVALID`、exit2。不创建宿主、迁移、worker、监听或会话。允许读取 Consul/既有 cache，禁止预检刷新或改写 cache，配置值不进入输出。正式启动仍按现有规则缓存成功取得的 Consul 配置快照；Consul 不可达时使用已有缓存，无缓存时使用 appsettings，环境变量与命令行配置始终覆盖快照。

先由部署角色取得已有注册码及 secret，按精确 URI 注册 Code+PKCE 和 Logout，确认正常 CA/SAN 与同源 HTTPS 到 BFF；移除五旧键、通过目标集成镜像预检后再替换。`start.sh` 使用 `--image`（或 `IMAGE_NAME`）选择具体版本，`--container` 选择精确实例，`--authority`、`--redirect-uri`、`--post-logout-redirect-uri` 提供外部 URI。凭据沿环境或受限 `--env-file` 注入，`--config-file` 可挂载明确 appsettings 文件；`--cache-dir` 可省略，提供时指向已有 cache 目录，预检只读挂载，正式使用同目录写原 cache。脚本支持系统 Bash 3.2，包括省略 cache 的启动。不存在目录或无效路径直接失败，不创建目录。预检在任何旧容器停止/删除前使用同一目标 image 与同组配置；失败保留原容器。旧容器须有本 launcher label 或原 Ruoyu.Admin 产品 image 身份，防止误换其他应用；成功仍仅替换显式实例并保留5020。此认证预检不证明 DB、下游、网络或生产平台注册已可用。

```bash
./start.sh --image ghcr.io/philfanzhou/ruoyu.admin:<version> --container ruoyu-admin \
  --env-file /private/admin.env --cache-dir /private/admin-consul-cache \
  --authority https://identity.example.com \
  --redirect-uri https://admin.example.com/api/auth/oidc/callback \
  --post-logout-redirect-uri https://admin.example.com/api/auth/oidc/logout/return
```

替换/重启使现有内存票据失效，不恢复已撤票据；回滚须使用此前明确可用的完整集成镜像与其匹配配置，不能关闭旧键回退身份模式。#75 的实际固定 head/image 正常 TLS 组合验收不代替生产切流，生产 rollout 和共享平台注册不在实现 PR 范围。

### SignaCore 托管登录（唯一模式）

当前版本只运行服务器会话托管登录。必须配置 `IdentityService:Authority/AppId/AppSecret`、`AdminOidc:RedirectUri/PostLogoutRedirectUri`、当前管理员白名单与 ADMIN 应用 audience，并完成真实下游信任及精确 Code/Logout 注册。五个旧键 `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` 不再选择运行模式：应删除；仅缺省或规范小写 `true` 可通过迁移检验，显式 `false`、空值或畸形值在所有环境启动拒绝。密码入口永久 `410 legacy_login_disabled`，API/图片/三个代理/关联查询始终使用同一管理员会话与 CSRF 边界，任何入站 Authorization 固定401；不存在关闭能力或浏览器 JWT 回退模式。

必需 `IdentityService:Authority/AppId/AppSecret` 与 `AdminOidc:RedirectUri/PostLogoutRedirectUri`，精确注册 `https://admin.example.com/api/auth/oidc/callback`（占位示例）。URI 必须与配置 byte-for-byte 相同，路径固定、无 query/fragment/userinfo/wildcard；不允许 localhost。传输安全是部署决策（issue #94，对齐 SignaCore #566）：代码层 http 与 https 同等接受，任意环境名（含 Production）、任意 host 形态（数字 loopback、私网/公网 IP 或域名）无差别，仅保留结构性 URI 规则；同一语义作用于登录/登出回调、`IdentityService:Authority` 与启用 `MistakeService:UseSessionToken` 时的 `MistakeService:Url`。公网部署应使用 TLS（反向代理 / TLS 终止 / 网络分区，建议而非强制，职责在部署侧）；回滚=改回 HTTPS URI 配置并重启，无迁移、无状态转换，切换 Cookie Secure 派生需重新登录。0.1.15 部署若残留旧的内网 HTTP origin 清单键，直接删除即可：残留键不被读取、不阻断启动。SignaCore Host 0.1.16 起同样移除 HTTP origin 门；更早的 Host 版本须按其自身 Testing 门放行本服务的 HTTP redirect origin（部署侧事实）。Authority 去结尾 `/` 后与 Discovery issuer 严格相同；OIDC 不使用 JWT AdditionalValidIssuers 放宽信任。错误配置启动失败，公开输出固定安全代码，配置校验内部只使用键名。

按 [SignaCore HostedLogin](https://github.com/philfanzhou/SignaCore/blob/52c68c812335dd6a967cb90042ac91c21543d387/docs/integrations/HostedLogin.md) 注册 Confidential、PerApplication audience、精确 Redirect URI 和 Code + openid profile + S256，不启用 refresh。Admin 从 Discovery 取授权/token/JWKS 端点，不发送 PAR、prompt 或其他不支持字段；兑换为 client_secret_basic（凭据只在 Authorization 头，0.1.14+ 并以零时钟偏差的严格默认校验 token 期限与未来 `iat`，本仓不放宽），固定外部 RedirectUri 与原 verifier，不信任入站 Host/X-Forwarded-Host 拼地址。Cookie Secure 标志按配置入口 URI 的实际 scheme 派生（SignaCore.Client.AspNetCore 0.1.16 起两侧同语义）：http 入口时本仓 `adminCsrf` 使用 `CookieSecurePolicy.SameAsRequest`、包自身全部 Cookie 不带 Secure；https 入口保持 `Always`/Secure。公网部署应让浏览器原点与 BFF 处理 CSRF 的请求都保持 HTTPS（建议而非强制）。当前服务不处理任意转发头，不能仅由外层 TLS 终止后把 HTTP 转给 5020，再依赖 `X-Forwarded-Proto` 声称安全；生产 `CookieSecurePolicy.Always` 会拒绝这种 CSRF 配置。保留硬编码 HTTP 5020，可通过标准 `Kestrel:Endpoints` 配置增加内部 HTTPS 监听，代理以正常 CA 验证 HTTPS 上游，不改端口契约或扩大转发头信任。

受控 HTTPS 联调的配置例（端口和证书路径由部署选择，密码只注入受限环境）：

```text
Kestrel__Endpoints__AdminHttps__Url=https://0.0.0.0:5022
Kestrel__Endpoints__AdminHttps__Certificate__Path=/tls/server.pfx
Kestrel__Endpoints__AdminHttps__Certificate__Password=<private environment value>
```

纯内网受控网络（无 TLS 终止设施、划分 VLAN 的管理面）不需要此 HTTPS 监听：代码层 http 与 https 同等接受，URI 直接配置内网明文地址（例如 `http://192.168.55.10:5020/api/auth/oidc/callback` 与 `http://192.168.55.10:5002` 的 Authority），无需任何额外开关；是否 TLS 由部署选择，完整 URI 仍须逐项精确注册。

代理使用 `proxy_pass https://admin:5022`、`proxy_ssl_verify on` 与 `proxy_ssl_trusted_certificate`，并设置与证书 SAN 匹配的 `proxy_ssl_name`。证书/PFX 只挂载到本实例，CA 只信任本实例及自有浏览器，不全局导入、不跳过验证。注册 callback 仍为浏览器的 HTTPS 同源地址，5020 仍保留；上述 HTTPS 监听不替代认证必需配置。实际联合验证还须确认 `/api/auth/csrf` 与 `/api/auth/oidc/csrf` 都返回 200，不能把托管登录成功当成 CSRF 可用。

反向代理必须避免 callback query（尤其 code）进入 access log/analytics，并保证原始单值 query 原样转发。Admin 协议 handler 禁用可能包含 token/URL/Cookie 的框架详细日志；启用 OIDC 时另外抑制会记录原始 query 的 `Microsoft.AspNetCore.Hosting.Diagnostics` 请求日志（包含其余路由的此类日志），OIDC 入口不导出 ASP.NET Core trace；应用调用方仍不得将敏感值插入自由文本。AppSecret 只通过环境变量/user-secrets/Consul 安全配置，不提交配置文件。

单实例内存 state 最长 5 分钟、43 字符随机引用、单次原子消费；票据 8 小时绝对到期、不滑动，所有 token 留服务器。两类存储各最多 4096 项、每分钟回收；满载固定失败，重启丢失，不能多副本共用会话。数据保护 keys 即使还在也不会恢复已丢失 ticket；无新表/迁移。没有 refresh；prepared logout 使用现有服务器 ID token 与专用退出 CSRF，具体边界见下文。不要将前端显示状态作为管理员授权证明。

服务器会话为唯一模式，非法配置在启动期失败。新会话管理员身份为已验证 Authority 的唯一 iss/sub 与服务器 stamp，白名单为空或当前移除即 403；入站 Authorization、缺票据/服务器 token 或到期期限为 JSON 401。unsafe 管理请求必须先 GET `/api/auth/csrf`，携带其独立 `adminCsrf` Cookie 与单值 `X-CSRF-TOKEN`；错误为 JSON 400，业务/OSS 删除前拒绝。CSRF Cookie 为 HttpOnly/Path=/、SameSite=Lax，Secure 策略按配置入口 scheme 派生（http 入口 `SameAsRequest`，https 入口 Secure）；不更改 CORS、不增加跨域凭据。详细响应见 [API 契约](../api.md#管理员会话与-csrf42)。

任何配置下密码 login 固定 `410 legacy_login_disabled`，不读取/转发密码。当前 SPA 退出使用专用 logout CSRF，服务器先原子撤销本地票据再准备上游退出；失败/丢失响应只查询本地事实，不自动重放，不宣称上游注销完成。非POST退出405。Student关联查询与三个代理始终使用服务器token边界。旧键不能恢复浏览器凭据路径；整版回滚也不能复活已撤销或丢失票据。

生产能力激活还依赖 Ruoyu.Study 的应用 Code 配置与下游 audience 迁移（IKJ8MO）。本配置文档不代表生产注册已修改；55 联合验收必须固定官方 Provider 版本并连接实际三个下游，测试 Authority 单元结果不能代替。

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
- `PublicBaseUrl`：`GetPresignedUrlAsync` 使用的公共签名地址。 若地址带 `/oss` 前缀，公开代理只移除该部署前缀，保留签名 URL 中原有的 bucket/key；重复附加 bucket 会破坏 S3 签名。验证实际列表缩略图、预览图和原图，不能只看代理健康状态。
- 浏览器访问 `/oss/` 时由 User Web Nginx 统一代理；Admin Nginx 不维护 SeaweedFS 上游地址。
- `USE_LOCAL_OSS=1` 时使用 `LocalFileOssService`，目录由 `OSS_LOCAL_PATH` 指定，默认 `data/oss`。

### 下游服务

#### Mistake 出站会话（IKJNXA）

`MistakeService:UseSessionToken` 缺省为 `false`；显式值必须可解析为布尔值。它只选择 Admin 到 Mistake 的出站传输，入站管理 API 永久使用 AdminSession。`false` 保留原匿名下游、DTO 与异常兼容行为，不恢复浏览器 Bearer。

启用前配置 `MistakeService:Url=https://mistake.example.com`，只能是显式根 API origin，无 userinfo、query、fragment 或子路径。传输安全是部署决策（issue #94）：http 与 https 同等接受（结构规则不变），禁止 localhost；公网部署应使用 HTTPS 与正常 CA/SAN（建议而非强制），无隐式回退。只读 `--validate-auth-config` 与正式启动共同验证这两个键；`start.sh` 在停止旧实例前以目标 image 的同组配置预检，不刷新 cache、不创建目录。此处沿用实际 CLI 名称，不增加 `--validate-admin-oidc-config` 别名。

`true` 为全部 14 个既有 typed 方法使用专用 handler：每次发送从当前 `HttpContext.RequestServices` 重新认证并重读服务器 ticket，校验 issuer/sub/stamp、当前白名单、token 和严格截止时间，包括同请求框架认证已缓存后的换票/撤票。只向配置 origin 的既有 method/path 发送当前 server Bearer；禁止 Cookie、CSRF、入站 Authorization、AppSecret、ID/refresh token 或密码转发。无当前请求则零发送。传输禁用 redirect、Cookie 容器、客户端 logger 和出站 telemetry，不向 S3 或浏览器附加 Bearer。

下游 401/403/3xx、协议/JSON 错误、TLS/网络/超时和未知写结果统一安全 502；合法业务失败为安全 400/409，真实详情 GET 404 与合法空列表保留。调用者取消直接传播，不转成普通 500；每个 unsafe 方法最多应用发送一次，无 refresh、retry、replay 或匿名回落。失败/取消后不继续 Student 或 S3 写，但不能证明已发送写未提交。LegacyClean 仍使用 `admin-legacy-cleanup`，严格 reviewer 不匹配会失败，不能因此改 reviewer 或放宽 SourceIntake/managed immutable/pins 保护。

启用需先验证官方 Hosted Code/PKCE、真实 Mistake 的精确 issuer 与 ADMIN audience、真实 Student 和正常签名取图；本机替身不代替联合验收。停用此出站能力须恢复匹配配置，入站模式不变；整版回滚只能用已明确可用的完整镜像，不恢复丢失票据或撤销已提交写。

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

ServiceMantle 健康端点（随 #34 加入，#54 起具备真实就绪证据源；匿名可达，不受 SPA 回退影响）：

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

## StorageReferences v1 报告启用

专属密钥、三个引用提供服务的 HTTPS 配置，以及迁移和回退检查见 [StorageAudit](../modules/OssAudit/StorageAudit.md)。能力默认关闭，仅在由执行者负责的隔离环境中显式启用；先升级引用提供服务，再部署新版 Admin。启动时不再删除旧 Homework reviews 图片或旧桶记录；保留历史，所有 `resolve` 均拒绝删除。停用新能力必须保留数据，不能通过回滚到旧版可删除对象的 Admin 来执行清理。

## 数据库备份与恢复

```bash
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_admin | gzip > backup_admin.sql.gz
gunzip -c backup_admin.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_admin
```

### Prepared logout（#46，#83 起由 SignaCore 包承担）

退出始终使用 prepared logout，端点为包拥有的 `POST /api/auth/oidc/logout`。必需的 `AdminOidc:PostLogoutRedirectUri` 与 RedirectUri 同 origin，路径固定 `/api/auth/oidc/logout/return`、无 query/fragment/userinfo；SignaCore 应用注册的 PostLogout 回调需由维护者改为该值。http 与 https 同等接受（结构规则不变），公网部署应使用 HTTPS（建议而非强制）；非 POST logout 固定405，每次 POST 尝试都清理旧 `adminAuthToken` Cookie。

SPA 先 GET `/api/auth/oidc/csrf`（公开签发 `{"token":"..."}`，轮换共享 `adminCsrf` Cookie），再以导航式 form POST 提交 `__RequestVerificationToken`（或单值 `X-CSRF-TOKEN` header）。缺/错 token 固定 `400 {"outcome":"csrf_rejected"}` 且不触碰会话；登出不以 access token 期限或管理员白名单阻止本人退出。包在按会话键串行的门内先移除票据、删除会话 Cookie，再向 Authority `/oauth2/logout/requests` 发 HTTP Basic 认证 + 服务器 id_token_hint + PostLogout URI + 一次性 state，不重试不跟随 redirect；成功 302 验证过的同源 logout_uri，失败/超时/取消固定 `200 {"outcome":"local_only"}`。回跳 `/api/auth/oidc/logout/return?state=...` 以绑定 Cookie（5 分钟）原子单用，正确 302 `/login`，其余固定 400 HTML。


成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。


### 令牌过期与显式重认证（#45）

在唯一会话模式，有效管理员票据且严格 `expires_at` 到期时，业务门禁为 `401 {"error":"reauthentication_required"}`，零下游/OSS 删除，不清服务器 ID token、不刷新或重放写请求。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false，仅合法 token 到期 false/true（保留显示名），匿名/撤票/8h失效/缺token/坏期限 false/false、显示名 null。当前非管理员403优先；任意 Authorization401。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果，BFF不自动挑战。fake测试验证两种回跳时序与deadline前/精确/后/8h、并发，不能证明生产SignaCore时限；真实联调仍归 #41/IKJ8MO。

### 门户服务端凭据（#44）

Teacher/Assistant 全代理前缀始终在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询共用相同管理员边界，只转发服务器 token，失败门禁在 Student/门户调用前拒绝。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产 rollout 不在 #75 实现范围；当前组合的正式验证使用官方 Provider 与真实下游。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。AppSecret 剥离始终保持，三个代理共用相同会话边界。生产平台注册不由本项执行；正式组合门禁归 #75。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。

Identity 代理退役浏览器凭据兼容（#73）：middleware 的 settings 为必需依赖，直接调用缺 TrustedSessionKey/有效服务器 token 也401拒绝。所有路径使用 StartsWithSegments remaining 映射 `/api`，根/尾斜线/大小写/query/body 保持；始终 RequestAborted，预取消/发送中取消传播且无应用层重试。非代理的 `IdentityAccountsController` 不在本次修改范围。中间镜像不部署，最终须包含 #41/#75；回滚只选择明确旧完整镜像，不复活票据。

门户传输退役浏览器凭据兼容（#74）：两 middleware 只接受 TrustedSessionKey 的有效 server token，settings 为必需依赖，直接调用缺可信项也401。统一 remaining 的 admin/auth/其他三类映射，始终 RequestAborted、剥离浏览器Authorization/Cookie/Host/CSRF/gateway，隔离Set-Cookie；取消传播且无应用层重试。关联查询只随API-session，即使门户proxy开关false也用server token；当前账户筛选、单侧失败保另一侧、无关联短路与Student失败响应保持。此中间镜像不部署，最终须组合#41/#75；整版回滚不恢复本版浏览器凭据路径或复活撤票。

## 内部 HTTP 关联值（#85）

全部直接 ServiceMantle 包统一官方 `0.3.1-rc.1`。既有 Student、Mistake、Homework、IdentityService、TeacherPortal、AssistantPortal、StorageReferences 和固定内部 Authority 的 prepared logout 显式挂载共享 `AddServiceMantleCorrelationIdPropagation`；外部 S3、Consul 及独立 OIDC discovery/token Backchannel 不挂载，无全局策略。目的地与 redirect 信任仍由调用方承担，既有基址、timeout、retry、日志禁用和敏感 span 过滤保持。

三代理不把原始入站 `x-correlation-id` 当成明确出站值复制，由共享 handler 读取 middleware 私有已解析 slot；正常接受值保持相同，拒绝的输入对应同一生成值。明确由出站调用指定的 header 保持单值；Mistake 会话 handler 仅保留该非凭据 metadata，其余 header 继续清空并注入服务器票据 token，propagation 位于会话 handler 之后。Authorization/Cookie/CSRF/Host/AppSecret 的原剥离及下游 Set-Cookie 隔离不变。池化每 send 读当前 context，后台没有 context 不造 ID；Mistake 原无会话请求仍零下游拒绝。关联值不参与认证或幂等。

`InstanceId.CreateRandom` 保持每 Host build 随机 `ruoyu-admin-<Guid:N>`、同 Host 稳定，不写 bootstrap 或其他状态。无需新配置/数据迁移；回滚部署旧代码及配套同版包，不运行数据库 Down。
