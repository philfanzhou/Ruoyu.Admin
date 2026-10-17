# Identity Service Proxy (IdentityProxy)

## Feature name and one-line summary

**IdentityProxy** — the admin console proxies identity-service requests and injects authentication automatically.

## Core user stories

As an administrator, I want to access identity-service functionality uniformly through the admin console without handling authentication manually, to simplify unified identity-account management.

## Supplementary constraints

1. **Idempotency**: proxy forwarding is stateless; GET requests are naturally idempotent, and POST/PUT/DELETE follow the downstream service's semantics
2. **Concurrency**: no concurrency limit; each request is forwarded independently
3. **Transaction boundary**: a single request-response with no cross-request transactions
4. **Failure degradation**: returns 502 Bad Gateway when the identity service is unreachable
5. **Path rewriting**: the `/api/identity` prefix is replaced with `/api`, for example `/api/identity/users` → `/api/users`
6. **Authentication injection**: forwarded requests automatically get the `X-Admin-AppId` and `X-Admin-AppSecret` headers
7. **Configuration dependency**: IdentityServiceOptions (Address, AppId, AppSecret) must be configured

## Key acceptance conditions summary

1. All `/api/identity/*` requests are intercepted by the middleware and forwarded to the identity service
2. Path rewriting is correct: the `/api/identity` prefix is replaced with `/api`
3. Forwarded requests automatically carry the `X-Admin-AppId` and `X-Admin-AppSecret` headers
4. A 502 Bad Gateway is returned when the identity service is unreachable
5. Non-identity-service paths are unaffected by the middleware

## Out of scope

- The identity service's business-logic implementation
- The administrator authentication/authorization flow
- Request/response caching
- Request rate limiting and circuit breaking

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
