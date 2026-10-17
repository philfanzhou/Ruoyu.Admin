# IdentityProxy Test Cases

## Current status: [to confirm]

The following are suggested test cases based on the middleware's behavior.

---

## IdentityProxyMiddleware tests

```gherkin
Feature: Identity service proxy

  Scenario: Successfully forward a GET request to the identity service
    Given the IdentityService is available
    And IdentityServiceOptions is configured correctly
    When a GET /api/identity/users is sent
    Then the request path is rewritten to /api/users
    And the request headers contain X-Admin-AppId
    And the request headers contain X-Admin-AppSecret
    And the identity service's response is returned

  Scenario: Successfully forward a POST request to the identity service
    Given the IdentityService is available
    And IdentityServiceOptions is configured correctly
    When a POST /api/identity/accounts is sent
    Then the request path is rewritten to /api/accounts
    And the request headers contain X-Admin-AppId and X-Admin-AppSecret
    And the request body is forwarded correctly
    And the identity service's response is returned

  Scenario: Return 502 when the identity service is unreachable
    Given the IdentityService is unavailable
    When a GET /api/identity/users is sent
    Then a 502 Bad Gateway is returned

  Scenario: Non-identity paths are not intercepted
    Given the request path is /api/admin/users
    When the request passes through the middleware
    Then it is not intercepted and continues processing normally

  Scenario: Path rewriting preserves query parameters
    Given the IdentityService is available
    When a GET /api/identity/users?page=1&size=10 is sent
    Then the request path is rewritten to /api/users?page=1&size=10
    And the query parameters are preserved

  Scenario: Path rewriting preserves the request body
    Given the IdentityService is available
    When a POST /api/identity/users is sent
    And the request body contains user-creation data
    Then the request body is forwarded to the identity service in full
```

---

## IdentityAccountsController tests

```gherkin
Feature: Batch user-account query

  Scenario: Empty input returns an empty list
    When a POST /api/admin/identity-accounts/batch is sent with null or []
    Then { accounts: [], partialFailure: false, warning: null } is returned

  Scenario: Missing credentials return an empty list
    Given AppId or AppSecret is empty
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1"]
    Then { accounts: [], partialFailure: false, warning: null } is returned

  Scenario: A successful query returns the filtered accounts
    Given the IdentityService is available
    And returns the information of user-1, user-2, user-3
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1", "user-3"]
    Then { accounts: [user-1, user-3], partialFailure: false, warning: null } is returned

  Scenario: Case-insensitive matching
    Given the IdentityService returns userId="User-1"
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1"]
    Then { accounts: [User-1], partialFailure: false, warning: null } is returned

  Scenario: Null fields default to empty strings
    Given the IdentityService returns a user with username=null, phone=null
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1"]
    Then the returned accounts map null fields to ""

  Scenario: Service unavailability returns partialFailure
    Given the IdentityService is unavailable (throws an exception)
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1"]
    Then { accounts: [], partialFailure: true, warning: "The Identity service is temporarily unavailable; some account information could not be loaded" } is returned

  Scenario: A non-success status code returns an empty list without partialFailure
    Given the IdentityService returns 500
    When a POST /api/admin/identity-accounts/batch is sent with ["user-1"]
    Then { accounts: [], partialFailure: false, warning: null } is returned

Feature: Query students by account

  Scenario: A successful query returns the student list
    Given the StudentManagement gRPC service is available
    When a GET /api/admin/accounts/{accountId}/students is sent
    Then a StudentDto list is returned

  Scenario: gRPC exceptions propagate upward
    Given the StudentManagement gRPC service throws NotFound
    When a GET /api/admin/accounts/{accountId}/students is sent
    Then an RpcException is thrown
```

---

## Authorization regression tests (implemented)

The test code lives in `Tests/Controllers/ControllerAuthorizationTests.cs`; it verifies through reflection that the controller-level `[Authorize]` attribute exists, preventing accidental removal during refactoring from breaking authorization. The default FallbackPolicy authorization policy cannot replace explicit annotation (project convention: every `/api/admin/*` controller must carry an explicit `[Authorize]`, as the extension point for a later `[Authorize(Roles = "admin")]` tightening).

| Test method | What it verifies |
| --- | --- |
| `IdentityAccountsController_HasAuthorizeAttribute` | IdentityAccountsController must carry the `[Authorize]` attribute (Identity account management is an administrator operation) |
