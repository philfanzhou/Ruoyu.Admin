# TeacherPortalProxy 测试用例

## 当前状态：[待确认]

以下为基于中间件行为的建议测试用例。

---

## TeacherPortalProxyMiddleware 测试

```gherkin
Feature: 教师门户代理

  Scenario: 成功转发admin路径请求
    Given TeacherPortal可用
    And TeacherPortalOptions已正确配置
    When 发送 GET /api/teacher-portal/admin/teachers
    Then 请求路径重写为 /api/admin/teachers
    And 请求头包含 X-Admin-Key
    And 返回教师门户的响应

  Scenario: 成功转发auth路径请求
    Given TeacherPortal可用
    And TeacherPortalOptions已正确配置
    When 发送 POST /api/teacher-portal/auth/login
    Then 请求路径重写为 /api/auth/login
    And 请求头包含 X-Admin-Key
    And 返回教师门户的响应

  Scenario: 其他路径默认路由到admin
    Given TeacherPortal可用
    And TeacherPortalOptions已正确配置
    When 发送 GET /api/teacher-portal/dashboard
    Then 请求路径重写为 /api/admin/dashboard
    And 请求头包含 X-Admin-Key
    And 返回教师门户的响应

  Scenario: 教师门户不可达时返回502
    Given TeacherPortal不可用
    And TeacherPortalOptions已正确配置
    When 发送 GET /api/teacher-portal/admin/teachers
    Then 返回502 Bad Gateway

  Scenario: 未配置时返回503
    Given TeacherPortalOptions未配置
    When 发送 GET /api/teacher-portal/admin/teachers
    Then 返回503 Service Unavailable

  Scenario: 非teacher-portal路径不被拦截
    Given 请求路径为 /api/admin/users
    When 请求经过中间件
    Then 请求不被拦截，继续正常处理

  Scenario: 路径重写保留查询参数
    Given TeacherPortal可用
    And TeacherPortalOptions已正确配置
    When 发送 GET /api/teacher-portal/admin/teachers?page=1
    Then 请求路径重写为 /api/admin/teachers?page=1
    And 查询参数被保留

  Scenario: 路径重写保留请求体
    Given TeacherPortal可用
    And TeacherPortalOptions已正确配置
    When 发送 POST /api/teacher-portal/admin/teachers
    And 请求体包含教师创建数据
    Then 请求体被完整转发到教师门户
```
