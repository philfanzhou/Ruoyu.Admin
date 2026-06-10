# Admin Portal 文档地图

> 本目录是 Admin Portal 项目的唯一文档入口。所有文档按层级组织，支持逐级下钻阅读。

## 阅读建议

- **第一次接触**：先读 [SystemContext](./SystemContext.md) 了解服务定位，再读 [Design](./Design.md) 了解架构
- **查接口**：直接看 [Integration](./Integration.md) 集成矩阵，或进入 [modules/](./modules/) 找对应功能点
- **查数据库**：进入 [database/](./database/README.md) 查表结构和关系
- **查流程**：看 [KeyFlows](./KeyFlows.md) 的关键时序图
- **排障**：看 [KeyFlows](./KeyFlows.md) + 对应模块的 05-TESTS.md
- **搭建环境/运行/测试**：进入 [development/](./development/README.md)
- **复核遗留问题**：看 [AgentReviewNotes](./AgentReviewNotes.md)

---

## 顶层总览

| 文档 | 用途 |
|------|------|
| [SystemContext.md](./SystemContext.md) | 服务定位、上下游调用关系、服务边界 |
| [Design.md](./Design.md) | 服务级架构、分层说明、技术栈、依赖关系 |
| [Requirements.md](./Requirements.md) | 服务级需求摘要（精简，详细需求见 modules/） |
| [KeyFlows.md](./KeyFlows.md) | 关键跨服务时序与调用链（ASCII 图） |
| [Integration.md](./Integration.md) | 集成矩阵、接口边界、失败语义 |
| [DataOwnership.md](./DataOwnership.md) | 数据主责、引用边界、双写禁区 |

## 统一事实源

| 文档 | 用途 |
|------|------|
| [database/README.md](./database/README.md) | 表清单、实体关系、迁移历史（数据库唯一事实源） |
| [database/relations.md](./database/relations.md) | 实体关系图 |
| [database/migrations.md](./database/migrations.md) | 迁移历史 |
| [database/tables/](./database/tables/) | 每表一个文件：字段、类型、约束、索引 |

## 内部业务能力

| 业务域 | 功能点数 | 入口 |
|--------|---------|------|
| StudentManagement（学生管理） | 3 | [modules/StudentManagement/](./modules/StudentManagement/) |
| UploadRecordManagement（上传记录管理） | 2 | [modules/UploadRecordManagement/](./modules/UploadRecordManagement/) |
| MistakeManagement（错题管理） | 1 | [modules/MistakeManagement/](./modules/MistakeManagement/) |
| OssAudit（OSS 审计） | 1 | [modules/OssAudit/](./modules/OssAudit/) |

详细索引见 [modules/README.md](./modules/README.md)

## 外部系统交互

| 外部系统 | 功能点数 | 入口 |
|----------|---------|------|
| IdentityService（身份服务） | 1 | [Integration/IdentityService/](./Integration/IdentityService/) |
| TeacherPortal（教师门户） | 1 | [Integration/TeacherPortal/](./Integration/TeacherPortal/) |

详细索引见 [Integration/README.md](./Integration/README.md)

## 开发执行支持

| 文档 | 用途 |
|------|------|
| [development/README.md](./development/README.md) | 开发执行支持入口 |
| [development/LocalSetup.md](./development/LocalSetup.md) | 本地环境搭建、配置 |
| [development/RunAndDebug.md](./development/RunAndDebug.md) | 启动、调试、热重载 |
| [development/Verification.md](./development/Verification.md) | 测试运行、覆盖率、构建验证 |

## 代码规范与已有文档

| 文档 | 用途 |
|------|------|
| [DotNetCodingPolicy.md](./DotNetCodingPolicy.md) | .NET 编码规范 |
| [api.md](./api.md) | REST API 接口文档（已有，与 modules/ 互补） |
| [deployment.md](./deployment.md) | 部署文档（已有） |

## 审阅记录

| 文档 | 用途 |
|------|------|
| [AgentReviewNotes.md](./AgentReviewNotes.md) | AI Agent 审阅记录与遗留问题 |
