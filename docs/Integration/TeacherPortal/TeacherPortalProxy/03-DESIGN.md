# TeacherPortalProxy 数据模型

## 配置数据

### TeacherPortalOptions

中间件所需的配置选项，通过依赖注入获取。

| 属性 | 类型 | 说明 |
|------|------|------|
| Url | string | TeacherPortal 的基础地址（如 `http://teacher-portal:8080`） |

## 请求头

中间件在转发请求时透传调用方的所有请求头（排除 `Content-*` 和 `Host`）。其中 `Authorization: Bearer` 头会被自动透传给 Teacher Portal，Teacher Portal 通过 `[Authorize(Roles="admin")]` 校验 JWT。不再注入静态的 `X-Admin-Key`。

## 响应状态码

| 状态码 | 场景 |
|--------|------|
| 2xx | 成功转发并收到教师门户正常响应 |
| 502 | TeacherPortal 不可达 |
| 503 | TeacherPortalOptions 未配置 |

## 无持久化数据

TeacherPortalProxyMiddleware 本身不涉及数据库操作，所有数据由下游 TeacherPortal 处理。
