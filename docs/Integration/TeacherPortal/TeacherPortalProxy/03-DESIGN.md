# TeacherPortalProxy 数据模型

## 配置数据

### TeacherPortalOptions

中间件所需的配置选项，通过依赖注入获取。

| 属性 | 类型 | 说明 |
|------|------|------|
| Address | string | TeacherPortal 的基础地址（如 `http://teacher-portal:8080`） |
| AdminApiKey | string | 管理后台的API密钥，用于教师门户认证 |

## 请求头

中间件在转发请求时注入以下请求头：

| 请求头 | 来源 | 说明 |
|--------|------|------|
| X-Admin-Key | TeacherPortalOptions.AdminApiKey | 管理后台API密钥，用于教师门户认证 |

## 响应状态码

| 状态码 | 场景 |
|--------|------|
| 2xx | 成功转发并收到教师门户正常响应 |
| 502 | TeacherPortal 不可达 |
| 503 | TeacherPortalOptions 未配置 |

## 无持久化数据

TeacherPortalProxyMiddleware 本身不涉及数据库操作，所有数据由下游 TeacherPortal 处理。
