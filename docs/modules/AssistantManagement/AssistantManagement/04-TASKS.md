# 助教管理 — 任务清单

## Phase 1: 文档（已完成）

- [x] 创建功能 spec
- [x] 创建技术设计
- [x] 创建任务清单和测试规格

## Phase 2: 后端代码

- [x] AssistantPortalProxyMiddleware.cs + AssistantPortalOptions
- [x] Program.cs 配置（Options、HttpClient、Middleware）
- [x] appsettings.json 添加 AssistantPortal 配置节

## Phase 3: 前端代码

- [x] assistantPortalApi.ts
- [x] AssistantView.vue（助教管理主页面）
- [x] router/index.ts 添加 /assistants 路由
- [x] 侧边导航添加助教管理入口

## Phase 4: 测试与验证

- [x] AssistantPortalProxyMiddleware 单元测试
- [x] AdminController 端点测试（grant/revoke/subjects）
- [x] 编译验证
- [x] 提交并 push

## 已删除任务（2026-07-27）

以下任务随助教-学生关联功能一起删除：

- ~~助教 Portal: AssistantStudent 实体 + DbContext 更新~~
- ~~助教 Portal: AssistantDbService 学生关联方法~~
- ~~助教 Portal: AdminController 学生关联端点~~
- ~~助教 Portal: RevokeAssistantRole 同步清理学生关联~~
- ~~AssistantDbService 学生关联方法测试~~
- ~~AdminController 学生关联端点测试~~

关联管理统一在学生管理页"关联账户"对话框完成。
