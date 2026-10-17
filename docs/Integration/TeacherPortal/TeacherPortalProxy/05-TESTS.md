# TeacherPortalProxy Test Cases

## Current status: [implemented]

The following are test cases based on the middleware's behavior.

---

## TeacherPortalProxyMiddleware tests

```gherkin
Feature: Teacher portal proxy

  Scenario: Successfully forward an admin-path request
    Given the TeacherPortal is available
    And TeacherPortalOptions is configured correctly
    When a GET /api/teacher-portal/admin/teachers is sent
    Then the request path is rewritten to /api/admin/teachers
    And the request headers contain X-Admin-Key
    And the teacher portal's response is returned

  Scenario: Successfully forward an auth-path request
    Given the TeacherPortal is available
    And TeacherPortalOptions is configured correctly
    When a POST /api/teacher-portal/auth/login is sent
    Then the request path is rewritten to /api/auth/login
    And the request headers contain X-Admin-Key
    And the teacher portal's response is returned

  Scenario: Other paths default to admin
    Given the TeacherPortal is available
    And TeacherPortalOptions is configured correctly
    When a GET /api/teacher-portal/dashboard is sent
    Then the request path is rewritten to /api/admin/dashboard
    And the request headers contain X-Admin-Key
    And the teacher portal's response is returned

  Scenario: Return 502 when the teacher portal is unreachable
    Given the TeacherPortal is unavailable
    And TeacherPortalOptions is configured correctly
    When a GET /api/teacher-portal/admin/teachers is sent
    Then a 502 Bad Gateway is returned

  Scenario: Return 503 when not configured
    Given TeacherPortalOptions is not configured
    When a GET /api/teacher-portal/admin/teachers is sent
    Then a 503 Service Unavailable is returned

  Scenario: Non-teacher-portal paths are not intercepted
    Given the request path is /api/admin/users
    When the request passes through the middleware
    Then it is not intercepted and continues processing normally

  Scenario: Path rewriting preserves query parameters
    Given the TeacherPortal is available
    And TeacherPortalOptions is configured correctly
    When a GET /api/teacher-portal/admin/teachers?page=1 is sent
    Then the request path is rewritten to /api/admin/teachers?page=1
    And the query parameters are preserved

  Scenario: Path rewriting preserves the request body
    Given the TeacherPortal is available
    And TeacherPortalOptions is configured correctly
    When a POST /api/teacher-portal/admin/teachers is sent
    And the request body contains teacher-creation data
    Then the request body is forwarded to the teacher portal in full
```
