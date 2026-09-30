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

`ServiceMantle.Logging 0.2.1-rc.1` 提供 Console + 可选 Loki，共享 Information 最低级别和两个 Warning override：`Microsoft.AspNetCore`、`Microsoft.EntityFrameworkCore.Database.Command`。`Logging:LogLevel` 是框架 ILogger 的前置过滤；它可以进一步抑制事件，不能突破上述管线下限。

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

本阶段仅接握手和状态，**前端、管理 API、代理仍用旧 JWT**。`AdminOidc:Enabled` 默认 false，不会以 Cookie 提前授予 Admin 权限；后续 #39/#40/#41 的上线契约尚未激活。

启用时复用 `IdentityService:Authority/AppId/AppSecret`，新增 `AdminOidc:RedirectUri`，精确注册 `https://admin.example.com/api/auth/oidc/callback`（占位示例）。URI 必须与配置 byte-for-byte 相同，路径固定、无 query/fragment/userinfo/wildcard；生产 HTTPS。只有 Development/Testing 允许 `http://127.0.0.1:<port>/api/auth/oidc/callback` 或 `http://[::1]:<port>/api/auth/oidc/callback`；不允许 localhost。Authority 去结尾 `/` 后与 Discovery issuer 严格相同；OIDC 不使用 JWT AdditionalValidIssuers 放宽信任。错误启用配置启动失败，消息只列配置键。

按 [SignaCore HostedLogin](https://github.com/philfanzhou/SignaCore/blob/52c68c812335dd6a967cb90042ac91c21543d387/docs/integrations/HostedLogin.md) 注册 Confidential、PerApplication audience、精确 Redirect URI 和 Code + openid profile + S256，不启用 refresh。Admin 从 Discovery 取授权/token/JWKS 端点，不发送 PAR、prompt 或其他不支持字段；兑换为 client_secret_post，固定外部 RedirectUri 与原 verifier，不信任入站 Host/X-Forwarded-Host 拼地址。TLS 可在可信代理终止，回跳仍固定 HTTPS，Cookie Secure；本服务不增加任意转发头信任。

反向代理必须避免 callback query（尤其 code）进入 access log/analytics，并保证原始单值 query 透传。Admin 协议 handler 禁用可能包含 token/URL/Cookie 的框架详细日志；启用 OIDC 时另外抑制会记录原始 query 的 `Microsoft.AspNetCore.Hosting.Diagnostics` 请求日志（包含其余路由的此类日志），OIDC 入口不导出 ASP.NET Core trace；应用调用方仍不得将敏感值插入自由文本。AppSecret 只通过环境变量/user-secrets/Consul 安全配置，不提交配置文件。

单实例内存 state 最长 5 分钟、43 字符随机引用、单次原子消费；票据 8 小时绝对到期、不滑动，所有 token 留服务器。两类存储各最多 4096 项、每分钟回收；满载固定失败，重启丢失，不能多副本共用会话。数据保护 keys 即使还在也不会恢复已丢失 ticket；无新表/迁移。上游 token 吊销、全局登出与会话续接留后续任务；不要将本地 session 状态作为管理员授权证明。

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

## 健康检查

ServiceMantle 健康端点（随 #34 落地，匿名可达，不受 SPA 回退影响）：

```bash
curl -s http://localhost:5020/health/live
# 200 {"status":"live"}
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5020/health/ready
# 503 —— 就绪源落地前的固定形态，不是故障
```

- **`/health/live` 是当前唯一应被部署与监控使用的探测端点**：进程存活即恒 200，用于容器存活探测与重启判定。响应携带 `x-correlation-id`。
- **`/health/ready` 与别名 `/health` 在就绪证据源（`IServiceHealthSnapshotSource`）落地前恒返回 503**，响应体 `{"status":"not_ready","phase":null,"migrationStatus":null,"databaseStatus":null,"errorCode":"health.probe_failed"}`——这是诚实的 fail-closed 语义（Admin 尚无就绪证据来源），**不得**把这两个端点接入重启或流量门禁，否则会误杀健康实例。真实依赖（DB/OSS）连通性纳入就绪判定属后续独立增强。
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
