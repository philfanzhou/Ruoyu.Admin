# IdentityProxy 任务清单

```json
[
  {
    "id": "T1",
    "title": "代理身份服务请求",
    "description": "管理员通过 /api/identity/* 路径访问身份服务功能，中间件自动重写路径并转发到IdentityService，附加X-Admin-AppId和X-Admin-AppSecret认证头",
    "endpoint": "/api/identity/*",
    "method": "*"
  },
  {
    "id": "T2",
    "title": "处理身份服务不可用",
    "description": "当IdentityService不可达时，中间件返回502 Bad Gateway响应",
    "endpoint": "/api/identity/*",
    "method": "*"
  },
  {
    "id": "T3",
    "title": "管理用户账户",
    "description": "管理员通过IdentityAccountsController管理用户账户[待确认具体功能]",
    "endpoint": "[待确认]",
    "method": "[待确认]"
  }
]
```
