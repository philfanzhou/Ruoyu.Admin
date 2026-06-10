# Admin Portal 文档

Admin Portal 是 Ruoyu.Study 系统的管理后台服务，基于 ASP.NET Core 8.0，为管理员提供学生管理、上传记录审核、错题管理、OSS 僵尸文件审计等能力，同时代理请求到 Identity Service 和 Teacher Portal。

> 本目录是 Admin Portal 项目的唯一文档入口。`docs/` 根目录不散落正式总览正文——总览类文档统一在 `overview/` 中维护。

## 首次阅读路径

1. **了解全局** → [overview/README.md](./overview/README.md) — 服务定位、上下游、架构、集成边界
2. **按需下钻** → 根据目标进入专题目录（见下方导航表）

## 目录职责与导航

| 目录 | 职责 | 何时查看 |
|------|------|----------|
| [overview/](./overview/README.md) | 服务级总览：系统上下文、集成矩阵、关键流程、数据归属、需求摘要、设计摘要、编码规范 | 首次接触项目、理解全局架构和边界时 |
| [modules/](./modules/README.md) | 内部业务功能：7 个功能点的需求/设计/任务/测试/约定（6 件套） | 开发/修改/测试某个具体功能时 |
| [Integration/](./Integration/README.md) | 外部系统交互：2 个集成点的代理逻辑和接口边界（6 件套） | 联调/排障外部系统交互时 |
| [database/](./database/README.md) | 数据库唯一事实源：表结构、字段、索引、约束、迁移历史 | 涉及数据库变更或查询表结构时 |
| [development/](./development/README.md) | 跨模块开发执行：环境搭建、启动调试、测试验证 | 搭建环境、运行项目、执行测试时 |

## 如何定位一个功能

1. **知道功能名** → 在 [modules/README.md](./modules/README.md) 的业务域索引中查找
2. **知道外部系统** → 在 [Integration/README.md](./Integration/README.md) 中查找
3. **知道表名** → 在 [database/README.md](./database/README.md) 的表清单中查找
4. **知道 API 路径** → 在 [api.md](./api.md) 中查找 HTTP 端点，再跳转到对应模块文档

## 审阅与已有文档

| 文档 | 说明 |
|------|------|
| [AgentReviewNotes.md](./AgentReviewNotes.md) | 审阅记录：遗留问题、证据缺口、人工复核项 |
| [api.md](./api.md) | REST API 接口文档（与 modules/ 互补） |
| [deployment.md](./deployment.md) | 部署文档（与 development/ 互补） |
