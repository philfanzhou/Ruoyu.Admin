# OpenSubjectManagement — 测试计划 (TESTS)

测试工具：`xUnit + Moq + FluentAssertions`。现有模型测试文件：`test/Admin.WebApi.Tests/Models/ModelTests.cs`。

## 现有测试

### 模型构造函数测试（已实现）

当前 `ModelTests.cs` 中**无** OpenSubjectDto、SetOpenSubjectsRequest、SubjectItem、SubjectOption 的构造函数测试。以下模型缺少测试：

- `OpenSubjectDto`
- `SetOpenSubjectsRequest`
- `SubjectItem`
- `SubjectOption`

## 已实现测试

### OpenSubjectManagementTests（Controller 测试）

| 测试方法 | 验证内容 |
| --- | --- |
| `GetStudentOpenSubjects_Success` | UT-01 查询学生开放科目成功 |
| `GetStudentOpenSubjects_ActiveOnlyTrue` | UT-02 查询学生开放科目 — activeOnly=true |
| `GetStudentOpenSubjects_EmptyEndDate_MapsToNull` | UT-03 查询学生开放科目 — OpenEndDate 映射 |
| `SetStudentOpenSubjects_Success` | UT-04 设置学生开放科目成功 |
| `SetStudentOpenSubjects_InvalidSubjectValue_Returns400` | UT-05 设置学生开放科目 — 无效科目值 |
| `SetStudentOpenSubjects_EmptyOpenStartDate_Returns400` | UT-06 设置学生开放科目 — 空 OpenStartDate |
| `SetStudentOpenSubjects_InvalidOpenStartDateFormat_Returns400` | UT-07 设置学生开放科目 — 非法 OpenStartDate 格式 |
| `SetStudentOpenSubjects_InvalidOpenEndDateFormat_Returns400` | UT-08 设置学生开放科目 — 非法 OpenEndDate 格式 |
| `SetStudentOpenSubjects_GrpcSuccessFalse_Returns400` | UT-09 设置学生开放科目 — gRPC 返回 Success=false |
| `SetStudentOpenSubjects_GrpcInvalidArgument_Returns400` | UT-10 设置学生开放科目 — gRPC InvalidArgument |
| `SetStudentOpenSubjects_EmptySubjectsList_SkipsValidation_CallsGrpc` | EX-01 Subjects 列表为空 → 跳过校验，直接调用 gRPC |
| `SetStudentOpenSubjects_NullOpenEndDate_SendsEmptyStringInGrpcRequest` | EX-04 OpenEndDate 为 null → gRPC 请求中为空字符串 |

### OpenSubjectModelTests（模型测试）

| 测试方法 | 验证内容 |
| --- | --- |
| `OpenSubjectDto_Constructor_SetsProperties` | OpenSubjectDto 构造函数正确赋值 |
| `OpenSubjectDto_WithNullOpenEndDate_SetsProperty` | OpenSubjectDto OpenEndDate 为 null 时正确设置 |
| `SubjectItem_Constructor_SetsProperties` | SubjectItem 构造函数正确赋值 |
| `SetOpenSubjectsRequest_Constructor_SetsProperties` | SetOpenSubjectsRequest 构造函数正确赋值 |
| `SubjectOption_Constructor_SetsProperties` | SubjectOption 构造函数正确赋值 |

## 单元测试 — Given-When-Then 格式

### 模型测试

#### UT-M01 OpenSubjectDto 构造函数测试

- **Given**：有效的 Id、Subject=1、OpenStartDate="2024-01-01"、OpenEndDate="2024-12-31"、IsActive=true。
- **When**：创建 `OpenSubjectDto`。
- **Then**：所有属性正确赋值。

#### UT-M02 OpenSubjectDto 构造函数 — OpenEndDate 为 null

- **Given**：OpenEndDate=null。
- **When**：创建 `OpenSubjectDto`。
- **Then**：`OpenEndDate` 为 null。

#### UT-M03 SubjectItem 构造函数测试

- **Given**：Subject=1、OpenStartDate="2024-01-01"、OpenEndDate=null。
- **When**：创建 `SubjectItem`。
- **Then**：所有属性正确赋值。

#### UT-M04 SetOpenSubjectsRequest 构造函数测试

- **Given**：Subjects 包含 2 个 SubjectItem。
- **When**：创建 `SetOpenSubjectsRequest`。
- **Then**：`Subjects.Count == 2`。

#### UT-M05 SubjectOption 构造函数测试

- **Given**：Value=1、Name="Math"、DisplayName="数学"。
- **When**：创建 `SubjectOption`。
- **Then**：所有属性正确赋值。

### Controller 测试

#### UT-01 查询学生开放科目成功（验证 SPEC FR-01）

- **Given**：Mock `_grpcClient.GetStudentOpenSubjectsAsync` 返回包含 2 个科目的响应。
- **When**：调用 `GetStudentOpenSubjects(validGuid, activeOnly: null)`。
- **Then**：返回 200，body 为 `List<OpenSubjectDto>`，Count == 2；gRPC 请求中 `ActiveOnly == false`。

#### UT-02 查询学生开放科目 — activeOnly=true（验证 SPEC FR-02）

- **Given**：Mock `_grpcClient.GetStudentOpenSubjectsAsync` 返回包含 1 个活跃科目的响应。
- **When**：调用 `GetStudentOpenSubjects(validGuid, activeOnly: true)`。
- **Then**：返回 200；gRPC 请求中 `ActiveOnly == true`。

#### UT-03 查询学生开放科目 — OpenEndDate 映射（验证 SPEC FR-10）

- **Given**：Mock gRPC 响应中一个科目 OpenEndDate 为空字符串，另一个为 "2024-12-31"。
- **When**：调用 `GetStudentOpenSubjects`。
- **Then**：返回的 DTO 中第一个 OpenEndDate 为 null，第二个为 "2024-12-31"。

#### UT-04 设置学生开放科目成功（验证 SPEC FR-03）

- **Given**：Mock `_grpcClient.SetStudentOpenSubjectsAsync` 返回 `Success=true`。
- **When**：调用 `SetStudentOpenSubjects(validGuid, new SetOpenSubjectsRequest(subjects))`，subjects 包含有效科目。
- **Then**：返回 200，body 为 `OperationResponse(true, "Open subjects updated successfully.")`。

#### UT-05 设置学生开放科目 — 无效科目值（验证 SPEC FR-04）

- **Given**：SubjectItem 中 Subject=-1（无效值）。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Invalid subject value: -1")`。

#### UT-06 设置学生开放科目 — 空 OpenStartDate（验证 SPEC FR-05）

- **Given**：SubjectItem 中 OpenStartDate 为空字符串。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Open start date is required")`。

#### UT-07 设置学生开放科目 — 非法 OpenStartDate 格式（验证 SPEC FR-06）

- **Given**：SubjectItem 中 OpenStartDate 为 "not-a-date"。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Invalid start date format: not-a-date")`。

#### UT-08 设置学生开放科目 — 非法 OpenEndDate 格式（验证 SPEC FR-07）

- **Given**：SubjectItem 中 OpenEndDate 为 "invalid-date"。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Invalid end date format: invalid-date")`。

#### UT-09 设置学生开放科目 — gRPC 返回 Success=false（验证 SPEC FR-08）

- **Given**：Mock `_grpcClient.SetStudentOpenSubjectsAsync` 返回 `Success=false, ErrorMessage="Student not found"`。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Student not found")`。

#### UT-10 设置学生开放科目 — gRPC InvalidArgument（验证 SPEC FR-09）

- **Given**：Mock `_grpcClient.SetStudentOpenSubjectsAsync` 抛出 `RpcException(StatusCode.InvalidArgument, "Duplicate subject")`。
- **When**：调用 `SetStudentOpenSubjects`。
- **Then**：返回 400，body 为 `ErrorResponse("Duplicate subject")`。

## 边界和异常测试

- EX-01 Subjects 列表为空 → 跳过校验循环，直接调用 gRPC。
- EX-02 Subjects 列表为 null → 跳过校验循环，直接调用 gRPC。
- EX-03 Subjects 列表包含多个有效科目 → 全部通过校验，gRPC 请求包含所有科目。
- EX-04 OpenEndDate 为 null → gRPC 请求中 OpenEndDate 为空字符串。
- EX-05 OpenEndDate 为合法日期 → gRPC 请求中 OpenEndDate 使用 SubjectConstants.DateFormat 格式化。
- EX-06 多个 SubjectItem 中第一个无效 → 立即返回 400，不校验后续项。
- EX-07 GetStudentOpenSubjects gRPC 异常 → 未被捕获，向上传播[推断]。

## 现有测试映射（到 SPEC 功能要求）

### Controller 测试

| 测试方法 | 验证 SPEC 项 |
| --- | --- |
| `GetStudentOpenSubjects_Success` | FR-01 |
| `GetStudentOpenSubjects_ActiveOnlyTrue` | FR-02 |
| `GetStudentOpenSubjects_EmptyEndDate_MapsToNull` | FR-10 |
| `SetStudentOpenSubjects_Success` | FR-03 |
| `SetStudentOpenSubjects_InvalidSubjectValue_Returns400` | FR-04 |
| `SetStudentOpenSubjects_EmptyOpenStartDate_Returns400` | FR-05 |
| `SetStudentOpenSubjects_InvalidOpenStartDateFormat_Returns400` | FR-06 |
| `SetStudentOpenSubjects_InvalidOpenEndDateFormat_Returns400` | FR-07 |
| `SetStudentOpenSubjects_GrpcSuccessFalse_Returns400` | FR-08 |
| `SetStudentOpenSubjects_GrpcInvalidArgument_Returns400` | FR-09 |

### 模型测试

| 测试方法 | 验证 SPEC 项 |
| --- | --- |
| `OpenSubjectDto_Constructor_SetsProperties` | FR-01/FR-03（模型层） |
| `OpenSubjectDto_WithNullOpenEndDate_SetsProperty` | FR-10（模型层） |
| `SubjectItem_Constructor_SetsProperties` | FR-03（模型层） |
| `SetOpenSubjectsRequest_Constructor_SetsProperties` | FR-03（模型层） |
| `SubjectOption_Constructor_SetsProperties` | FR-07（模型层） |
