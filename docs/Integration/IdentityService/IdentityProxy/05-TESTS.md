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
Feature: 用户账户管理

  Scenario: [待确认具体测试用例]
    Given [待确认]
    When [待确认]
    Then [待确认]
```
