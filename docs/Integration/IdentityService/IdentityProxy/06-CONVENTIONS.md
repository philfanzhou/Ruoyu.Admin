# IdentityProxy 约定与模式

## 中间件模式

### 请求拦截与转发

IdentityProxyMiddleware 采用反向代理模式：
- 拦截特定路径前缀的请求
- 重写路径后转发到下游服务
- 自动注入认证信息
- 透传响应给客户端

### 路径重写约定

- 前缀替换：`/api/identity` → `/api`
- 保留原始路径的其余部分
- 保留查询参数和请求体

### 认证头约定

使用自定义请求头传递应用级认证信息：
- `X-Admin-AppId`：标识管理后台应用
- `X-Admin-AppSecret`：应用密钥

前缀 `X-Admin-` 表示这是管理后台级别的认证信息[推断]。

## 错误处理约定

| 场景 | HTTP状态码 | 说明 |
|------|-----------|------|
| 下游服务不可达 | 502 Bad Gateway | 标准网关错误 |

## 配置约定

### Options 模式

使用 ASP.NET Core 的 Options 模式：
- `IdentityServiceOptions` 通过依赖注入获取
- 配置项从 appsettings.json 或环境变量加载

### 配置结构

```json
{
  "IdentityService": {
    "Address": "http://identity-service:8080",
    "AppId": "admin-portal",
    "AppSecret": "***"
  }
}
```

## 安全约定

- AppSecret 属于敏感配置，不应记录到日志中
- 代理中间件不应对请求体内容进行解析或记录[推断]
- 所有到 IdentityService 的通信应使用 HTTPS[推断]
