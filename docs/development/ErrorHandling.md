# Error handling specification

## Exception boundary: ServiceMantle Problem Details (since #56)

Unhandled exceptions are no longer turned into error text by the controllers themselves. Exceptions escaping JSON controller endpoints (the `UseWhen` branch selects "MVC controller actions other than `ImageController`") are uniformly converted to `application/problem+json` by `UseServiceMantleProblemDetails()`:

```json
{
  "type": "urn:servicemantle:error:downstream.unavailable",
  "title": "A downstream service request failed.",
  "status": 502,
  "correlationId": "32-char lowercase hex, identical to the response header x-correlation-id",
  "errorCode": "downstream.unavailable"
}
```

Fixed rules:

- The fields are exactly the five `type/title/status/correlationId/errorCode`; exception messages, stacks, inner exceptions, and `Data` never enter the response.
- Exactly one precise mapping exists: `HttpRequestException` → `502 downstream.unavailable` (downstream failure, fixed English title). All other unmapped exceptions (including a standalone `OperationCanceledException`) → `500 http.internal_server_error` / `An unexpected error occurred.`.
- Caller cancellation (`RequestAborted`) propagates as-is and produces no problem response.
- Exceptions after the response has started writing: the already-sent status and bytes are kept, and the library swallows the exception and writes a safe log (an existing non-guaranteed boundary).
- Business success/failure responses actively returned by endpoints (validation 400/404/409/502 and fixed-text 5xx catches), authentication 401/403, proxied responses, SPA/images/health endpoints do not enter this branch.
- Frontend normalization: `extractApiErrorMessage` in `frontend/src/services/apiBase.ts` takes the value in the order "business `message` → problem+json `title` (only when the content type is `application/problem+json`) → transport error message"; `detail` is never read.
- Behavior changes (breaking, exception paths only): the catches that used to write `ex.Message` into the response are deleted; the former 404/400 downstream pass-through on student endpoints is now `502 problem+json`; `OssAuditController` delete failures, formerly `500 Failed to delete object: <message>`, are now a fixed problem 500; the single-item failure text of batch resolve is fixed as "Deletion failed, skipped"; rotate/legacy-clean downstream failures of `OssUploadRecordController` use fixed text.
- Known adjacent behavior (unchanged here): `OssAuditWorker` still writes exception messages into the run log and `OssAuditRuns.ErrorMessage` (background audit diagnostics, visible only to authenticated administrators; not a controller response path).

## REST API error response format

Every REST API must use the following JSON structure when returning an error:

```json
{
  "success": false,
  "message": "human-readable error description",
  "errorCode": "MACHINE_READABLE_CODE",
  "details": {}
}
```

| Field | Type | Required | Description |
|------|------|------|------|
| `success` | boolean | yes | Fixed `false`, indicating the request failed |
| `message` | string | yes | Human-readable error description aimed at end users, in Chinese |
| `errorCode` | string | yes | Machine-readable error code in uppercase-underscore form, for frontend logic |
| `details` | object | no | Additional error details, such as a list of fields that failed validation |

Successful responses should also include the `success` field:

```json
{
  "success": true,
  "data": {},
  "message": ""
}
```

Paginated responses:

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

## HTTP status code specification

| Status code | Meaning | Usage |
|--------|------|----------|
| `200` | Success | Request handled successfully |
| `400` | Invalid request | Parameter validation failure, malformed format |
| `401` | Unauthenticated | Missing or invalid authentication credentials |
| `403` | Forbidden | Authenticated but lacking permission |
| `404` | Not found | The requested resource was not found |
| `409` | Conflict | Resource state conflict |
| `422` | Unprocessable | Business rule validation failure |
| `429` | Too many requests | Rate limiting |
| `500` | Internal server error | Unexpected server-side exception |
| `502` | Bad gateway | Downstream gRPC service unavailable |

## Mapping gRPC exceptions to HTTP status codes

When a REST API controller catches an `RpcException`, it must map the gRPC status code to the corresponding HTTP status code:

| gRPC StatusCode | HTTP Status Code | Description |
|-----------------|------------------|------|
| `InvalidArgument` | `400` | Parameter validation failure |
| `Unauthenticated` | `401` | Authentication failure |
| `PermissionDenied` | `403` | Insufficient permission |
| `NotFound` | `404` | Resource not found |
| `AlreadyExists` | `409` | Resource already exists |
| `FailedPrecondition` | `422` | Business precondition not met |
| `ResourceExhausted` | `429` | Resource exhausted / throttled |
| `Internal` | `500` | Internal service error |
| `Unavailable` | `502` | Service unavailable |
| Others | `500` | Unknown error |

## Forbidden practices

1. Never disguise a business error as 200 + success:false; return the correct 4xx status code
2. Never map every gRPC exception to 502; map by the gRPC status code
3. Never expose internal implementation details in error responses (database exceptions, stack traces, etc.)

## Logging conventions

- Use structured logging placeholders, not string interpolation
- Always pass the exception object: use `LogError(ex, ...)` instead of `LogError(ex.Message, ...)`
- RpcExceptions should log the status code and details
- Expected NotFound cases use the Warning level
