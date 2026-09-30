# 本地非生产回归环境 (LocalRegressionEnv)

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
2. 数据库 `ruoyu_admin` 无需手工建：**种子脚本会在缺失时自动创建**；建表由 `Program.cs` 的 `DatabaseInitializer` 负责（`CREATE TABLE IF NOT EXISTS` 幂等语义，本仓库不使用 EF Migrations）。
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

首次启动由 `DatabaseInitializer` 建表（种子脚本要求表已存在）。验证：`curl http://localhost:5020/` 返回运行文案；`http://localhost:5020/swagger`（Development）可见。

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
- **种子失败「表不存在」**：先完成步骤 3 让 `DatabaseInitializer` 建表。
- **审计运行始终 `Status=2`**：替身未启动（步骤 5），或 `StudentService/MistakeService/HomeworkService:Url` 指向了错误端口。


## 可选 OIDC 基础回归（#38）

上述 #24 配方仍走旧密码/JWT。新握手默认关闭，显式启用与精确注册见 [LocalSetup.md](./LocalSetup.md#可选托管登录开发配置)。本地 callback 使用 `http://127.0.0.1:5020/api/auth/oidc/callback`，不要注册 localhost；生产 HTTPS/TLS 与代理 query 禁日志要求见 [Deployment.md](./Deployment.md#可选-signacore-托管登录)。Secret 仍只通过环境变量或 user-secrets 注入。

真 Program 的自动化握手由 `OidcTestAuthority` 隔离提供 Discovery/JWKS/token、测试 RSA 与虚构 canary，执行 `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"`（需要 Docker）。包含严格签名与回调拒绝、取消、一次消费/并发、Cookie 引用/内存 token、过期/重启、匿名 SPA/健康以及新 Cookie 不放行 Admin API；它不表示生产 ADMIN Code 注册和下游 audience（Ruoyu.Study IKJ8MO）已就绪。

本阶段尚未切前端与管理 API。session 最长 8 小时绝对、state 5 分钟，均为单进程有界内存，重启失效。后续会话授权/写请求 CSRF、续接/登出、SPA 切换分别由 #39/#40/#41 推进；旧 `POST /api/auth/logout` 只清旧 JWT Cookie，不负责新会话或全局登出。
