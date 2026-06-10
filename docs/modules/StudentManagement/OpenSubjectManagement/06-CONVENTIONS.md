# OpenSubjectManagement — 约定与规范 (CONVENTIONS)

## 命名约定

- **命名空间**：`Admin.WebApi.Controllers`、`Admin.WebApi.Models`。
- **类名**：DTO 采用 `XxxDto` 后缀；请求采用 `XxxRequest` 后缀；选项采用 `XxxOption` 后缀；子项采用 `XxxItem` 后缀。
- **record 类型**：Model 使用 `record`（不可变）。注意 `OpenSubjectDto`、`SetOpenSubjectsRequest`、`SubjectItem`、`SubjectOption` 为 `public record`（非 sealed），与项目中其他 Model 的 `sealed record` 不同。
- **方法名**：Controller 动作使用 PascalCase（如 `GetStudentOpenSubjects`、`SetStudentOpenSubjects`）。
- **私有字段**：采用 `_camelCase` 下划线前缀（如 `_grpcClient`、`_logger`）。
- **路由**：Controller 级别 `[Route("api/admin/students")]`，动作级别 `"{studentId:guid}/open-subjects"`。

## JSON 序列化约定

- **属性命名**：ASP.NET Core 默认 camelCase 序列化策略。
  - `OpenSubjectDto.OpenStartDate` → JSON `"openStartDate"`
  - `OpenSubjectDto.OpenEndDate` → JSON `"openEndDate"`（null 时 JSON 中为 null 或不出现）
  - `OpenSubjectDto.IsActive` → JSON `"isActive"`
  - `SubjectItem.Subject` → JSON `"subject"`
  - `SetOpenSubjectsRequest.Subjects` → JSON `"subjects"`
  - `SubjectOption.DisplayName` → JSON `"displayName"`
- **日期格式**：OpenStartDate/OpenEndDate 使用 `string` 类型，格式为 DateOnly（如 "2024-01-01"），由 `DateOnly.TryParse` 校验。
- **可空字段**：`OpenEndDate` 为 `string?`，JSON 中 null 表示无限期开放。

## 日志和安全要求

- **禁止记录敏感信息**：StudentId 仅在错误上下文中记录。
- **日志级别**：
  - `Warning`：非致命错误[推断]。
  - `Error`：gRPC 调用严重错误[推断]。
- **错误消息**：对外返回的错误消息为英文简短描述，不包含 gRPC 内部细节。

## 错误消息格式约定

| 场景 | 消息文本（英文） |
| --- | --- |
| 无效科目值 | `"Invalid subject value: {subject}"` |
| OpenStartDate 为空 | `"Open start date is required"` |
| 非法 OpenStartDate 格式 | `"Invalid start date format: {value}"` |
| 非法 OpenEndDate 格式 | `"Invalid end date format: {value}"` |
| gRPC InvalidArgument | 使用 gRPC 返回的 `Status.Detail` |
| gRPC Success=false | 使用 gRPC 返回的 `ErrorMessage` |
| 设置成功 | `"Open subjects updated successfully."` |

> 注：SetStudentOpenSubjects 的 gRPC Success=false 返回 400 Bad Request，与 UpdateStudent/DeleteStudent 返回 404 Not Found 不同。

## 日期处理约定

- **输入校验**：使用 `DateOnly.TryParse` 校验日期字符串格式。
- **输出格式化**：OpenEndDate 使用 `SubjectConstants.DateFormat` 格式化后传入 gRPC 请求。
- **空值处理**：OpenEndDate 为 null 时，gRPC 请求中传入空字符串 `""`。
- **gRPC 响应映射**：gRPC 返回的 OpenEndDate 为空字符串时，映射为 C# `null`。

## 科目校验约定

- **校验方法**：使用 `SubjectConstants.IsValid(int subject)` 静态方法校验科目值有效性。
- **校验时机**：在 Controller 层 SetStudentOpenSubjects 方法中，遍历每个 SubjectItem 时逐一校验。
- **校验失败行为**：立即返回 400，不继续校验后续 SubjectItem。
- **科目值来源**：有效科目值由 `Ruoyu.Study.Common.Constants.SubjectConstants` 定义。

## 测试工具要求

- **框架**：`xUnit` 2.x。
- **Mock**：`Moq` 4.x；gRPC Client 通过 Mock 替换。
- **断言风格**：现有模型测试使用 `FluentAssertions`（`Should().Be()`）；Controller 测试建议保持一致。
- **异步**：测试方法必须为 `async Task`，使用 `await` 调用被测代码。
- **测试命名**：`Constructor_ShouldSetPropertiesCorrectly`（模型层）；Controller 层建议使用 `[方法名]_[场景]_[预期行为]`。
- **SubjectConstants 依赖**：测试中需确认 `SubjectConstants.IsValid` 的行为，可能需要通过集成测试验证[待确认]。

## 代码风格

- **文件作用域命名空间**：`namespace Admin.WebApi.Controllers;`（分号结尾）。
- **record 定义**：OpenSubjectDtos.cs 中多个 record 定义在同一文件中。
- **参数校验**：在 Controller 动作方法中遍历 SubjectItem 列表，逐项校验，校验失败立即返回 `BadRequest(new ErrorResponse(...))`。
- **gRPC 错误处理**：使用 `try-catch` + `when` 子句过滤 `RpcException(StatusCode.InvalidArgument)`。
- **日期解析**：使用 `DateOnly.TryParse` 而非 `DateTime.TryParse`，确保只接受日期部分。
- **空值判断**：OpenEndDate 使用 `string.IsNullOrEmpty` 判断，OpenStartDate 使用 `string.IsNullOrEmpty` 判断。
