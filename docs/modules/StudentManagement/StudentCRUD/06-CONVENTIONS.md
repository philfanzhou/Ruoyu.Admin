# StudentCRUD — 约定与规范 (CONVENTIONS)

## 命名约定

- **命名空间**：`Admin.WebApi.Controllers`、`Admin.WebApi.Models`。
- **类名**：Controller 采用 `XxxController` 后缀；DTO/Request 采用 `XxxDto`/`XxxRequest` 后缀；响应采用 `XxxResponse` 后缀；选项采用 `XxxOption` 后缀。
- **record 类型**：所有 Model 均使用 `record`（不可变），部分使用 `sealed record`。
- **方法名**：Controller 动作使用 PascalCase，与 HTTP 动词语义对应（如 `ListStudents`、`GetStudent`、`CreateStudent`、`UpdateStudent`、`DeleteStudent`）。
- **私有字段**：采用 `_camelCase` 下划线前缀（如 `_grpcClient`、`_logger`）。
- **私有静态字段**：采用 `PascalCase`（如 `GradeLabels`）。
- **私有静态方法**：采用 `PascalCase`（如 `ToDto`、`IsValidGuid`、`IsValidGrade`）。
- **路由**：Controller 级别 `[Route("api/admin/students")]`，动作级别使用路由片段（如 `"grades"`、`"{studentId:guid}"`）。

## JSON 序列化约定

- **属性命名**：ASP.NET Core 默认 camelCase 序列化策略，C# record 的 PascalCase 属性在 JSON 中自动转为 camelCase。
  - `StudentDto.Name` → JSON `"name"`
  - `StudentDto.IdentityAccountIds` → JSON `"identityAccountIds"`
  - `PagedResponse.Total` → JSON `"total"`
  - `GradeOption.Value` → JSON `"value"`
  - `SubjectOption.DisplayName` → JSON `"displayName"`
- **时间戳格式**：`CreatedAt`/`UpdatedAt` 使用 `long`（Unix 时间戳秒级）。

## 日志和安全要求

- **禁止记录敏感信息**：不得在日志中记录完整的身份账户 ID 列表；StudentId 仅在错误上下文中记录。
- **日志级别**：
  - `Information`：关键操作成功（当前代码未显式记录 Information 日志）[推断]。
  - `Warning`：非致命错误。
  - `Error`：gRPC 调用失败。
- **错误消息**：对外返回的错误消息为英文简短描述，不包含内部异常堆栈或 gRPC 细节。

## 错误消息格式约定

| 场景 | 消息文本（英文） |
| --- | --- |
| 姓名为空 | `"Name is required."` |
| 无效年级 | `"Invalid grade value."` |
| 无身份账户 ID（创建时） | `"At least one Identity Account ID is required."` |
| 非法 GUID 格式 | `"Invalid Identity Account ID format: {ids}"` 或 `"Invalid Identity Account ID format. Must be a valid GUID."` |
| gRPC InvalidArgument | 使用 gRPC 返回的 `Status.Detail` |
| gRPC NotFound | 使用 gRPC 返回的 `Status.Detail` |
| 更新成功 | `"Student updated successfully."` |
| 删除成功 | `"Student deleted."` |

## 测试工具要求

- **框架**：`xUnit` 2.x。
- **Mock**：`Moq` 4.x；gRPC Client 必须通过 Mock 替换。
- **断言风格**：现有模型测试使用 `FluentAssertions`（`Should().Be()`）；Controller 测试建议保持一致。
- **异步**：测试方法必须为 `async Task`，使用 `await` 调用被测代码；禁止 `Task.Wait()` / `.Result`。
- **测试命名**：`Constructor_ShouldSetPropertiesCorrectly`（模型层）；Controller 层建议使用 `[方法名]_[场景]_[预期行为]`。

## 代码风格

- **文件作用域命名空间**：`namespace Admin.WebApi.Controllers;`（分号结尾）。
- **record 定义**：单行主构造函数 `public sealed record StudentDto(string Id, string Name, ...)`。
- **参数校验**：在 Controller 动作方法开头进行前置校验，校验失败立即返回 `BadRequest(new ErrorResponse(...))`。
- **gRPC 错误处理**：使用 `try-catch` + `when` 子句过滤 `RpcException` 的 `StatusCode`。
- **DTO 转换**：使用 `private static` 方法（如 `ToDto`）将 gRPC 响应映射为 API 模型。
- **路由约束**：GUID 路由参数使用 `{studentId:guid}` 约束。
- **HTTP 方法**：`[HttpGet]`、`[HttpPost]`、`[HttpPut]`、`[HttpDelete]` 显式标注。
- **参数来源**：`[FromQuery]` 用于查询参数，`[FromBody]` 用于请求体。
