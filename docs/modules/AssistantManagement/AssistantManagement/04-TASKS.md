# 助教管理 — 任务清单

## Phase 1: 文档（已完成）

- [x] 创建功能 spec
- [x] 创建技术设计
- [x] 创建任务清单和测试规格

## Phase 2: 后端代码

- [ ] AssistantPortalProxyMiddleware.cs + AssistantPortalOptions
- [ ] Program.cs 配置（Options、HttpClient、Middleware）
- [ ] appsettings.json 添加 AssistantPortal 配置节
- [ ] 助教 Portal: AssistantStudent 实体 + DbContext 更新
- [ ] 助教 Portal: AssistantDbService 学生关联方法
- [ ] 助教 Portal: AdminController 学生关联端点
- [ ] 助教 Portal: RevokeAssistantRole 同步清理学生关联

## Phase 3: 前端代码

- [ ] assistantPortalApi.ts
- [ ] AssistantView.vue（助教管理主页面）
- [ ] router/index.ts 添加 /assistants 路由
- [ ] 侧边导航添加助教管理入口

## Phase 4: 测试与验证

- [ ] AssistantPortalProxyMiddleware 单元测试
- [ ] AssistantDbService 学生关联方法测试
- [ ] AdminController 学生关联端点测试
- [ ] 编译验证
- [ ] 提交并 push
