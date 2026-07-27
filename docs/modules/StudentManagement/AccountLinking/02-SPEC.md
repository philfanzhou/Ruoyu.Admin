# AccountLinking — 详细需求规格 (SPEC)

## 功能概述和用户故事

**概述**：身份账户关联功能允许管理员在学生档案和身份账户之间建立/解除关联关系，并通过 Identity Service 的 HTTP API 批量获取身份账户的详细信息。同时支持通过身份账户反查关联的学生列表，以及查看学生关联的教师/助教。

**用户故事 1（管理员关联账户）**：作为管理员，我要将一个身份账户关联到学生档案，使学生可以通过该身份登录系统。

**用户故事 2（管理员解除关联）**：作为管理员，我要解除身份账户与学生档案的关联，使学生不再通过该身份登录。

**用户故事 3（管理员查看关联账户）**：作为管理员，我要查看学生关联的所有身份账户 ID，并批量获取这些账户的详细信息（用户名、显示名、手机号等）。

**用户故事 4（管理员反查学生）**：作为管理员，我要通过身份账户 ID 查找该账户关联的所有学生档案。

**用户故事 5（查看学生关联教师/助教）**：作为管理员，我要查看学生关联的所有教师和助教，了解该学生的师资配置。

## 功能要求清单（可独立测试）

- [x] FR-01 能查询学生关联的身份账户 ID 列表。
- [x] FR-02 能将身份账户关联到学生档案，IdentityAccountId 必须为合法 GUID 格式且不能为空。
- [x] FR-03 能解除身份账户与学生档案的关联。
- [x] FR-04 能批量查询身份账户的详细信息（通过 Identity Service HTTP API）。
- [x] FR-05 能通过身份账户 ID 反查关联的学生列表。
- [x] FR-13 能查询学生关联的教师和助教列表（通过学生 identityAccountIds 反查 TeacherPortal/AssistantPortal）。
- [x] FR-06 关联时若 gRPC 返回 Success=false，API 返回 404。
- [x] FR-07 关联时若 gRPC 返回 InvalidArgument，API 返回 400。
- [x] FR-08 解关联时若 gRPC 返回 Success=false，API 返回 404。
- [x] FR-09 解关联时若 gRPC 返回 InvalidArgument，API 返回 400。
- [x] FR-10 批量查询时若 Identity Service 不可用，返回空列表而非错误。
- [x] FR-11 批量查询时若 AppId 或 AppSecret 未配置，返回空列表。
- [x] FR-12 批量查询仅返回请求中包含的账户（按 UserId 过滤）。

## 详细的验收标准（可自动验证）

- AC-FR-01：调用 `GET /api/admin/students/{studentId}/accounts` 返回 `IReadOnlyList<string>`（账户 ID 列表）。
- AC-FR-02：调用 `POST /api/admin/students/{studentId}/accounts` 传入 `LinkUserRequest(IdentityAccountId)`，IdentityAccountId 为空返回 400；非法 GUID 格式返回 400；合法时 gRPC 调用成功返回 `OperationResponse(true, "Identity account linked to student successfully.")`。
- AC-FR-03：调用 `DELETE /api/admin/students/{studentId}/accounts/{accountId}` 返回 `OperationResponse(true, "Identity account unlinked from student.")`；gRPC 返回 Success=false 时返回 404。
- AC-FR-04：调用 `POST /api/admin/identity-accounts/batch` 传入 `List<string>` 账户 ID，返回 `List<IdentityAccountDto>`；每个 DTO 含 UserId、Username、DisplayName、Phone、Remark。
- AC-FR-05：调用 `GET /api/admin/accounts/{accountId}/students` 返回 `IReadOnlyList<StudentDto>`。
- AC-FR-13：调用 `GET /api/admin/students/{studentId}/linked-accounts` 返回 `{ teachers: LinkedAccountDto[], assistants: LinkedAccountDto[] }`；当 TeacherPortal/AssistantPortal 不可用时返回对应空列表。
- AC-FR-06：关联操作 gRPC 返回 `Success=false` 时，API 返回 404 `ErrorResponse(ErrorMessage)`。
- AC-FR-07：关联操作 gRPC 抛出 `RpcException(InvalidArgument)` 时，API 返回 400。
- AC-FR-08：解关联操作 gRPC 返回 `Success=false` 时，API 返回 404 `ErrorResponse(ErrorMessage)`。
- AC-FR-09：解关联操作 gRPC 抛出 `RpcException(InvalidArgument)` 时，API 返回 400。
- AC-FR-10：Identity Service HTTP 调用失败时，批量查询返回空列表 `[]`。
- AC-FR-11：`_options.AppId` 或 `_options.AppSecret` 为空时，批量查询返回空列表 `[]`。
- AC-FR-12：批量查询返回的结果仅包含请求 ID 列表中存在的账户（大小写不敏感匹配）。

## 非功能需求

- **性能**：批量查询 P95 < 1s（含 Identity Service HTTP 调用）。
- **安全**：仅管理员可操作；Identity Service 调用使用 AppId/AppSecret 认证。
- **可靠性**：Identity Service 不可用时不影响其他功能，批量查询降级返回空列表。
- **可观测性**：Identity Service 调用失败应通过 `ILogger` 记录 Warning 级别日志。

## 测试策略

- **覆盖率目标**：StudentsController 关联/解关联方法 + IdentityAccountsController 方法代码行覆盖率 ≥ 80%。
- **必须测试的错误路径**：空 IdentityAccountId、非法 GUID、gRPC InvalidArgument、gRPC Success=false、Identity Service HTTP 失败、AppId/AppSecret 未配置。
- **测试环境要求**：使用 `xUnit + Moq`；gRPC Client 和 HttpClientFactory 通过 Mock 替换。
- **禁止事项**：不得在单元测试中连接真实 gRPC 服务或 Identity Service。
