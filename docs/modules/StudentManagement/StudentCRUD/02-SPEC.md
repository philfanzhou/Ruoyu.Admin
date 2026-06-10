# StudentCRUD — 详细需求规格 (SPEC)

## 功能概述和用户故事

**概述**：学生增删改查是 Admin Portal 的核心功能，通过 REST API 对外暴露学生档案的 CRUD 操作，内部通过 gRPC 调用 Student 服务完成数据持久化。

**用户故事 1（管理员创建）**：作为管理员，我要为学生创建档案，输入姓名、年级和至少一个身份账户 ID；系统生成唯一 ID、设置创建时间、建立用户关联映射，并返回完整的学生档案信息。

**用户故事 2（管理员查询）**：作为管理员，我希望按姓名和年级筛选学生列表，分页浏览结果，或直接查看某个学生的详细信息。

**用户故事 3（管理员修改）**：作为管理员，我希望修改学生的姓名、年级，并可选地更新关联的身份账户列表。

**用户故事 4（管理员删除）**：作为管理员，我希望删除不再需要的学生档案。

## 功能要求清单（可独立测试）

- [x] FR-01 能分页查询学生列表，支持按姓名和年级筛选。
- [x] FR-02 能查询单个学生的详细信息。
- [x] FR-03 能创建学生档案，姓名不能为空，年级必须为 1-12，至少关联一个身份账户 ID。
- [x] FR-04 能更新学生档案，姓名不能为空，年级必须为 1-12，IdentityAccountIds 可选。
- [x] FR-05 能删除学生档案。
- [x] FR-06 能获取年级选项列表（1-12 年级静态标签）。
- [x] FR-07 能获取科目选项列表（从 Student 服务动态获取）。
- [x] FR-08 创建时身份账户 ID 必须为合法 GUID 格式，非法格式返回 400。
- [x] FR-09 更新时若提供 IdentityAccountIds，其中每个 ID 必须为合法 GUID 格式。
- [x] FR-10 分页参数 page 默认 1（最小 1），pageSize 默认 20（范围 1-100）。
- [x] FR-11 gRPC 返回 InvalidArgument 时，API 返回 400 Bad Request。
- [x] FR-12 gRPC 返回 NotFound 时，API 返回 404 Not Found（GetStudent 场景）。
- [x] FR-13 Update/Delete 操作 gRPC 返回 Success=false 时，API 返回 404 Not Found。

## 详细的验收标准（可自动验证）

- AC-FR-01：调用 `GET /api/admin/students` 返回 `PagedResponse<StudentDto>`，包含 `items`、`total`、`page`、`pageSize` 字段；传入 `name` 参数时按姓名筛选；传入 `grade` 参数时按年级筛选。
- AC-FR-02：调用 `GET /api/admin/students/{studentId}` 返回 `StudentDto`；若学生不存在返回 404。
- AC-FR-03：调用 `POST /api/admin/students` 传入有效参数，返回 `StudentDto`（含生成的 Id、CreatedAt、UpdatedAt）；姓名为空或空白返回 400；年级不在 1-12 范围返回 400；IdentityAccountIds 为空或 null 返回 400。
- AC-FR-04：调用 `PUT /api/admin/students/{studentId}` 传入有效参数，返回 `OperationResponse(Success=true)`；姓名为空返回 400；年级无效返回 400；若 gRPC 返回 Success=false 则返回 404。
- AC-FR-05：调用 `DELETE /api/admin/students/{studentId}` 返回 `OperationResponse(Success=true)`；若 gRPC 返回 Success=false 则返回 404。
- AC-FR-06：调用 `GET /api/admin/students/grades` 返回 12 个 `GradeOption`，Value 从 1 到 12，Label 为对应中文年级名称。
- AC-FR-07：调用 `GET /api/admin/students/subject-options` 返回 `List<SubjectOption>`，每个选项含 Value、Name、DisplayName。
- AC-FR-08：创建时传入非法 GUID 格式的 IdentityAccountId，返回 400 且错误消息包含非法 ID。
- AC-FR-09：更新时传入非法 GUID 格式的 IdentityAccountId，返回 400 且错误消息包含非法 ID。
- AC-FR-10：page=0 或 page=-1 时自动修正为 1；pageSize=0 时修正为 1；pageSize=200 时修正为 100。
- AC-FR-11：gRPC 抛出 `RpcException(StatusCode.InvalidArgument)` 时，API 返回 400 Bad Request，body 为 `ErrorResponse`。
- AC-FR-12：gRPC 抛出 `RpcException(StatusCode.NotFound)` 时（GetStudent），API 返回 404 Not Found。
- AC-FR-13：Update/Delete 操作 gRPC 返回 `Success=false` 时，API 返回 404，body 为 `ErrorResponse(ErrorMessage)`。

## 非功能需求

- **性能**：列表查询 P95 < 500ms（含 gRPC 调用）。
- **安全**：仅管理员可操作（由上层 Identity 代理中间件负责鉴权）。
- **可靠性**：依赖 Student gRPC 服务的可用性；gRPC 不可用时返回 502。
- **可观测性**：关键错误应通过 `ILogger` 记录。

## 测试策略

- **覆盖率目标**：StudentsController 中 CRUD 相关方法代码行覆盖率 ≥ 80%。
- **必须测试的错误路径**：空姓名、无效年级、空身份账户列表、非法 GUID 格式、gRPC InvalidArgument、gRPC NotFound、gRPC Success=false。
- **测试环境要求**：使用 `xUnit + Moq` 进行单元测试；gRPC Client 通过 Mock 替换。
- **禁止事项**：不得在单元测试中连接真实 gRPC 服务。
