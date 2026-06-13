# 错误处理规范

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
