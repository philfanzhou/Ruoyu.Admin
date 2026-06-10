# TeacherPortalProxy 约定与模式

## 中间件模式

### 请求拦截与转发

TeacherPortalProxyMiddleware 采用反向代理模式，与 IdentityProxyMiddleware 模式一致：
- 拦截特定路径前缀的请求
- 重写路径后转发到下游服务
- 自动注入认证信息
- 透传响应给客户端

### 路径重写约定

与 IdentityProxyMiddleware 的简单前缀替换不同，TeacherPortalProxyMiddleware 使用多规则路径重写：

| 规则 | 原始前缀 | 目标前缀 | 说明 |
|------|---------|---------|------|
| admin | `/api/teacher-portal/admin` | `/api/admin` | 管理接口 |
| auth | `/api/teacher-portal/auth` | `/api/auth` | 认证接口 |
| 默认 | `/api/teacher-portal/其他` | `/api/admin` | 兜底路由 |

**设计意图**：教师门户的接口分为管理接口和认证接口两类，非认证路径统一归为管理路径[推断]。

### 认证头约定

使用单一自定义请求头传递认证信息：
- `X-Admin-Key`：管理后台API密钥

与 IdentityProxy 的双头认证（AppId + AppSecret）不同，TeacherPortal 使用单密钥认证模式。

## 错误处理约定

| 场景 | HTTP状态码 | 说明 |
|------|-----------|------|
| 配置未就绪 | 503 Service Unavailable | 服务暂时不可用 |
| 下游服务不可达 | 502 Bad Gateway | 网关错误 |

**与 IdentityProxy 的区别**：TeacherPortal 增加了 503 状态码用于配置缺失场景，表明这是可恢复的服务不可用（配置后即可使用）。

## 配置约定

### Options 模式

使用 ASP.NET Core 的 Options 模式：
- `TeacherPortalOptions` 通过依赖注入获取
- 配置项从 appsettings.json 或环境变量加载

### 配置结构

```json
{
  "TeacherPortal": {
    "Address": "http://teacher-portal:8080",
    "AdminApiKey": "***"
  }
}
```

## 安全约定

- AdminApiKey 属于敏感配置，不应记录到日志中
- 代理中间件不应对请求体内容进行解析或记录[推断]
- 所有到 TeacherPortal 的通信应使用 HTTPS[推断]

## 代理中间件对比

| 特性 | IdentityProxy | TeacherPortalProxy |
|------|--------------|-------------------|
| 拦截路径 | `/api/identity/*` | `/api/teacher-portal/*` |
| 路径重写 | 单一前缀替换 | 多规则 + 默认路由 |
| 认证方式 | AppId + AppSecret（双头） | AdminApiKey（单头） |
| 未配置响应 | [待确认] | 503 |
| 不可达响应 | 502 | 502 |
