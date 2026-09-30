# 错误处理规范

## 异常边界：ServiceMantle Problem Details（#56 起）

未处理异常不再由控制器自行拼接错误文本。JSON 控制器端点（`UseWhen` 分支按「MVC 控制器动作且非 `ImageController`」选定）中逃出的异常由 `UseServiceMantleProblemDetails()` 统一转换为 `application/problem+json`：

```json
{
  "type": "urn:servicemantle:error:downstream.unavailable",
  "title": "A downstream service request failed.",
  "status": 502,
  "correlationId": "32位小写hex，与响应头 x-correlation-id 相同",
  "errorCode": "downstream.unavailable"
}
```

固定规则：

- 字段恰为 `type/title/status/correlationId/errorCode` 五项；异常消息、堆栈、内部异常与 `Data` 永不进入响应。
- 精确映射唯一一条：`HttpRequestException` → `502 downstream.unavailable`（下游失败，固定英文 title）。其余未映射异常（含独立抛出的 `OperationCanceledException`）→ `500 http.internal_server_error` / `An unexpected error occurred.`。
- 调用方请求取消（`RequestAborted`）原样传播，不产生 problem 响应。
- 响应已开始写入后发生的异常：保留已发送的状态与字节，库吞掉异常并写安全日志（既有非保证边界）。
- 端点主动返回的业务成功/失败响应（验证 400/404/409/502 与固定文本 5xx catch）、认证 401/403、代理转发响应、SPA/图片/健康端点不进入该分支。
- 前端归一化：`frontend/src/services/apiBase.ts` 的 `extractApiErrorMessage` 按「业务 `message` → problem+json `title`（仅 content-type 为 `application/problem+json` 时）→ 传输错误消息」取值；`detail` 永不读取。
- 行为变化（破坏性，仅异常路径）：原先把 `ex.Message` 写入响应的 catch 已删除；学生端点原 404/400 下游中转现为 `502 problem+json`；`OssAuditController` 删除失败原 `500 Failed to delete object: <消息>` 现为固定 problem 500；批量 resolve 的单条失败文本固定为「删除失败，已跳过」；`OssUploadRecordController` 的 rotate/legacy-clean 下游失败为固定文本。
- 已知邻近行为（本次不改）：`OssAuditWorker` 仍把异常消息写入运行日志与 `OssAuditRuns.ErrorMessage`（后台审计诊断，仅认证管理员可见，不属控制器响应路径）。

## REST API 错误响应格式

所有 REST API 在返回错误时，必须使用以下 JSON 结构：

```json
{
  "success": false,
  "message": "人类可读的错误描述",
  "errorCode": "MACHINE_READABLE_CODE",
  "details": {}
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `success` | boolean | 是 | 固定为 `false`，表示请求失败 |
| `message` | string | 是 | 人类可读的错误描述，面向终端用户，使用中文 |
| `errorCode` | string | 是 | 机器可读的错误代码，大写下划线格式，供前端逻辑判断 |
| `details` | object | 否 | 附加错误详情，如验证错误的字段列表 |

成功响应也应包含 `success` 字段：

```json
{
  "success": true,
  "data": {},
  "message": ""
}
```

分页响应：

```json
{
  "success": true,
  "data": [],
  "total": 100,
  "page": 1,
  "pageSize": 10,
  "totalPages": 10
}
```

## HTTP 状态码规范

| 状态码 | 含义 | 使用场景 |
|--------|------|----------|
| `200` | 成功 | 请求成功处理 |
| `400` | 请求无效 | 参数验证失败、格式错误 |
| `401` | 未认证 | 未提供或无效的认证凭据 |
| `403` | 禁止访问 | 已认证但无权限 |
| `404` | 资源不存在 | 请求的资源未找到 |
| `409` | 冲突 | 资源状态冲突 |
| `422` | 无法处理 | 业务规则验证失败 |
| `429` | 请求过多 | 频率限制 |
| `500` | 服务器内部错误 | 未预期的服务端异常 |
| `502` | 网关错误 | 下游 gRPC 服务不可用 |

## gRPC 异常到 HTTP 状态码映射

REST API 控制器在捕获 `RpcException` 时，必须根据 gRPC 状态码映射为对应的 HTTP 状态码：

| gRPC StatusCode | HTTP Status Code | 说明 |
|-----------------|------------------|------|
| `InvalidArgument` | `400` | 参数验证失败 |
| `Unauthenticated` | `401` | 认证失败 |
| `PermissionDenied` | `403` | 权限不足 |
| `NotFound` | `404` | 资源不存在 |
| `AlreadyExists` | `409` | 资源已存在 |
| `FailedPrecondition` | `422` | 业务前置条件不满足 |
| `ResourceExhausted` | `429` | 资源耗尽/限流 |
| `Internal` | `500` | 服务内部错误 |
| `Unavailable` | `502` | 服务不可用 |
| 其他 | `500` | 未知错误 |

## 禁止的做法

1. 禁止将业务错误伪装为 200 + success:false，应返回正确的 4xx 状态码
2. 禁止将所有 gRPC 异常统一返回 502，应根据 gRPC 状态码区分映射
3. 禁止在错误响应中暴露内部实现细节（数据库异常、堆栈跟踪等）

## 日志规范

- 使用结构化日志占位符，不要使用字符串插值
- 异常对象必须传入：使用 `LogError(ex, ...)` 而非 `LogError(ex.Message, ...)`
- RpcException 应记录状态码和详情
- 预期内的 NotFound 使用 Warning 级别
