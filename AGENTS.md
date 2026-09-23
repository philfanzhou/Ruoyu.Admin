# Ruoyu.Admin 协作规范

Ruoyu.Admin 是 [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) 平台的管理后台：.NET 8 BFF API、Vue 3 / Element Plus 管理端、单容器部署。它几乎不持有业务数据，只主责 Storage Audit（存储审计）一个领域。

## 维护方式

- 本文件是 AI 协作流程、项目边界和验证约束的统一入口。
- Codex 直接读取本文件；Claude Code 通过根目录 `CLAUDE.md` 导入本文件。
- `.agent`、`.claude`、`.trae` 等工具目录可以补充工具专属规则，但不得覆盖本文件的边界、测试和安全约束。
- 本仓库没有 CI。所有验证必须在本地显式运行，未运行或失败的验证必须如实说明，不得假定通过。

## 文档与沟通语言

- 流程与约束文档、GitHub issue/PR 正文和 review 使用中文；Issue 标题使用中文。
- PR 标题和 commit message 使用英文 conventional commit 格式（`feat:` / `fix:` / `docs:` / `test:` / `refactor:` / `chore:` 等）。
- 面向使用者的根 `README.md` 保持英文。
- `docs/` 与 `CONTEXT.md` 是从 Ruoyu.Study monorepo 继承的中文文档，保持中文；翻译为英文是独立后续项，不与功能改动混在同一 PR。
- 引用代码、命令、路径、JSON 字段和配置键时保持原样。

## 项目边界与架构

- `backend/Ruoyu.Admin.Common`、`backend/Ruoyu.Admin.Consul`、`backend/Ruoyu.Admin.ServiceClients` 是类库，不得反向引用 `backend/Admin.WebApi`。
- `backend/Admin.WebApi` 负责 HTTP、认证、配置、代理中间件和宿主组合。Controller 不得直接构造 `HttpClient`，一律通过 `Ruoyu.Admin.ServiceClients` 的接口或 `IHttpClientFactory` 命名客户端。
- API 监听端口 **5020 是硬编码的**（`Program.cs` 中的 `const int httpPort`），不是配置项。改端口属于部署契约变更，必须同步 `start.sh`、`frontend/nginx.conf`、`frontend/vite.config.js` 和部署文档。
- 数据库只有 `ruoyu_admin`，只有 `OssAuditRuns` 和 `OssAuditRecords` 两张表，建表由 `Program.cs` 中的 `DatabaseInitializer` 回调负责。本仓库不使用 EF Core Migrations——`GENERATED ALWAYS AS IDENTITY` 不被 `EnsureCreated` 支持，改动建表语句时必须保持 `CREATE TABLE IF NOT EXISTS` 幂等语义。
- Storage Audit 的处置动作会**真实删除生产对象存储中的文件**（`IOssService.DeleteAsync` 还会连带删除缩略图）。任何触及审计判定逻辑、路径聚合或删除路径的改动都必须有测试，且不得放宽「对象被任何业务记录引用即视为在用」的判定。
- `/` 与所有非 `/api` 路由必须允许匿名访问，否则 SPA 登录页无法加载（未登录 → 拿不到页面 → 无法登录的死锁）。静态文件与 SPA 回退必须注册在 `UseAuthentication()` 之前。

## 外部契约归属

Student、Mistake、Homework、Teacher Portal、Assistant Portal 的接口由 [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) 主责，Identity 由 [SignaCore](https://github.com/philfanzhou/SignaCore) 主责。本仓库无编译期依赖。

- `backend/Ruoyu.Admin.ServiceClients` 中的 DTO 是下游 HTTP 契约的**手写镜像副本**。上游字段变更不会在本仓库产生编译错误，只会产生运行时反序列化偏差。
- 因此下游契约变更必须同步修改 ServiceClients，并补充对应 Controller 单测。
- 不得为了让编译通过而在 DTO 上加宽容的可选字段来掩盖上游契约漂移；应确认上游事实后显式对齐。
- 三个代理中间件（Identity / Teacher Portal / Assistant Portal）透传调用方凭据。`IdentityProxyMiddleware` 会剥离入站的 `X-Admin-AppSecret` 再注入本服务自己的值——不得移除这个剥离逻辑，否则调用方可以伪造网关凭据。

## 安全与变更纪律

- 不得提交 token、密码、连接字符串、Identity `AppSecret`、数据库文件、个人数据或真实凭据，也不得把它们写入日志、截图或测试输出。
- `StartupDiagnosticsFormatter` 的脱敏方法（`MaskSecret` / `SummarizePassword`）是启动日志的唯一出口。新增启动诊断日志必须走它，不得直接打印配置值。
- `appsettings.json` 中的 OSS 凭据是本地开发用的 mock 值；生产凭据只来自 Consul KV 或环境变量。不得把真实凭据写进任何 `appsettings*.json`。
- 认证、授权、审计删除、代理和 CORS 的行为变化必须有针对性测试和文档说明。
- 提交前检查文档链接、secret 和仓库状态。

## 已知文档债

`docs/README.md` 的「已知文档债」小节记录了从 monorepo 继承、尚未修正的过期描述（主要是 gRPC 时代的集成矩阵与设计稿）。**代码、测试、配置和脚本才是当前实现事实**；文档与代码冲突时先判断是实现未完成、文档过期还是需求变化，不得直接假定任一方正确，也不得基于过期文档做设计决策。

## 验证

按改动风险运行最小充分验证：

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
```

触及 Dockerfile、`start.sh` 或 `nginx.conf` 时，还需实际构建镜像：

```bash
./scripts/build.sh
./scripts/build-web.sh
```

跨服务端到端用例（Admin 建学生 → User Portal 可见）保留在 Ruoyu.Study 仓库的 `tests/e2e/` 中，面向已部署环境运行，不在本仓库内。
