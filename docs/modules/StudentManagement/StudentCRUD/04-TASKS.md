# StudentCRUD — 任务清单 (TASKS)

> 说明：本功能代码已实现完成，下列任务为 **代码评审与自动化验证** 任务。

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 ListStudents 方法：确认分页参数规范化逻辑（page < 1 → 1, pageSize Clamp(1,100)）、gRPC 请求构建（Grade 枚举转换）、响应映射（ToDto + PagedResponse）与 SPEC 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "代码阅读签名与 SPEC FR-01/FR-10 一致；编译通过 dotnet build。",
    "notes": "Grade 为 0 或负数时应转为 Unspecified，不作为筛选条件。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 GetStudent 方法：确认 NotFound 和 InvalidArgument 的 RpcException 捕获逻辑与 SPEC FR-12/FR-11 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；异常捕获顺序正确（NotFound 在前，InvalidArgument 在后）。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 CreateStudent 方法：确认参数校验逻辑（姓名非空白、年级有效、IdentityAccountIds 非空、GUID 格式校验）与 SPEC FR-03/FR-08 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；所有 InvalidArgument 场景均有覆盖。",
    "notes": "GUID 格式校验必须列出所有非法 ID。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 UpdateStudent 方法：确认参数校验逻辑（姓名非空白、年级有效、可选 IdentityAccountIds 的 GUID 校验）与 SPEC FR-04/FR-09 一致；确认 gRPC 返回 Success=false 时返回 404 与 SPEC FR-13 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；IdentityAccountIds 为 null 时跳过校验与 AddRange。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-05",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 DeleteStudent 方法：确认 gRPC 返回 Success=false 时返回 404 与 SPEC FR-13 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；InvalidArgument 和 Success=false 均正确处理。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-06",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 GetGrades 和 GetSubjectOptions 方法：确认年级标签为静态字典（1-12）、科目选项通过 gRPC GetAvailableSubjects 获取。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；GradeLabels 包含 12 个条目；GetSubjectOptions 正确映射 SubjectOption。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-07",
    "depends_on": [],
    "action": "评审 Models 目录下 StudentDto、CreateStudentRequest、UpdateStudentRequest、GradeOption、PagedResponse、OperationResponse、ErrorResponse 的 record 定义：确认字段命名和类型与 API 文档一致。",
    "files": ["backend/Models/StudentDto.cs", "backend/Models/CreateStudentRequest.cs", "backend/Models/UpdateStudentRequest.cs", "backend/Models/GradeOption.cs", "backend/Models/PagedResponse.cs", "backend/Models/OperationResponse.cs", "backend/Models/ErrorResponse.cs"],
    "acceptance": "编译通过；record 字段与 SPEC 验收标准一致。",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03", "REVIEW-04", "REVIEW-05", "REVIEW-06", "REVIEW-07"],
    "action": "为 StudentsController 编写单元测试：覆盖 ListStudents 分页参数规范化、GetStudent 正常/NotFound/InvalidArgument、CreateStudent 参数校验与成功路径、UpdateStudent 参数校验与 Success=false、DeleteStudent 成功与 NotFound、GetGrades 返回 12 个选项、GetSubjectOptions 正确映射。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "当前无 Controller 级别测试，需新建测试文件。",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["TEST-01"],
    "action": "执行边界测试：page=0 修正为 1、pageSize=0 修正为 1、pageSize=200 修正为 100、姓名仅含空格返回 400、年级=0 返回 400、年级=13 返回 400。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "",
    "status": "todo"
  },
  {
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02"],
    "action": "在 Release 配置下编译解决方案并运行所有测试，打印覆盖率摘要。",
    "files": ["src/admin_portal/backend/*.csproj", "src/admin_portal/test/**/*.cs"],
    "acceptance": "dotnet test --configuration Release",
    "notes": "必须零警告，无测试失败。",
    "status": "todo"
  }
]
```
