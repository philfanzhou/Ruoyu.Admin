# IdentityProxy 测试用例

## 当前状态：[待确认]

以下为基于中间件行为的建议测试用例。

---

## IdentityProxyMiddleware 测试

```gherkin
Feature: 身份服务代理

  Scenario: 成功转发GET请求到身份服务
    Given IdentityService可用
    And IdentityServiceOptions已正确配置
    When 发送 GET /api/identity/users
    Then 请求路径重写为 /api/users
    And 请求头包含 X-Admin-AppId
    And 请求头包含 X-Admin-AppSecret
    And 返回身份服务的响应

  Scenario: 成功转发POST请求到身份服务
    Given IdentityService可用
    And IdentityServiceOptions已正确配置
    When 发送 POST /api/identity/accounts
    Then 请求路径重写为 /api/accounts
    And 请求头包含 X-Admin-AppId 和 X-Admin-AppSecret
    And 请求体被正确转发
    And 返回身份服务的响应

  Scenario: 身份服务不可达时返回502
    Given IdentityService不可用
    When 发送 GET /api/identity/users
    Then 返回502 Bad Gateway

  Scenario: 非identity路径不被拦截
    Given 请求路径为 /api/admin/users
    When 请求经过中间件
    Then 请求不被拦截，继续正常处理

  Scenario: 路径重写保留查询参数
    Given IdentityService可用
    When 发送 GET /api/identity/users?page=1&size=10
    Then 请求路径重写为 /api/users?page=1&size=10
    And 查询参数被保留

  Scenario: 路径重写保留请求体
    Given IdentityService可用
    When 发送 POST /api/identity/users
    And 请求体包含用户创建数据
    Then 请求体被完整转发到身份服务
```

---

## IdentityAccountsController 测试

```gherkin
Feature: 用户账户批量查询

  Scenario: 空输入返回空列表
    When 发送 POST /api/admin/identity-accounts/batch with null or []
    Then 返回 { accounts: [], partialFailure: false, warning: null }

  Scenario: 缺少凭据返回空列表
    Given AppId 或 AppSecret 为空
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1"]
    Then 返回 { accounts: [], partialFailure: false, warning: null }

  Scenario: 成功查询返回过滤后的账户
    Given IdentityService可用
    And 返回 user-1, user-2, user-3 的信息
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1", "user-3"]
    Then 返回 { accounts: [user-1, user-3], partialFailure: false, warning: null }

  Scenario: 大小写不敏感匹配
    Given IdentityService返回 userId="User-1"
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1"]
    Then 返回 { accounts: [User-1], partialFailure: false, warning: null }

  Scenario: null字段默认为空字符串
    Given IdentityService返回 username=null, phone=null 的用户
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1"]
    Then 返回 accounts 中 null 字段映射为 ""

  Scenario: 服务不可用返回partialFailure
    Given IdentityService不可用(抛出异常)
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1"]
    Then 返回 { accounts: [], partialFailure: true, warning: "Identity 服务暂时不可用，部分账户信息无法加载" }

  Scenario: 非成功状态码返回空列表无partialFailure
    Given IdentityService返回 500
    When 发送 POST /api/admin/identity-accounts/batch with ["user-1"]
    Then 返回 { accounts: [], partialFailure: false, warning: null }

Feature: 按账户查询学生

  Scenario: 成功查询返回学生列表
    Given StudentManagement gRPC服务可用
    When 发送 GET /api/admin/accounts/{accountId}/students
    Then 返回 StudentDto 列表

  Scenario: gRPC异常向上传播
    Given StudentManagement gRPC服务抛出 NotFound
    When 发送 GET /api/admin/accounts/{accountId}/students
    Then 抛出 RpcException
```

---

## 鉴权回归测试（已实现）

测试代码位于 `Tests/Controllers/ControllerAuthorizationTests.cs`，通过反射验证 controller 级 `[Authorize]` 属性存在，防止重构时误删导致鉴权失效。FallbackPolicy 兜底不足以替代显式标注（项目约定：所有 `/api/admin/*` controller 必须显式 `[Authorize]`，作为后续 `[Authorize(Roles = "admin")]` 收紧的扩展点）。

| 测试方法 | 验证内容 |
| --- | --- |
| `IdentityAccountsController_HasAuthorizeAttribute` | IdentityAccountsController 必须携带 `[Authorize]` 属性（Identity 账号管理为管理员操作） |
