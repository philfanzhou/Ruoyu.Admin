# IdentityProxy 数据模型

## 配置数据

### IdentityServiceOptions

中间件所需的配置选项，通过依赖注入获取。

| 属性 | 类型 | 说明 |
|------|------|------|
| Address | string | IdentityService 的基础地址（如 `http://identity-service:8080`） |
| AppId | string | 管理后台在身份服务中注册的应用ID |
| AppSecret | string | 管理后台在身份服务中注册的应用密钥 |

## 请求头

中间件在转发请求时注入以下请求头：

| 请求头 | 来源 | 说明 |
|--------|------|------|
| X-Admin-AppId | IdentityServiceOptions.AppId | 标识管理后台应用身份 |
| X-Admin-AppSecret | IdentityServiceOptions.AppSecret | 管理后台应用认证密钥 |

## 响应状态码

| 状态码 | 场景 |
|--------|------|
| 2xx | 成功转发并收到身份服务正常响应 |
| 502 | IdentityService 不可达 |

## 无持久化数据

IdentityProxyMiddleware 本身不涉及数据库操作，所有数据由下游 IdentityService 处理。IdentityAccountsController 的数据模型[待确认]。
