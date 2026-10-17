# Teacher Portal Proxy (TeacherPortalProxy)

## Feature name and one-line summary

**TeacherPortalProxy** — the admin console proxies teacher-portal requests and injects the admin key automatically.

## Core user stories

As an administrator, I want to access teacher-portal functionality uniformly through the admin console without handling authentication manually, to simplify unified teacher-permission management.

## Supplementary constraints

1. **Idempotency**: proxy forwarding is stateless; GET requests are naturally idempotent, and POST/PUT/DELETE follow the downstream service's semantics
2. **Concurrency**: no concurrency limit; each request is forwarded independently
3. **Transaction boundary**: a single request-response with no cross-request transactions
4. **Failure degradation**: returns 502 Bad Gateway when the teacher-portal service is unreachable; returns 503 Service Unavailable when not configured
5. **Path rewriting rules**: `/api/teacher-portal/admin` → `/api/admin`; `/api/teacher-portal/auth` → `/api/auth`; other `/api/teacher-portal/*` → `/api/admin`
6. **Authentication injection**: forwarded requests automatically get the `X-Admin-Key` header
7. **Configuration dependency**: TeacherPortalProxyOptions (Address, AdminApiKey) must be configured

## Key acceptance conditions summary

1. All `/api/teacher-portal/*` requests are intercepted by the middleware and forwarded to the teacher-portal service
2. The path rewriting rules are correct: admin → `/api/admin`, auth → `/api/auth`, others → `/api/admin`
3. Forwarded requests automatically carry the `X-Admin-Key` header
4. A 502 Bad Gateway is returned when the teacher-portal service is unreachable
5. A 503 Service Unavailable is returned when the proxy is not configured
6. Non-teacher-portal paths are unaffected by the middleware

## Out of scope

- The teacher portal's business-logic implementation
- The administrator authentication/authorization flow
- Request/response caching
- Request rate limiting and circuit breaking

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
