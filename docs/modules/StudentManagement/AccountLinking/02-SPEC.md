# AccountLinking — 详细需求规格 (SPEC)

## 功能概述和用户故事

**概述**：身份账户关联功能允许管理员在学生档案和身份账户之间建立/解除关联关系，并通过 Identity Service 的 HTTP API 批量获取身份账户的详细信息。同时支持通过身份账户反查关联的学生列表。关联管理统一在学生管理页面"关联账户"对话框完成，不区分账户角色（教师/助教/普通用户均可），教师/助教管理页面不再提供关联学生功能。

**用户故事 1（管理员关联账户）**：作为管理员，我要将一个身份账户关联到学生档案，使学生可以通过该身份登录系统。

**用户故事 2（管理员解除关联）**：作为管理员，我要解除身份账户与学生档案的关联，使学生不再通过该身份登录。

**用户故事 3（管理员查看关联账户）**：作为管理员，我要查看学生关联的所有身份账户 ID，并批量获取这些账户的详细信息（用户名、显示名、手机号等）。

**用户故事 4（管理员反查学生）**：作为管理员，我要通过身份账户 ID 查找该账户关联的所有学生档案。

**用户故事 5（学生侧统一管理关联账户）**：作为管理员，我要在学生管理页面的"关联账户"对话框中，搜索任意 Identity 用户并添加到该学生，或移除该学生已关联的账户，无需切换到教师/助教管理页面，且不区分账户角色。

## 功能要求清单（可独立测试）

- [x] FR-01 能查询学生关联的身份账户 ID 列表。
- [x] FR-02 能将身份账户关联到学生档案，IdentityAccountId 必须为合法 GUID 格式且不能为空。
- [x] FR-03 能解除身份账户与学生档案的关联。
- [x] FR-04 能批量查询身份账户的详细信息（通过 Identity Service HTTP API）。
- [x] FR-05 能通过身份账户 ID 反查关联的学生列表。
- [x] FR-06 关联时若 Student HTTP 服务返回 NotFound，API 返回 404。
- [x] FR-07 关联时若 Student HTTP 服务返回 BadRequest，API 返回 400。
- [x] FR-08 解关联时若 Student HTTP 服务返回 NotFound，API 返回 404。
- [x] FR-09 解关联时若 Student HTTP 服务返回 BadRequest，API 返回 400。
- [x] FR-10 批量查询时若 Identity Service 不可用，返回空列表而非错误。
- [x] FR-11 批量查询时若 AppId 或 AppSecret 未配置，返回空列表。
- [x] FR-12 批量查询仅返回请求中包含的账户（按 UserId 过滤）。
- [x] FR-13 学生管理页面"关联账户"列提供"管理"按钮，点击后弹出对话框展示该学生已关联的全部 Identity 账户（不区分角色），并支持搜索任意 Identity 用户进行添加/移除（调用 `GET /api/admin/students/{studentId}/accounts`、`POST /api/admin/students/{studentId}/accounts`、`DELETE /api/admin/students/{studentId}/accounts/{accountId}`）。

## 详细的验收标准（可自动验证）

- AC-FR-01：调用 `GET /api/admin/students/{studentId}/accounts` 返回 `IReadOnlyList<string>`（账户 ID 列表）。
- AC-FR-02：调用 `POST /api/admin/students/{studentId}/accounts` 传入 `LinkUserRequest(IdentityAccountId)`，IdentityAccountId 为空返回 400；非法 GUID 格式返回 400；合法时 HTTP 调用成功返回 `OperationResponse(true, "Identity account linked to student successfully.")`。
- AC-FR-03：调用 `DELETE /api/admin/students/{studentId}/accounts/{accountId}` 返回 `OperationResponse(true, "Identity account unlinked from student.")`；HTTP 返回 NotFound 时返回 404。
- AC-FR-04：调用 `POST /api/admin/identity-accounts/batch` 传入 `List<string>` 账户 ID，返回 `List<IdentityAccountDto>`；每个 DTO 含 UserId、Username、DisplayName、Phone、Remark。
- AC-FR-05：调用 `GET /api/admin/accounts/{accountId}/students` 返回 `IReadOnlyList<StudentDto>`。
- AC-FR-13：在学生管理页面"关联账户"对话框中：
  - 已关联账户列表每行展示"移除"按钮，点击调用 `studentAdminApi.unlinkIdentityAccount(studentId, accountId)`（DELETE `/api/admin/students/{studentId}/accounts/{accountId}`），成功后从列表移除。
  - 搜索框输入用户名或手机号（至少 2 个字符），调用 `identityApi.getUsers()` 搜索任意 Identity 用户，过滤出未关联的账户，每行展示"添加"按钮，点击调用 `studentAdminApi.linkIdentityAccount(studentId, accountId)`（POST `/api/admin/students/{studentId}/accounts`），成功后加入已关联列表。
  - 操作失败时显示错误消息，不关闭对话框。
- AC-FR-06：关联操作 Student HTTP 服务返回 NotFound 时，API 返回 404 `ErrorResponse(ErrorMessage)`。
- AC-FR-07：关联操作 Student HTTP 服务返回 BadRequest 时，API 返回 400。
- AC-FR-08：解关联操作 Student HTTP 服务返回 NotFound 时，API 返回 404 `ErrorResponse(ErrorMessage)`。
- AC-FR-09：解关联操作 Student HTTP 服务返回 BadRequest 时，API 返回 400。
- AC-FR-10：Identity Service HTTP 调用失败时，批量查询返回空列表 `[]`。
- AC-FR-11：`_options.AppId` 或 `_options.AppSecret` 为空时，批量查询返回空列表 `[]`。
- AC-FR-12：批量查询返回的结果仅包含请求 ID 列表中存在的账户（大小写不敏感匹配）。

## 非功能需求

- **性能**：批量查询 P95 < 1s（含 Identity Service HTTP 调用）。
- **安全**：仅管理员可操作；Identity Service 调用使用 AppId/AppSecret 认证。
- **可靠性**：Identity Service 不可用时不影响其他功能，批量查询降级返回空列表。
- **可观测性**：Identity Service 调用失败应通过 `ILogger` 记录 Warning 级别日志。

## 测试策略

- **覆盖率目标**：StudentsController 关联/解关联/查询账户 ID 方法 + IdentityAccountsController 方法代码行覆盖率 ≥ 80%。
- **必须测试的错误路径**：空 IdentityAccountId、非法 GUID、Student HTTP BadRequest、Student HTTP NotFound、Identity Service HTTP 失败、AppId/AppSecret 未配置。
- **测试环境要求**：使用 `xUnit + Moq`；IStudentHttpClient 和 HttpClientFactory 通过 Mock 替换。
- **前端验证**：StudentView "关联账户"对话框的添加/移除按钮在浏览器中手动验证。
- **禁止事项**：不得在单元测试中连接真实 HTTP 服务或 Identity Service。

## 已移除功能

- **教师/助教管理页"关联学生"列与管理按钮**：原设计在教师/助教管理页提供"关联学生"列展示学生数量并提供"管理"按钮打开 `StudentAssociationDrawer`。该功能与学生管理页"关联账户"对话框语义重复，且数量加载采用 N+1 查询（每个教师/助教一次 HTTP 请求）存在性能问题。已于 2026-07-27 删除前后端实现：
  - 前端：删除 `StudentAssociationDrawer.vue` 组件、`studentLinkApi.ts` 服务、TeacherView/AssistantView 的"关联学生"列与相关函数。
  - 后端：删除 `teacher_portal`/`assistant_portal` 的 `GET/POST/DELETE /api/admin/{role}s/{userId}/students[/{studentId}]` 端点及单元测试。
  - 关联管理统一在学生管理页"关联账户"对话框完成，调用 admin_portal 的 `GET/POST/DELETE /api/admin/students/{studentId}/accounts` 端点，不区分账户角色。
