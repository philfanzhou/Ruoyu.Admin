# 身份账户关联 (AccountLinking)

## 功能名称和一句话概括

**AccountLinking** — 管理员将身份账户与学生档案进行关联、解关联及双向查询。

## 核心用户故事

作为管理员，我希望将身份账户关联到学生档案并支持双向查询与双向管理，以便学生可以通过不同身份登录系统且管理员能追溯关联关系，并能从学生侧或教师/助教侧任一视角增删关联。

## 补充约束

1. **幂等性**：关联操作非幂等（重复关联同一账户到同一学生由 Student HTTP 服务层保证唯一性）
2. **并发**：关联唯一性由 Student HTTP 服务层保证，Admin Portal 不做额外并发控制
3. **事务边界**：关联/解关联为单次 HTTP 调用，无跨服务事务
4. **失败降级**：Identity Service 不可用时，批量查询身份账户信息返回空列表而非错误
5. **关联校验**：IdentityAccountId 必须为合法 GUID 格式且不能为空
6. **解关联参数**：需同时提供 studentId 和 accountId，均为 GUID 路由参数
7. **双向管理**：关联关系的增删可从教师/助教侧（StudentAssociationDrawer，按角色筛选）或学生侧（StudentView"关联账户"对话框，不区分角色）任一视角发起。学生侧对话框调用 `POST/DELETE /api/admin/students/{studentId}/accounts` 与 `GET /api/admin/students/{studentId}/accounts`，可关联任意 Identity 账户（教师、助教、普通用户均可），不按角色区分

## 关键验收条件摘要

1. 可查看学生已关联的身份账户 ID 列表
2. 可将新的身份账户关联到学生档案，IdentityAccountId 必须为合法 GUID
3. 同一身份账户不能重复关联到同一学生
4. 可解除身份账户与学生档案的关联
5. 批量查询身份账户信息通过 Identity Service HTTP API 实现
6. Identity Service 不可用时批量查询返回空列表
7. 可通过身份账户 ID 反查关联的学生列表
8. 学生管理页面"关联账户"列提供"管理"按钮，点击后弹出对话框展示该学生已关联的全部 Identity 账户（不区分角色），并支持搜索任意 Identity 用户进行添加/移除（调用 `GET /api/admin/students/{studentId}/accounts`、`POST /api/admin/students/{studentId}/accounts`、`DELETE /api/admin/students/{studentId}/accounts/{accountId}`）

## 已移除功能

- **学生列表"关联账户"（Identity 绑定状态）列**：原设计在学生列表单独一列展示 `identityAccountIds[0]`（首个绑定的 Identity 账户）及"关联账户"按钮打开单选关联对话框。该列与"关联教师/助教"列语义重复，已于 2026-07-27 合并：学生列表仅保留一列"关联账户"（原"关联教师/助教"列改名），点击"管理"按钮弹出的对话框统一管理该学生的全部 Identity 账户关联关系。
- **学生列表"关联教师/助教"数量标签**：原设计在学生列表显示"X人/无"数量标签，由于数据为点击"管理"按钮后的懒加载，列表初次加载时全部显示"无"，存在误导。已于 2026-07-27 移除数量标签，仅保留"管理"按钮，数量在点击后弹出的对话框内展示。
- **教师/助教管理页"关联账户"列**：原设计在教师/助教管理页提供"关联/更换 Identity 账户"功能（`PUT /api/{teacher-portal|assistant-portal}/admin/{role}s/{userId}/user-id`）。该功能未被要求且引入了额外的 userId 变更复杂度，已于 2026-07-27 删除前后端实现。教师/助教的 `userId` 在 `grant-teacher` / `grant-assistant` 授予时绑定，不再支持事后更换。

## 范围外

- 身份账户的创建、修改和删除
- 身份账户密码管理
- 身份认证流程（登录/登出）
- 跨租户身份账户关联

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
