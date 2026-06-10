# OpenSubjectManagement — 详细需求规格 (SPEC)

## 功能概述和用户故事

**概述**：开放科目管理功能允许管理员配置学生可学习的学科及其开放时间范围，通过 gRPC 调用 Student 服务完成数据持久化。管理员可以查看学生的开放科目列表（支持按活跃状态筛选），并全量设置学生的开放科目配置。

**用户故事 1（管理员查看开放科目）**：作为管理员，我要查看学生的开放科目列表，了解该学生当前可以学习哪些学科，以及每个科目的开放起止日期和活跃状态。

**用户故事 2（管理员筛选活跃科目）**：作为管理员，我希望仅查看当前活跃的科目，以便快速了解学生当前可用的学科。

**用户故事 3（管理员设置开放科目）**：作为管理员，我要设置学生的开放科目配置，指定每个科目的开放开始日期和可选的结束日期，以便控制学生可以学习哪些学科。

## 功能要求清单（可独立测试）

- [x] FR-01 能查询学生的开放科目列表。
- [x] FR-02 查询时支持 activeOnly 参数筛选，仅返回当前活跃的科目。
- [x] FR-03 能全量设置学生的开放科目，每个科目包含 Subject、OpenStartDate、可选 OpenEndDate。
- [x] FR-04 科目值必须通过 SubjectConstants.IsValid 校验，无效科目返回 400。
- [x] FR-05 OpenStartDate 必填，不能为空，否则返回 400。
- [x] FR-06 OpenStartDate 必须为合法 DateOnly 格式，否则返回 400。
- [x] FR-07 OpenEndDate 可选，若提供则必须为合法 DateOnly 格式，否则返回 400。
- [x] FR-08 设置时若 gRPC 返回 Success=false，API 返回 400。
- [x] FR-09 设置时若 gRPC 返回 InvalidArgument，API 返回 400。
- [x] FR-10 查询返回的每个 OpenSubjectDto 包含 Id、Subject、OpenStartDate、OpenEndDate（可空）、IsActive。

## 详细的验收标准（可自动验证）

- AC-FR-01：调用 `GET /api/admin/students/{studentId}/open-subjects` 返回 `List<OpenSubjectDto>`。
- AC-FR-02：调用 `GET /api/admin/students/{studentId}/open-subjects?activeOnly=true` 时，gRPC 请求中 `ActiveOnly=true`。
- AC-FR-03：调用 `PUT /api/admin/students/{studentId}/open-subjects` 传入 `SetOpenSubjectsRequest`，成功返回 `OperationResponse(true, "Open subjects updated successfully.")`。
- AC-FR-04：设置时传入无效科目值（如 Subject=-1），返回 400 `ErrorResponse("Invalid subject value: -1")`。
- AC-FR-05：设置时 OpenStartDate 为空字符串，返回 400 `ErrorResponse("Open start date is required")`。
- AC-FR-06：设置时 OpenStartDate 为非法日期格式（如 "not-a-date"），返回 400 `ErrorResponse("Invalid start date format: not-a-date")`。
- AC-FR-07：设置时 OpenEndDate 为非法日期格式，返回 400 `ErrorResponse("Invalid end date format: {value}")`。
- AC-FR-08：设置时 gRPC 返回 `Success=false`，API 返回 400 `ErrorResponse(ErrorMessage)`。
- AC-FR-09：设置时 gRPC 抛出 `RpcException(InvalidArgument)`，API 返回 400。
- AC-FR-10：查询返回的 OpenSubjectDto 中 OpenEndDate 为 null 时 JSON 中不出现该字段或为 null；IsActive 为 bool 类型。

## 非功能需求

- **性能**：查询和设置操作 P95 < 500ms（含 gRPC 调用）。
- **安全**：仅管理员可操作（由上层 Identity 代理中间件负责鉴权）。
- **可靠性**：依赖 Student gRPC 服务的可用性；gRPC 不可用时返回 502。
- **可观测性**：关键错误应通过 `ILogger` 记录。

## 测试策略

- **覆盖率目标**：StudentsController 中开放科目相关方法代码行覆盖率 ≥ 80%。
- **必须测试的错误路径**：无效科目值、空 OpenStartDate、非法日期格式（开始和结束）、gRPC InvalidArgument、gRPC Success=false。
- **测试环境要求**：使用 `xUnit + Moq`；gRPC Client 通过 Mock 替换。
- **禁止事项**：不得在单元测试中连接真实 gRPC 服务。
