# AccountLinking — 任务清单 (TASKS)

> 说明：本功能代码已实现完成，下列任务为 **代码评审与自动化验证** 任务。FR-14/FR-15 双向管理（IMPL-01）已实现完成。

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 GetIdentityAccountsByStudentId 方法：确认 HTTP 请求构建、响应映射（AccountIds → IReadOnlyList<string>）与 SPEC FR-01 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；返回类型为 IReadOnlyList<string>。",
    "notes": "该方法无 try-catch，HTTP 异常将向上传播。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 LinkIdentityAccountToStudent 方法：确认参数校验逻辑（IdentityAccountId 非空白、合法 GUID 格式）、HTTP 返回 NotFound 时返回 404、BadRequest 捕获与 SPEC FR-02/FR-06/FR-07 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；所有校验和错误处理路径覆盖。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 UnlinkIdentityAccountFromStudent 方法：确认 HTTP 返回 NotFound 时返回 404、BadRequest 捕获与 SPEC FR-03/FR-08/FR-09 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；两个 Guid 路由参数正确传递。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "评审 IdentityAccountsController.cs 中 GetIdentityAccountsBatch 方法：确认空列表降级逻辑（accountIds 空、AppId/AppSecret 空、HTTP 失败、反序列化失败、异常捕获）、targetIds 大小写不敏感匹配、IdentityUserItem → IdentityAccountDto 映射与 SPEC FR-04/FR-10/FR-11/FR-12 一致。",
    "files": ["backend/Controllers/IdentityAccountsController.cs"],
    "acceptance": "编译通过；所有降级路径返回空列表而非错误。",
    "notes": "Identity Service 调用使用 X-Admin-AppId 和 X-Admin-AppSecret Header 认证。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-05",
    "depends_on": [],
    "action": "评审 IdentityAccountsController.cs 中 GetStudentsByIdentityAccountId 方法：确认 IStudentHttpClient 调用、请求构建和响应映射与 SPEC FR-05 一致。",
    "files": ["backend/Controllers/IdentityAccountsController.cs"],
    "acceptance": "编译通过；返回 IReadOnlyList<StudentDto>。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-06",
    "depends_on": [],
    "action": "评审 Models 目录下 LinkUserRequest、IdentityAccountDto、LinkedAccountDto、LinkedAccountsResponse 的 record 定义：确认字段命名和类型与 API 文档一致。",
    "files": ["backend/Models/LinkUserRequest.cs", "backend/Models/IdentityAccountDto.cs"],
    "acceptance": "编译通过；record 字段与 SPEC 验收标准一致。",
    "notes": "IdentityUserItem 为 internal record，不对外暴露。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-07",
    "depends_on": [],
    "action": "评审 StudentAssociationsController.cs 中 GetLinkedAccounts 方法：确认聚合查询逻辑（取学生 identityAccountIds → HTTP 调 TeacherPortal/AssistantPortal → 过滤 userId 在 accountIdSet 中的记录）、容错降级（任一下游不可用返回空列表）与 SPEC FR-13 一致。",
    "files": ["backend/Controllers/StudentAssociationsController.cs"],
    "acceptance": "编译通过；任一下游不可用时对应角色返回空列表。",
    "notes": "JSON 解析兼容 { data: [...] } 与裸数组两种格式。",
    "status": "implemented"
  },
  {
    "id": "IMPL-01",
    "depends_on": [],
    "action": "在 StudentView.vue 的'关联教师/助教'对话框中实现双向管理：(1) 已关联教师/助教列表每行添加'移除'按钮，调用 studentLinkApi.unlink(userId, studentId, role)；(2) 添加搜索框，输入关键词后调用 teacherPortalClient.getTeachers() / assistantPortalClient 拉取列表，按关键词过滤并排除已关联，显示在'可添加'区域；(3) 可添加行点击'添加'按钮，调用 studentLinkApi.link(userId, studentId, role)；(4) 操作成功后刷新已关联列表，失败显示错误消息。",
    "files": ["frontend/src/views/StudentView.vue"],
    "acceptance": "编译通过（npm run build）；浏览器手动验证：能搜索教师/助教、能添加/移除关联、操作后列表正确刷新；StudentAssociationDrawer 现有行为不变。",
    "notes": "复用 studentLinkApi 与 StudentAssociationDrawer 同源端点；后端无需新增端点。搜索框可按需懒加载（输入≥1字符触发）。",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03"],
    "action": "为 StudentsController 关联/解关联方法编写单元测试：覆盖 GetIdentityAccountsByStudentId 正常路径、LinkIdentityAccountToStudent 参数校验（空 ID、非法 GUID）和成功路径及 HTTP NotFound、UnlinkIdentityAccountFromStudent 成功路径及 HTTP NotFound 和 BadRequest。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "当前无 Controller 级别测试，需新建测试文件。",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["REVIEW-04", "REVIEW-05"],
    "action": "为 IdentityAccountsController 编写单元测试：覆盖 GetIdentityAccountsBatch 正常路径、空 accountIds 降级、AppId/AppSecret 未配置降级、HTTP 失败降级、异常捕获降级、targetIds 过滤、GetStudentsByIdentityAccountId 正常路径。",
    "files": ["test/Admin.WebApi.Tests/Controllers/IdentityAccountsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~IdentityAccountsControllerTests",
    "notes": "需 Mock IHttpClientFactory 和 IOptions<IdentityServiceOptions>。",
    "status": "todo"
  },
  {
    "id": "TEST-03",
    "depends_on": ["REVIEW-07"],
    "action": "为 StudentAssociationsController.GetLinkedAccounts 编写单元测试：覆盖学生无 identityAccountIds、TeacherPortal 不可用、AssistantPortal 不可用、JSON 兼容两种格式、userId 过滤等场景。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentAssociationsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentAssociationsControllerTests",
    "notes": "需 Mock IHttpClientFactory、IStudentHttpClient、IConfiguration。",
    "status": "todo"
  },
  {
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02", "TEST-03", "IMPL-01"],
    "action": "在 Release 配置下编译解决方案并运行所有测试，前端执行 npm run build，打印覆盖率摘要。",
    "files": ["src/admin_portal/backend/*.csproj", "src/admin_portal/test/**/*.cs", "src/admin_portal/frontend/package.json"],
    "acceptance": "dotnet test --configuration Release；cd src/admin_portal/frontend && npm run build；必须零警告，无测试失败。",
    "notes": "",
    "status": "todo"
  }
]
```
