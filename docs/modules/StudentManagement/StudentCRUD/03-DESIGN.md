# StudentCRUD — 设计说明 (DESIGN)

## 本功能在项目中的目录与文件结构

```
src/admin_portal/
├── backend/
│   ├── Controllers/
│   │   └── StudentsController.cs          # API 控制器（CRUD + GetGrades + GetSubjectOptions）
│   └── Models/
│       ├── StudentDto.cs                   # 学生 DTO（record）
│       ├── CreateStudentRequest.cs         # 创建请求（record）
│       ├── UpdateStudentRequest.cs         # 更新请求（record）
│       ├── GradeOption.cs                  # 年级选项（record）
│       ├── SubjectOption.cs                # 科目选项（record）[推断，定义于 OpenSubjectDtos.cs]
│       ├── PagedResponse.cs               # 分页响应泛型（record）
│       ├── OperationResponse.cs           # 操作结果响应（record）
│       └── ErrorResponse.cs               # 错误响应（record）
├── test/
│   └── Admin.WebApi.Tests/
│       └── Models/
│           └── ModelTests.cs              # 模型构造函数测试
└── docs/modules/StudentManagement/StudentCRUD/
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
[Route("api/admin/students")]
[ApiController]
public class StudentsController : ControllerBase
{
    public StudentsController(
        StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
        ILogger<StudentsController> logger)

    [HttpGet]
    public async Task<IActionResult> ListStudents(string? name, int? grade, int? page, int? pageSize)

    [HttpGet("grades")]
    public IActionResult GetGrades()

    [HttpGet("{studentId:guid}")]
    public async Task<IActionResult> GetStudent(Guid studentId)

    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)

    [HttpPut("{studentId:guid}")]
    public async Task<IActionResult> UpdateStudent(Guid studentId, [FromBody] UpdateStudentRequest request)

    [HttpDelete("{studentId:guid}")]
    public async Task<IActionResult> DeleteStudent(Guid studentId)

    [HttpGet("subject-options")]
    public async Task<IActionResult> GetSubjectOptions()
}
```

### 数据模型

```csharp
// backend/Models/StudentDto.cs
public sealed record StudentDto(
    string Id,
    string Name,
    int Grade,
    IReadOnlyList<string> IdentityAccountIds,
    long CreatedAt,
    long UpdatedAt);

// backend/Models/CreateStudentRequest.cs
public sealed record CreateStudentRequest(string Name, int Grade, List<string> IdentityAccountIds);

// backend/Models/UpdateStudentRequest.cs
public sealed record UpdateStudentRequest(string Name, int Grade, List<string>? IdentityAccountIds);

// backend/Models/GradeOption.cs
public sealed record GradeOption(int Value, string Label);

// backend/Models/PagedResponse.cs
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// backend/Models/OperationResponse.cs
public sealed record OperationResponse(bool Success, string Message);

// backend/Models/ErrorResponse.cs
public sealed record ErrorResponse(string Message);
```

### 年级静态映射

```csharp
private static readonly Dictionary<int, string> GradeLabels = new()
{
    [1] = "小学一年级",  [2] = "小学二年级",  [3] = "小学三年级",
    [4] = "小学四年级",  [5] = "小学五年级",  [6] = "小学六年级",
    [7] = "初中一年级",  [8] = "初中二年级",  [9] = "初中三年级",
    [10] = "高中一年级", [11] = "高中二年级", [12] = "高中三年级",
};
```

### 注入的外部依赖

```csharp
// 构造函数签名
public StudentsController(
    SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
    ILogger<StudentsController> logger)
```

- `StudentManagementGrpcServiceClient`：Student 服务的 gRPC 客户端，提供 ListStudents、GetStudent、CreateStudent、UpdateStudent、DeleteStudent、GetAvailableSubjects 方法。
- `ILogger<StudentsController>`：日志记录器。

## 数据流描述（步骤序列）

### 查询学生列表 (ListStudents)

1. 接收查询参数 name、grade、page、pageSize。
2. 规范化分页参数：page < 1 时修正为 1，pageSize 使用 `Math.Clamp(value, 1, 100)`。
3. 构建 gRPC `ListStudentsRequest`：grade > 0 时转为 Grade 枚举，否则为 Unspecified。
4. 调用 `_grpcClient.ListStudentsAsync(request)`。
5. 将 gRPC 响应中的每个 Item 通过 `ToDto` 转换为 `StudentDto`。
6. 返回 `PagedResponse<StudentDto>`。

### 获取学生详情 (GetStudent)

1. 接收 studentId（Guid 路由参数）。
2. 构建 gRPC `GetStudentRequest`。
3. 调用 `_grpcClient.GetStudentAsync(request)`。
4. 捕获 `RpcException(NotFound)` → 返回 404；捕获 `RpcException(InvalidArgument)` → 返回 400。
5. 将 gRPC 响应通过 `ToDto` 转换并返回 200。

### 创建学生 (CreateStudent)

1. 接收 `CreateStudentRequest` body。
2. 校验：姓名非空白、年级有效（1-12）、IdentityAccountIds 非空、所有 ID 为合法 GUID。
3. 校验失败返回 400 `ErrorResponse`。
4. 构建 gRPC `CreateStudentRequest`，姓名 Trim 后传入。
5. 调用 `_grpcClient.CreateStudentAsync(grpcRequest)`。
6. 捕获 `RpcException(InvalidArgument)` → 返回 400。
7. 将 gRPC 响应通过 `ToDto` 转换并返回 200。

### 更新学生 (UpdateStudent)

1. 接收 studentId（Guid 路由参数）和 `UpdateStudentRequest` body。
2. 校验：姓名非空白、年级有效、若提供 IdentityAccountIds 则每个 ID 为合法 GUID。
3. 构建 gRPC `UpdateStudentRequest`，若 IdentityAccountIds 非空则 AddRange。
4. 调用 `_grpcClient.UpdateStudentAsync(grpcRequest)`。
5. 若 gRPC 返回 `Success=false` → 返回 404 `ErrorResponse(ErrorMessage)`。
6. 捕获 `RpcException(InvalidArgument)` → 返回 400。
7. 返回 `OperationResponse(true, "Student updated successfully.")`。

### 删除学生 (DeleteStudent)

1. 接收 studentId（Guid 路由参数）。
2. 构建 gRPC `DeleteStudentRequest`。
3. 调用 `_grpcClient.DeleteStudentAsync(request)`。
4. 若 gRPC 返回 `Success=false` → 返回 404 `ErrorResponse(ErrorMessage)`。
5. 捕获 `RpcException(InvalidArgument)` → 返回 400。
6. 返回 `OperationResponse(true, "Student deleted.")`。

### 获取年级选项 (GetGrades)

1. 从静态字典 `GradeLabels` 生成 `List<GradeOption>`。
2. 按 Value 排序后返回 200。

### 获取科目选项 (GetSubjectOptions)

1. 构建 gRPC `Empty` 请求。
2. 调用 `_grpcClient.GetAvailableSubjectsAsync(request)`。
3. 将 gRPC 响应中的 Subjects 映射为 `List<SubjectOption>`。
4. 返回 200。

## 错误处理策略

- **参数校验**：在 Controller 层前置判空与格式检查，无效参数直接返回 400 `ErrorResponse`。
- **gRPC 错误映射**：
  - `RpcException(StatusCode.InvalidArgument)` → 400 Bad Request
  - `RpcException(StatusCode.NotFound)` → 404 Not Found
- **gRPC 业务失败**：Update/Delete 操作 gRPC 返回 `Success=false` 时，使用 `ErrorMessage` 返回 404。
- **未捕获的 gRPC 异常**：未被 `when` 子句过滤的 `RpcException` 将向上传播，由 ASP.NET Core 中间件处理[推断]。

## 依赖的外部模块接口

| 接口 | 提供能力 | 所在模块 |
| --- | --- | --- |
| `StudentManagementGrpcServiceClient` | ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, GetAvailableSubjects | `Ruoyu.Study.Student.Contract.Protos` |

## 可测试性设计

- **依赖注入接口化**：`StudentManagementGrpcServiceClient` 通过构造函数注入，可被 Moq 替换[推断，需验证 gRPC Client 是否可 Mock]。
- **静态方法可测试**：`ToDto`、`IsValidGuid`、`IsValidGrade` 为 private static 方法，可通过公共 API 间接测试。
- **失败路径可触发**：Mock gRPC Client 抛出 `RpcException` 可模拟各种错误场景。
