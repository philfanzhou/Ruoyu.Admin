# OpenSubjectManagement — 设计说明 (DESIGN)

## 本功能在项目中的目录与文件结构

```
src/admin_portal/
├── backend/
│   ├── Controllers/
│   │   └── StudentsController.cs              # GetStudentOpenSubjects, SetStudentOpenSubjects
│   └── Models/
│       ├── OpenSubjectDtos.cs                  # OpenSubjectDto, SetOpenSubjectsRequest, SubjectItem, SubjectOption
│       ├── OperationResponse.cs               # 操作结果响应（record，共享）
│       └── ErrorResponse.cs                   # 错误响应（record，共享）
├── test/
│   └── Admin.WebApi.Tests/
│       └── Models/
│           └── ModelTests.cs                  # 模型构造函数测试
└── docs/modules/StudentManagement/OpenSubjectManagement/
    ├── 01-FEATURE.md
    ├── 02-SPEC.md
    ├── 03-DESIGN.md
    ├── 04-TASKS.md
    ├── 05-TESTS.md
    └── 06-CONVENTIONS.md
```

## 关键接口签名和数据结构定义

### API 控制器

```csharp
// backend/Controllers/StudentsController.cs
[HttpGet("{studentId:guid}/open-subjects")]
public async Task<IActionResult> GetStudentOpenSubjects(Guid studentId, [FromQuery] bool? activeOnly)

[HttpPut("{studentId:guid}/open-subjects")]
public async Task<IActionResult> SetStudentOpenSubjects(Guid studentId, [FromBody] SetOpenSubjectsRequest request)
```

### 数据模型

```csharp
// backend/Models/OpenSubjectDtos.cs
public record OpenSubjectDto(
    string Id,
    int Subject,
    string OpenStartDate,
    string? OpenEndDate,
    bool IsActive);

public record SetOpenSubjectsRequest(List<SubjectItem> Subjects);

public record SubjectItem(int Subject, string OpenStartDate, string? OpenEndDate);

public record SubjectOption(int Value, string Name, string DisplayName);
```

### 注入的外部依赖

```csharp
// 构造函数签名（StudentsController 共享）
public StudentsController(
    SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
    ILogger<StudentsController> logger)
```

- `StudentManagementGrpcServiceClient`：提供 GetStudentOpenSubjects、SetStudentOpenSubjects 方法。
- `ILogger<StudentsController>`：日志记录器。

### 外部常量依赖

```csharp
// Ruoyu.Study.Common.Constants.SubjectConstants
SubjectConstants.IsValid(int subject)  // 校验科目值是否有效
SubjectConstants.DateFormat            // 日期格式字符串[推断]
```

## 数据流描述（步骤序列）

### 查询学生开放科目 (GetStudentOpenSubjects)

1. 接收 studentId（Guid 路由参数）和 activeOnly（bool? 查询参数，默认 false）。
2. 构建 gRPC `GetStudentOpenSubjectsRequest`，设置 `StudentId` 和 `ActiveOnly`。
3. 调用 `_grpcClient.GetStudentOpenSubjectsAsync(request)`。
4. 将 gRPC 响应中的 Subjects 映射为 `List<OpenSubjectDto>`：
   - `Id` ← `s.Id`
   - `Subject` ← `s.Subject`
   - `OpenStartDate` ← `s.OpenStartDate`
   - `OpenEndDate` ← `s.OpenEndDate`（空字符串时转为 null）
   - `IsActive` ← `s.IsActive`
5. 返回 200 `List<OpenSubjectDto>`。

### 设置学生开放科目 (SetStudentOpenSubjects)

1. 接收 studentId（Guid 路由参数）和 `SetOpenSubjectsRequest` body。
2. 构建 gRPC `SetOpenSubjectsRequest`，设置 `StudentId`。
3. 若 `request.Subjects` 非空且非空列表，遍历每个 SubjectItem：
   a. 校验科目有效性：`SubjectConstants.IsValid(subject.Subject)`，无效返回 400。
   b. 校验 OpenStartDate 非空：`string.IsNullOrEmpty(subject.OpenStartDate)`，为空返回 400。
   c. 校验 OpenStartDate 格式：`DateOnly.TryParse(subject.OpenStartDate, out _)`，非法返回 400。
   d. 校验 OpenEndDate 格式（若非空）：`DateOnly.TryParse(subject.OpenEndDate, out var parsed)`，非法返回 400。
   e. 构建 gRPC `OpenSubjectItem`，OpenEndDate 使用 `SubjectConstants.DateFormat` 格式化（若非空）或空字符串。
   f. 添加到 gRPC 请求的 Subjects 列表。
4. 调用 `_grpcClient.SetStudentOpenSubjectsAsync(grpcRequest)`。
5. 若 gRPC 返回 `Success=false` → 返回 400 `ErrorResponse(ErrorMessage)`。
6. 捕获 `RpcException(InvalidArgument)` → 返回 400。
7. 返回 `OperationResponse(true, "Open subjects updated successfully.")`。

## 错误处理策略

- **参数校验**：在 Controller 层逐项校验每个 SubjectItem，校验失败立即返回 400 `ErrorResponse`。
- **gRPC 错误映射**：
  - `RpcException(StatusCode.InvalidArgument)` → 400 Bad Request
  - gRPC 返回 `Success=false` → 400 Bad Request（注意：与 CRUD 操作返回 404 不同）
- **GetStudentOpenSubjects**：无显式 try-catch，gRPC 异常将向上传播[推断]。

## 依赖的外部模块接口

| 接口 | 提供能力 | 所在模块 |
| --- | --- | --- |
| `StudentManagementGrpcServiceClient` | GetStudentOpenSubjects, SetStudentOpenSubjects | `Ruoyu.Study.Student.Contract.Protos` |
| `SubjectConstants` | IsValid(int), DateFormat | `Ruoyu.Study.Common.Constants` |

## 可测试性设计

- **依赖注入接口化**：`StudentManagementGrpcServiceClient` 通过构造函数注入，可被 Moq 替换[推断]。
- **校验逻辑可间接测试**：`SubjectConstants.IsValid` 为外部静态方法，通过 Controller 公共 API 间接测试。
- **日期解析可测试**：`DateOnly.TryParse` 为 .NET 标准库方法，传入各种格式字符串可验证校验行为。
- **失败路径可触发**：Mock gRPC Client 抛出 `RpcException` 或返回 `Success=false` 可模拟各种错误场景。
