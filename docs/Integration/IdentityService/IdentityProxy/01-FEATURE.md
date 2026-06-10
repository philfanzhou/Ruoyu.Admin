# 身份服务代理 (IdentityProxy)

## 功能名称和一句话概括

**IdentityProxy** — 管理后台代理转发身份服务请求并自动注入认证信息。

## 核心用户故事

作为管理员，我希望通过管理后台统一访问身份服务功能且无需手动处理认证，以便简化身份账户的统一管理。

## 补充约束

1. **幂等性**：代理转发为无状态操作，GET 请求天然幂等，POST/PUT/DELETE 遵循下游服务语义
2. **并发**：无并发限制，每个请求独立转发
3. **事务边界**：单次请求-响应，无跨请求事务
4. **失败降级**：身份服务不可达时返回 502 Bad Gateway
5. **路径重写**：`/api/identity` 前缀替换为 `/api`，例如 `/api/identity/users` → `/api/users`
6. **认证注入**：转发请求时自动添加 `X-Admin-AppId` 和 `X-Admin-AppSecret` 请求头
7. **配置依赖**：需配置 IdentityServiceOptions（Address, AppId, AppSecret）

## 关键验收条件摘要

1. 所有 `/api/identity/*` 请求被中间件拦截并转发到身份服务
2. 路径重写正确：`/api/identity` 前缀替换为 `/api`
3. 转发请求自动附加 `X-Admin-AppId` 和 `X-Admin-AppSecret` 请求头
4. 身份服务不可达时返回 502 Bad Gateway
5. 非身份服务路径不受中间件影响

## 范围外

- 身份服务的业务逻辑实现
- 管理员认证/授权流程
- 请求/响应的缓存
- 请求限流与熔断

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
