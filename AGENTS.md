# Ruoyu.Admin 协作规范

Ruoyu.Admin 是 Ruoyu.Study 平台的管理后台：.NET BFF API、Vue 3 / Element Plus 管理端、单容器部署。它几乎不持有业务数据，只主责 Storage Audit（存储审计）一个领域。

## 维护方式

- 本文件是 AI 协作流程、项目边界和验证约束的统一入口。
- Codex 直接读取本文件；Claude Code 通过根目录 `CLAUDE.md` 导入本文件。
- `.agent`、`.claude`、`.trae` 等工具目录可以补充工具专属规则，但不得覆盖本文件的边界、测试和安全约束。
- CI 位于 `.github/workflows/`：`ci.yml`（Build & Test：后端编译+测试、前端构建+组件测试、集成镜像构建验证与 trivy 扫描；tag 推送时发布 GHCR 镜像与 GitHub Release）与 `codeql.yml`（Analyze csharp / javascript-typescript）。`main` 由 `Protect main` ruleset 保护：禁止直推、force-push 和删除，PR 必须通过上述三个 required check 才能合并。
- 发布契约：tag 带 `v` 前缀（`vX.Y.Z-rc.N` / `vX.Y.Z`）。`vX.Y.Z-rc.N` 只发布不可变版本标签的**测试镜像**并创建 pre-release；`vX.Y.Z` 发布**正式镜像**并同时移动 `X.Y` 与 `latest`。镜像 tag 不带 `v` 前缀（保持 `X.Y.Z` 历史形态，CI 的 semver 元数据步骤负责剥离），GitHub Release 标题即 tag 本身（无库名前缀）。镜像为 `ghcr.io/philfanzhou/ruoyu.admin`——API+SPA 单容器集成是产品唯一发布形态（monorepo 继承的独立前端镜像从未被任何部署使用，已移除）。

## 文档与沟通语言

- 流程与约束文档、GitHub issue/PR 正文和 review 使用中文；Issue 标题使用中文。
- PR 标题和 commit message 使用英文 conventional commit 格式（`feat:` / `fix:` / `docs:` / `test:` / `refactor:` / `chore:` 等）。
- 面向使用者的根 `README.md` 保持英文。
- `docs/` 与 `CONTEXT.md` 是从 Ruoyu.Study monorepo 继承的中文文档，保持中文；翻译为英文是独立后续项。
- 引用代码、命令、路径、JSON 字段和配置键时保持原样。

## 项目边界与架构

- `backend/Ruoyu.Admin.Common`、`backend/Ruoyu.Admin.Consul`、`backend/Ruoyu.Admin.ServiceClients` 是类库，不得反向引用 `backend/Admin.WebApi`。
- `backend/Admin.WebApi` 负责 HTTP、认证、配置、代理中间件和宿主组合。Controller 不得直接构造 `HttpClient`，一律通过 `Ruoyu.Admin.ServiceClients` 的接口或 `IHttpClientFactory` 命名客户端。
- 认证只使用 AdminSession +托管 OIDC；五旧键只检验缺省/规范 true，false/畸形在启动前拒绝，无入站 Bearer/JWT Cookie scheme。`--validate-auth-config` 加载正式相同配置后固定安全退出，Consul cache只读；`start.sh` 在 stop/rm 前使用目标集成镜像预检。
- API 监听端口 **5020 是硬编码的**（`Program.cs` 中的 `const int httpPort`），不是配置项。改端口属于部署契约变更，必须同步 `start.sh`、`frontend/vite.config.js` 和部署文档。
- 数据库只有 `ruoyu_admin`，只有 `OssAuditRuns` 和 `OssAuditRecords` 两张表，结构由确切 EF Core 迁移链（`Persistence/Migrations`）管理，启动时由 ServiceMantle 共享 `StartupDatabaseGate`（配置边界和部署声明位于 `Admin.WebApi/Database`）调用 `AuditMigrationExecutor` 及共享迁移编排（PostgreSQL advisory lock 多实例串行化；旧库接管、结构拒绝规则见 `docs/database/migrations.md`）。新增或修改迁移必须同步 `AuditMigrationExecutor.KnownMigrationIds` 契约；「结构未知即拒绝（`RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`）」「缺库默认拒绝创建（`Database:AllowCreate` 默认 false）」语义不得放宽。
- Storage Audit v1使用三个完整持久引用快照，只产生Status3只读观察。`deletionAuthorized=false`恒定；所有single/batch resolve状态都经shared collector后拒绝（完整409、不可用502），不执行Delete/Copy/审计record删除。触及聚合、判定或任何删除路径必须有测试。
- **审计不变量：三个provider完整验证后才允许OSS列举。** 任一不可达、不全、坏合同或取消必须整轮失败，零S3列举/删除/复制与新观察。无旧gRPC/offset/空集fallback；不把capture事实当未来删除许可。
- 扫描只含`Uploads`、`Mistakes`。Questions/Documents没有完整引用来源不纳入。startup旧正则图片清理与旧桶record清理停止；历史pins与审计记录保持，不能因旧Status绕过共同门禁。唯一合同与消费者职责见`docs/modules/OssAudit/StorageAudit.md`。
- `/` 与所有非 `/api` 路由必须允许匿名访问，否则 SPA 登录页无法加载（未登录 → 拿不到页面 → 无法登录的死锁）。静态文件与 SPA 回退必须注册在 `UseAuthentication()` 之前。

## 外部契约归属

Student、Mistake、Homework、Teacher Portal、Assistant Portal 的接口由 Ruoyu.Study 主责，Identity 由 [SignaCore](https://github.com/philfanzhou/SignaCore) 主责。本仓库无编译期依赖。

- `backend/Ruoyu.Admin.ServiceClients` 中的 DTO 是下游 HTTP 契约的**手写镜像副本**。上游字段变更不会在本仓库产生编译错误，只会产生运行时反序列化偏差。
- 因此下游契约变更必须同步修改 ServiceClients，并补充对应 Controller 单测。
- 不得为了让编译通过而在 DTO 上加宽容的可选字段来掩盖上游契约漂移；应确认上游事实后显式对齐。
- 三个代理中间件（Identity / Teacher Portal / Assistant Portal）只使用服务器票据内 token，拒绝入站 Authorization，剥离浏览器 Cookie/CSRF/Host 与下游 Set-Cookie。`IdentityProxyMiddleware` 会剥离入站的 `X-Admin-AppSecret` 再注入本服务自己的值——不得移除这个剥离逻辑，否则调用方可以伪造网关凭据。

## 安全与变更纪律

- 不得提交 token、密码、连接字符串、Identity `AppSecret`、数据库文件、个人数据或真实凭据，也不得把它们写入日志、截图或测试输出。
- `StartupDiagnosticsFormatter` 的脱敏方法（`MaskSecret` / `SummarizePassword`）是启动日志的唯一出口。新增启动诊断日志必须走它，不得直接打印配置值。
- `appsettings.json` 中的 OSS 凭据是本地开发用的 mock 值；生产凭据只来自 Consul KV 或环境变量。不得把真实凭据写进任何 `appsettings*.json`。
- 认证、授权、审计删除、代理和 CORS 的行为变化必须有针对性测试和文档说明。
- 提交前检查文档链接、secret 和仓库状态。

## 未决事项

- 仓库级尚未做出的决定只写入 `docs/pending-decisions.md`，不得创建分散的 `HumanReview.md` 或在能力文档里夹带待办。
- 待决事项只阻塞文件中声明的范围，不冻结无关任务。
- 决定形成后迁入 ADR、能力文档或实施 Issue，并从 pending 文件移除。
- 主责在 Ruoyu.Study 的跨仓库契约问题记录在该仓库，本仓库只保留 Admin 侧取舍。
- 完成记录、测试结果、普通缺陷和代码卫生建议不得写入 pending 文件；已知缺陷记录在 `docs/README.md`。

## 已知文档债

`docs/README.md` 的「已知文档债」小节记录了从 monorepo 继承、尚未修正的过期描述（主要是 gRPC 时代的集成矩阵与设计稿）。**代码、测试、配置和脚本才是当前实现事实**；文档与代码冲突时先判断是实现未完成、文档过期还是需求变化，不得直接假定任一方正确，也不得基于过期文档做设计决策。

## 验证

按改动风险运行最小充分验证（后端 `dotnet test` 包含 `backend/Tests/Integration/` 的 Testcontainers 集成测试，需要本机 Docker；无 Docker 时用 `--filter "FullyQualifiedName!~Admin.WebApi.Tests.Integration"` 运行其余套件）：

```bash
cd backend
dotnet restore Ruoyu.Admin.sln
dotnet build Ruoyu.Admin.sln --configuration Release
dotnet test Ruoyu.Admin.sln --configuration Release --no-build
```

前端：

```bash
cd frontend
npm ci
npm run build
npm test
```

触及 Dockerfile 或 `start.sh` 时，还需实际构建镜像：

```bash
./scripts/build.sh
```

跨服务端到端用例（Admin 建学生 → User Portal 可见）保留在 Ruoyu.Study 仓库的 `tests/e2e/` 中，面向已部署环境运行，不在本仓库内。
