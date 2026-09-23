# OpenSubjectManagement — 任务清单 (TASKS)

> 说明：本功能代码已实现完成，下列任务为 **代码评审与自动化验证** 任务。

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 GetStudentOpenSubjects 方法：确认 gRPC 请求构建（StudentId、ActiveOnly 默认 false）、响应映射（OpenEndDate 空字符串转 null）与 SPEC FR-01/FR-02 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；ActiveOnly 默认值为 false；OpenEndDate 空字符串映射为 null。",
    "notes": "该方法无 try-catch，gRPC 异常将向上传播。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "评审 StudentsController.cs 中 SetStudentOpenSubjects 方法：确认参数校验逻辑（SubjectConstants.IsValid、OpenStartDate 非空、DateOnly 格式校验、OpenEndDate 可选格式校验）、gRPC 请求构建（OpenEndDate 格式化）、gRPC 返回 Success=false 时返回 400 与 SPEC FR-03~FR-09 一致。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；所有校验路径和错误处理覆盖。",
    "notes": "注意 SetStudentOpenSubjects 的 Success=false 返回 400 而非 404，与 UpdateStudent/DeleteStudent 不同。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "评审 Models/OpenSubjectDtos.cs 中 OpenSubjectDto、SetOpenSubjectsRequest、SubjectItem、SubjectOption 的 record 定义：确认字段命名和类型与 SPEC 一致。",
    "files": ["backend/Models/OpenSubjectDtos.cs"],
    "acceptance": "编译通过；record 字段与 SPEC 验收标准一致。",
    "notes": "OpenSubjectDto 为 public record（非 sealed），与其他 Model 的 sealed record 不同。",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "确认 SubjectConstants.IsValid 的科目值范围和 SubjectConstants.DateFormat 的格式字符串，验证 SetStudentOpenSubjects 中的校验逻辑使用正确的常量。",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "编译通过；SubjectConstants.IsValid 覆盖所有有效科目值。",
    "notes": "SubjectConstants 定义在 Ruoyu.Admin.Common.Constants 包中，需确认版本一致性。",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03", "REVIEW-04"],
    "action": "为 StudentsController 开放科目方法编写单元测试：覆盖 GetStudentOpenSubjects 正常路径和 activeOnly 参数、SetStudentOpenSubjects 成功路径、无效科目值、空 OpenStartDate、非法日期格式（开始和结束）、gRPC Success=false、gRPC InvalidArgument。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "当前无 Controller 级别测试，需新建测试文件。需 Mock SubjectConstants.IsValid 的行为。",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["TEST-01"],
    "action": "执行边界测试：Subjects 列表为空时跳过校验、Subjects 列表包含多个有效科目、OpenEndDate 为 null 时 gRPC 请求中 OpenEndDate 为空字符串、OpenEndDate 为合法日期时正确格式化。",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "",
    "status": "todo"
  },
  {
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02"],
    "action": "在 Release 配置下编译解决方案并运行所有测试，打印覆盖率摘要。",
    "files": ["backend/*.csproj", "backend/Tests/**/*.cs"],
    "acceptance": "dotnet test --configuration Release",
    "notes": "必须零警告，无测试失败。",
    "status": "todo"
  }
]
```
