# AccountLinking — 任务清单 (TASKS)

> 说明：本功能代码已实现完成，下列任务为 **代码评审与自动化验证** 任务。

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 GetIdentityAccountsByStudentId 方法：确认 gRPC 请求构建、响应映射（AccountIds → IReadOnlyList<string>）与 SPEC FR-01 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；返回类型为 IReadOnlyList<string>。",
    "notes": "该方法无 try-catch，gRPC 异常将向上传播。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 LinkIdentityAccountToStudent 方法：确认参数校验逻辑（IdentityAccountId 非空白、合法 GUID 格式）、gRPC 返回 Success=false 时返回 404、InvalidArgument 捕获与 SPEC FR-02/FR-06/FR-07 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；所有校验和错误处理路径覆盖。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 UnlinkIdentityAccountFromStudent 方法：确认 gRPC 返回 Success=false 时返回 404、InvalidArgument 捕获与 SPEC FR-03/FR-08/FR-09 一致。",
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
    "action": "评审 IdentityAccountsController.cs 中 GetStudentsByIdentityAccountId 方法：确认 gRPC Client 通过方法参数注入、请求构建和响应映射与 SPEC FR-05 一致。",
    "files": ["backend/Controllers/IdentityAccountsController.cs"],
    "acceptance": "编译通过；返回 IReadOnlyList<StudentDto>。",
    "notes": "gRPC Client 通过方法参数注入是特殊模式，需确认 DI 配置正确。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-06",
    "depends_on": [],
    "action": "评审 Models 目录下 LinkUserRequest 和 IdentityAccountDto 的 record 定义：确认字段命名和类型与 API 文档一致。",
    "files": ["backend/Models/LinkUserRequest.cs", "backend/Models/IdentityAccountDto.cs"],
    "acceptance": "编译通过；record 字段与 SPEC 验收标准一致。",
    "notes": "IdentityUserItem 为 internal record，不对外暴露。",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03"],
    "action": "为 StudentsController 关联/解关联方法编写单元测试：覆盖 GetIdentityAccountsByStudentId 正常路径、LinkIdentityAccountToStudent 参数校验（空 ID、非法 GUID）和成功路径及 gRPC Success=false、UnlinkIdentityAccountFromStudent 成功路径及 gRPC Success=false 和 InvalidArgument。",
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
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02"],
    "action": "在 Release 配置下编译解决方案并运行所有测试，打印覆盖率摘要。",
    "files": ["admin_portal/backend/*.csproj", "admin_portal/test/**/*.cs"],
    "acceptance": "dotnet test --configuration Release",
    "notes": "必须零警告，无测试失败。",
    "status": "todo"
  }
]
```
