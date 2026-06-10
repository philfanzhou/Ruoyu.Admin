# StudentCRUD — 测试计划 (TESTS)

测试工具：`xUnit + Moq + FluentAssertions`。现有模型测试文件：`test/Admin.WebApi.Tests/Models/ModelTests.cs`。

## 现有测试

### 模型构造函数测试（已实现）

| 测试类 | 测试方法 | 验证内容 |
| --- | --- | --- |
| `StudentDtoTests` | `Constructor_ShouldSetPropertiesCorrectly` | StudentDto 所有属性正确赋值 |
| `StudentDtoTests` | `Constructor_WithEmptyAccountIds_ShouldAcceptEmptyList` | IdentityAccountIds 为空列表时正常工作 |
| `CreateStudentRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | CreateStudentRequest 属性正确赋值 |
| `UpdateStudentRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | UpdateStudentRequest 属性正确赋值 |
| `PagedResponseTests` | `Constructor_ShouldSetPropertiesCorrectly` | PagedResponse 泛型属性正确赋值 |
| `OperationResponseTests` | `Constructor_ShouldSetPropertiesCorrectly` | OperationResponse 属性正确赋值 |
| `OperationResponseTests` | `Constructor_WithFailure_ShouldSetSuccessFalse` | Success=false 时正确设置 |
| `ErrorResponseTests` | `Constructor_ShouldSetMessageCorrectly` | ErrorResponse.Message 正确赋值 |

## 缺失测试

**StudentsController 无任何 Controller 级别测试。** 以下为需要补充的测试计划。

## 单元测试 — Given-When-Then 格式

### UT-01 查询学生列表成功（验证 SPEC FR-01）

- **Given**：Mock `_grpcClient.ListStudentsAsync` 返回包含 2 个学生的分页响应。
- **When**：调用 `ListStudents(name: null, grade: null, page: 1, pageSize: 20)`。
- **Then**：返回 200，body 为 `PagedResponse<StudentDto>`，Items.Count == 2，Total/Page/PageSize 正确。

### UT-02 查询学生列表 — 分页参数规范化（验证 SPEC FR-10）

- **Given**：Mock `_grpcClient.ListStudentsAsync` 返回空列表。
- **When**：调用 `ListStudents(page: 0, pageSize: 200)`。
- **Then**：gRPC 请求中 Page == 1，PageSize == 100。

### UT-03 查询学生列表 — 按姓名和年级筛选（验证 SPEC FR-01）

- **Given**：Mock `_grpcClient.ListStudentsAsync` 返回匹配结果。
- **When**：调用 `ListStudents(name: "张三", grade: 7)`。
- **Then**：gRPC 请求中 Name == "张三"，Grade == Grade.Middle1。

### UT-04 获取学生详情成功（验证 SPEC FR-02）

- **Given**：Mock `_grpcClient.GetStudentAsync` 返回有效 StudentDto。
- **When**：调用 `GetStudent(validGuid)`。
- **Then**：返回 200，body 为 `StudentDto`。

### UT-05 获取学生详情 — 不存在（验证 SPEC FR-12）

- **Given**：Mock `_grpcClient.GetStudentAsync` 抛出 `RpcException(StatusCode.NotFound)`。
- **When**：调用 `GetStudent(unknownGuid)`。
- **Then**：返回 404，body 为 `ErrorResponse`。

### UT-06 创建学生成功（验证 SPEC FR-03）

- **Given**：Mock `_grpcClient.CreateStudentAsync` 返回有效 StudentDto。
- **When**：调用 `CreateStudent(new CreateStudentRequest("张三", 7, accountIds))`。
- **Then**：返回 200，body 为 `StudentDto`。

### UT-07 创建学生 — 姓名为空（验证 SPEC FR-03）

- **Given**：请求 Name 为空字符串。
- **When**：调用 `CreateStudent(new CreateStudentRequest("", 7, accountIds))`。
- **Then**：返回 400，body 为 `ErrorResponse("Name is required.")`。

### UT-08 创建学生 — 无效年级（验证 SPEC FR-03）

- **Given**：请求 Grade = 0。
- **When**：调用 `CreateStudent(new CreateStudentRequest("张三", 0, accountIds))`。
- **Then**：返回 400，body 为 `ErrorResponse("Invalid grade value.")`。

### UT-09 创建学生 — 空 IdentityAccountIds（验证 SPEC FR-03）

- **Given**：请求 IdentityAccountIds 为空列表。
- **When**：调用 `CreateStudent(new CreateStudentRequest("张三", 7, []))`。
- **Then**：返回 400，body 为 `ErrorResponse("At least one Identity Account ID is required.")`。

### UT-10 创建学生 — 非法 GUID 格式（验证 SPEC FR-08）

- **Given**：请求 IdentityAccountIds 包含 "not-a-guid"。
- **When**：调用 `CreateStudent`。
- **Then**：返回 400，错误消息包含 "not-a-guid"。

### UT-11 更新学生成功（验证 SPEC FR-04）

- **Given**：Mock `_grpcClient.UpdateStudentAsync` 返回 `Success=true`。
- **When**：调用 `UpdateStudent(validGuid, request)`。
- **Then**：返回 200，body 为 `OperationResponse(true, "Student updated successfully.")`。

### UT-12 更新学生 — gRPC 返回 Success=false（验证 SPEC FR-13）

- **Given**：Mock `_grpcClient.UpdateStudentAsync` 返回 `Success=false, ErrorMessage="Student not found"`。
- **When**：调用 `UpdateStudent(unknownGuid, request)`。
- **Then**：返回 404，body 为 `ErrorResponse("Student not found")`。

### UT-13 删除学生成功（验证 SPEC FR-05）

- **Given**：Mock `_grpcClient.DeleteStudentAsync` 返回 `Success=true`。
- **When**：调用 `DeleteStudent(validGuid)`。
- **Then**：返回 200，body 为 `OperationResponse(true, "Student deleted.")`。

### UT-14 删除学生 — 不存在（验证 SPEC FR-13）

- **Given**：Mock `_grpcClient.DeleteStudentAsync` 返回 `Success=false, ErrorMessage="Student not found"`。
- **When**：调用 `DeleteStudent(unknownGuid)`。
- **Then**：返回 404，body 为 `ErrorResponse("Student not found")`。

### UT-15 获取年级选项（验证 SPEC FR-06）

- **Given**：无外部依赖。
- **When**：调用 `GetGrades()`。
- **Then**：返回 200，body 为 12 个 `GradeOption`，第一个 Value=1 Label="小学一年级"，最后一个 Value=12 Label="高中三年级"。

### UT-16 获取科目选项（验证 SPEC FR-07）

- **Given**：Mock `_grpcClient.GetAvailableSubjectsAsync` 返回科目列表。
- **When**：调用 `GetSubjectOptions()`。
- **Then**：返回 200，body 为 `List<SubjectOption>`，每个选项含 Value、Name、DisplayName。

## 边界和异常测试

- EX-01 page=-1 时修正为 1。
- EX-02 pageSize=0 时修正为 1。
- EX-03 pageSize=200 时修正为 100。
- EX-04 姓名仅含空格 → 返回 400（`IsNullOrWhiteSpace` 校验）。
- EX-05 年级=13 → 返回 400。
- EX-06 多个 IdentityAccountIds 中部分非法 → 返回 400 并列出所有非法 ID。
- EX-07 更新时 IdentityAccountIds=null → 跳过校验，不向 gRPC 请求添加。
- EX-08 gRPC 抛出 `RpcException(StatusCode.InvalidArgument)` → 返回 400。

## 现有测试映射（到 SPEC 功能要求）

| 测试方法 | 验证 SPEC 项 |
| --- | --- |
| `StudentDtoTests.Constructor_ShouldSetPropertiesCorrectly` | FR-01/FR-02/FR-03（模型层） |
| `CreateStudentRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-03（模型层） |
| `UpdateStudentRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04（模型层） |
| `PagedResponseTests.Constructor_ShouldSetPropertiesCorrectly` | FR-01（模型层） |
| `OperationResponseTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04/FR-05（模型层） |
| `ErrorResponseTests.Constructor_ShouldSetMessageCorrectly` | FR-11/FR-12/FR-13（模型层） |
