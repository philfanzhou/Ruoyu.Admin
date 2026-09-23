# Ruoyu.Admin 文档

Ruoyu.Admin 是 [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) 平台的管理后台服务，负责学生管理、上传记录审核、错题管理与 OSS 存储审计能力。

## 入口

| 入口 | 用途 |
|------|------|
| [overview/](./overview/README.md) | 服务级总览 |
| [modules/](./modules/README.md) | 内部业务功能 |
| [Integration/](./Integration/README.md) | 外部系统交互 |
| [database/](./database/README.md) | 数据结构与数据主责 |
| [development/](./development/README.md) | 开发执行支持 |
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

Student、Mistake、Homework、Teacher Portal、Assistant Portal 与 Identity 的接口定义**不由本仓库主责**，均在 [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) 与 [SignaCore](https://github.com/philfanzhou/SignaCore) 仓库内。`backend/Ruoyu.Admin.ServiceClients` 中的 DTO 是这些 HTTP 契约的手写镜像副本：上游字段变更不会在本仓库产生编译错误，只会产生运行时反序列化偏差，因此上游变更必须同步修改 ServiceClients 并补充 Controller 单测。

## 已知文档债（自 monorepo 继承）

以下问题在迁出前就已存在，**尚未修正**，阅读时需要以代码为准：

- `docs/overview/Integration.md` 的集成矩阵仍按 gRPC 描述，且带有「Phase 4 计划变更（尚未实施）」标注。实际实现已全部改为 HTTP/JSON，gRPC 已移除。矩阵中的方法名与端口需按 `backend/Admin.WebApi/Program.cs`、`Controllers/` 和三个代理中间件重新核对。
- `docs/modules/` 下约 40 个文件仍带有 gRPC 时代的表述（`StudentManagementGrpcServiceClient`、`Ruoyu.Study.Student.Contract.Protos`、`MistakeGrpcService` 等）。这些是历史设计稿，描述的是已下线的协议。
- `docs/modules/*/04-TASKS.md` 是历史实施任务清单，不代表当前状态，不作为运行手册使用。
- 原 monorepo 的仓库级 ADR（如 ADR-0003 OSS 内外网路由分离）留在 Ruoyu.Study 仓库，本仓库文档以绝对链接引用。

`docs/overview/Design.md` 的依赖关系图、`docs/api.md` 与本文档已在迁出时更新为当前事实。

## 已知缺陷（自 monorepo 继承，尚未修复）

### Storage Audit 会把 QuestionBank 的题目图片全部误判为孤儿对象

`OssAuditWorker` 扫描 `uploads/`、`mistakes/`、`questions/` 三个前缀（`backend/Admin.WebApi/Services/OssAuditWorker.cs`），但引用聚合只来自 Student、Homework 和 Mistake 三个下游，**没有任何来源聚合 QuestionBank 的题目图片路径**。

后果：`questions/` 下的每一个对象都会被判为无引用并写入 `OssAuditRecords`。管理员在 UI 上执行 resolve 或 batch-resolve 时，`IOssService.DeleteAsync` 会真实删除这些文件，连带删除缩略图，**直接破坏 QuestionBank 的题目图片**。

同一处不一致还体现在 `OssPathPrefixConstants` 中：它只列出 `uploads/`、`mistakes/`、`homework/`，完全不知道 `questions/` 的存在，尽管 `OssBucket.Questions` 已经在审计范围内。

**在修复之前，不得对 `Bucket == "questions"` 的审计记录执行 resolve 或 batch-resolve。**

可选修复方向（均属于行为变更，需要独立评审，不要在无关 PR 里顺手改）：

1. 让 QuestionBank 暴露一个引用路径查询端点，`OssAuditWorker` 增加一个聚合来源，失败时按 Student/Homework 的语义**中止整轮审计**（而不是像 Mistake 那样降级继续），因为降级会产生误删。
2. 或者把 `OssBucket.Questions` 移出审计范围，直到 1 落地。
3. 无论选哪个，都要把 `questions/` 补进 `OssPathPrefixConstants.All`，并同步 `docs/database/` 与 Storage Audit 的能力文档。

### `AdminApi:Port` 是死配置

`appsettings.json` 里的 `AdminApi:Port` 没有任何代码读取。监听端口由 `Program.cs` 中的 `const int httpPort = 5020` 硬编码。删除该配置键或让端口真正可配，都需要单独决定。
