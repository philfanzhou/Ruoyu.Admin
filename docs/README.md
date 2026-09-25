# Ruoyu.Admin 文档

Ruoyu.Admin 是 Ruoyu.Study 平台的管理后台服务，负责学生管理、上传记录审核、错题管理与 OSS 存储审计能力。

## 入口

| 入口 | 用途 |
|------|------|
| [overview/](./overview/README.md) | 服务级总览 |
| [modules/](./modules/README.md) | 内部业务功能 |
| [Integration/](./Integration/README.md) | 外部系统交互 |
| [database/](./database/README.md) | 数据结构与数据主责 |
| [development/](./development/README.md) | 开发执行支持 |
| [pending-decisions.md](./pending-decisions.md) | 本仓库唯一的未决设计入口 |
| [api.md](./api.md) | 补充接口文档 |
| [development/Deployment.md](./development/Deployment.md) | 部署说明 |
| [../CONTEXT.md](../CONTEXT.md) | Storage Audit 上下文术语 |

## 仓库布局

| 路径 | 内容 |
|------|------|
| `backend/Admin.WebApi` | 宿主、Controller、代理中间件、审计持久化、`OssAuditWorker` |
| `backend/Ruoyu.Admin.Common` | `IOssService`（S3 + 本地文件）、缩略图、JWT Bearer 认证、数据库初始化、共享常量 |
| `backend/Ruoyu.Admin.Consul` | Consul KV 配置源与本地缓存回退、Serilog/Loki 引导、PostgreSQL 连接串工厂 |
| `backend/Ruoyu.Admin.ServiceClients` | Student / Mistake 手写 HTTP 客户端与镜像 DTO |
| `backend/Tests` | xUnit + Moq + FluentAssertions 单元测试 |
| `frontend/` | Vue 3 + Element Plus 管理端 SPA |
| `scripts/` | Docker 镜像构建脚本 |
| `start.sh` | 集成镜像启动脚本 |

## 外部契约归属

Student、Mistake、Homework、Teacher Portal、Assistant Portal 与 Identity 的接口定义**不由本仓库主责**，均在 Ruoyu.Study 与 [SignaCore](https://github.com/philfanzhou/SignaCore) 仓库内。`backend/Ruoyu.Admin.ServiceClients` 中的 DTO 是这些 HTTP 契约的手写镜像副本：上游字段变更不会在本仓库产生编译错误，只会产生运行时反序列化偏差，因此上游变更必须同步修改 ServiceClients 并补充 Controller 单测。

## 已知文档债（自 monorepo 继承）

以下问题在迁出前就已存在，**尚未修正**，阅读时需要以代码为准：

- `docs/overview/Integration.md` 的集成矩阵仍按 gRPC 描述，且带有「Phase 4 计划变更（尚未实施）」标注。实际实现已全部改为 HTTP/JSON，gRPC 已移除。矩阵中的方法名与端口需按 `backend/Admin.WebApi/Program.cs`、`Controllers/` 和三个代理中间件重新核对。
- `docs/modules/` 下约 40 个文件仍带有 gRPC 时代的表述（`StudentManagementGrpcServiceClient`、`Ruoyu.Study.Student.Contract.Protos`、`MistakeGrpcService` 等）。这些是历史设计稿，描述的是已下线的协议。
- `docs/modules/*/04-TASKS.md` 是历史实施任务清单，不代表当前状态，不作为运行手册使用。
- 原 monorepo 的仓库级 ADR（如 ADR-0003 OSS 内外网路由分离）留在 Ruoyu.Study 仓库，本仓库文档以绝对链接引用。

`docs/overview/Design.md` 的依赖关系图、`docs/api.md` 与本文档已在迁出时更新为当前事实。

## 已修复的继承缺陷

### Storage Audit 曾把 QuestionBank 的题目图片全部误判为孤儿（迁出时修复）

`OssAuditWorker` 原先固定扫描 `uploads/`、`mistakes/`、`questions/` 三个桶，但引用聚合只来自 Student、Homework、Mistake。`questions/` 归 QuestionBank 所有，没有任何来源覆盖它，因此该桶下每个对象都被写入 `OssAuditRecords`。删除前复核查询的是同一批来源，对 `questions/` 必然回答"不是我引用的"，因而拦不住——resolve / batch-resolve 会经 `IOssService.DeleteAsync` 真实删除题目图片并连带删除缩略图。

修复内容：

- 审计范围收敛为 `OssAuditWorker.AuditedBuckets`（`Uploads`、`Mistakes`），并确立不变量「一个桶只有在存在可达引用来源时才允许被扫描」。
- 新增启动清理 `CleanupUnauditedBucketAuditRecordsAsync`，移除历史遗留的 `questions` / `documents` 误判记录；桶名为空或无法识别的记录保留。
- `Mistake` 由「降级继续」改为与 Student / Homework 一致的「中止整轮审计」。这一条**不是**数据丢失修复：删除前复核会在 Mistake 不可达时返回 502 拒删，恢复后也能识别真实引用。改它是因为降级会让整轮审计报告成功并给出失真的 `NewZombieCount`，把待处置队列灌满噪声。
- 测试见 `backend/Tests/Services/OssAuditWorkerScopeTests.cs`；其中 `RunAudit_AbortsWhenMistakeServiceIsUnavailable_*` 已对修复前代码做过 A/B 验证（修复前 `Status` 为 1，测试失败）。

权威描述见 [modules/OssAudit/OssAudit/06-CONVENTIONS.md](./modules/OssAudit/OssAudit/06-CONVENTIONS.md) 的「桶范围」与「删除前复核」。

**运维注意**：修复只在应用启动时清理误判记录。若线上库中已有 `Bucket = 'questions'` 的记录，升级后首次启动会自动移除；升级前不要对这些记录执行 resolve 或 batch-resolve。

### `OssAuditController` 曾完全没有测试覆盖（迁出时补齐）

`ResolveRecord` / `BatchResolve` 的删除前复核是唯一阻止误删的运行时保护，迁出时 `backend/Tests/Controllers/` 下没有 `OssAuditControllerTests`。原 monorepo 文档把 22 个此类用例标注为「已实现」，实际在源仓库中也不存在——属虚构清单。

现已按实际代码补齐 `OssAuditControllerTests`，31 个用例覆盖复核的每一条拒删分支、大小写不敏感比对、`_homeworkClient` 为 null 的收窄语义、删除失败留档、批量处置的整批拒绝与逐条跳过，以及查询与触发端点。两个 502 守卫用例已做 A/B 验证（临时移除守卫后确实失败）。补测试过程中还发现并修复了 `BatchResolve` 空结果早退路径缺 `totalRequested` 字段、与 `docs/api.md` 和前端 `BatchResolveResponse` 声明不一致的问题。

清单见 [modules/OssAudit/OssAudit/05-TESTS.md](./modules/OssAudit/OssAudit/05-TESTS.md)。

**仍未覆盖**：`OssAuditWorker.ExecuteAsync` 的调度计算与并发互斥分支。

## 待补的继承缺陷

### `Steeltoe.Discovery.Consul` 4.2.0 高危漏洞（GHSA-67c9-f6v2-qv86）

继承自源仓库的 Consul 服务发现/配置组件存在已知高危公告：畸形 `secure` 元数据会导致服务实例查找中止（DoS）。受影响范围 `>= 4.0.0, <= 4.2.0`，修复版本 `4.3.0`；`dotnet restore` 以 NU1903 警告呈现。

`ci.yml` 中的 trivy 镜像扫描步骤（HIGH/CRITICAL + `ignore-unfixed`）当前为 **report-only（`exit-code: '0'`）**：该漏洞已有修复版本，`ignore-unfixed` 不会跳过它，若设为阻塞，required check `Build & Test` 会让每个 PR 从当天起直接红。**升级条件**：以独立变更把 `Ruoyu.Admin.Consul` 的 Steeltoe.Discovery.Consul 升到 `>= 4.3.0`（涉及 Consul KV 加载与服务注册行为，需按验证纪律完整回归），复核通过后把该步骤改为 `exit-code: '1'`（对齐 SignaCore 的阻塞策略）。首扫时另一镜像（已废弃的独立前端镜像，其 nginx:1.30.0-alpine 基础层贡献了 39 个 HIGH）已随 2026-09-25 的分离模式退役一并消失；当前扫描对象只剩集成镜像，已知发现仅 Steeltoe 这一条。

### `AdminApi:Port` 是死配置

`appsettings.json` 里的 `AdminApi:Port` 没有任何代码读取。监听端口由 `Program.cs` 中的 `const int httpPort = 5020` 硬编码。删除该配置键或让端口真正可配，都需要单独决定。

### `Ruoyu.Admin.ServiceClients` 含未使用的下游方法

该库在 monorepo 中由 Mistake 服务与多个 Portal 共用，本仓库只消费其中一部分：Student 25 个方法用到 20 个，Mistake 14 个方法用到 7 个。`CreateReturnedRecordRequest` 等 mistake→student 调用的 DTO 块在本仓库完全未被引用。未裁剪，属于开源仓库的死代码清理项。

### `Ruoyu.Admin.Common.Constants.OssPathPrefixConstants` 在本仓库未被引用

它是 Student 图片指纹懒校验用的跨前缀清单，随共享库整体复制过来，本仓库没有任何代码读取它。

它的 `All` 只列 `uploads/`、`mistakes/`、`homework/`，与 `OssBucket` 的四个值（`Uploads`、`Mistakes`、`Questions`、`Documents`）并不对应。**不要**把它当作审计范围的依据——审计范围由 `OssAuditWorker.AuditedBuckets` 定义。

另外 `homework/` 这个前缀本身具有误导性：Homework 服务没有 OSS 集成，作业图片实际由 Student 存放在 `uploads/homework/` 之下（`OssAuditWorker.CleanupLegacyHomeworkReviewImagesAsync` 处理的正是这个路径），源码注释也标明该常量是「reserved for future homework-service image upload」。判断某类对象归谁所有时，应以实际写入方为准，不要以这个常量为准。

处理选项：删除该文件，或补注释说明其真实归属与 `homework/` 的保留状态。
