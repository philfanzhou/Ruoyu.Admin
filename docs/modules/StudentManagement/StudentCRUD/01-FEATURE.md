# Student CRUD (StudentCRUD)

## Feature name and one-line summary

**StudentCRUD** — full create, read, update, and delete over student profiles by administrators, plus option-data queries.

## Core user stories

As an administrator, I want to create, query, update, and delete student profiles so that the system's base student data is maintained.

## Supplementary constraints

1. **Idempotency**: create is not idempotent (repeated submissions create multiple records); update and delete are idempotent
2. **Concurrency**: no explicit concurrency control; relies on database row-level locks
3. **Transaction boundary**: a single create/update/delete completes within one gRPC call; no cross-service transactions
4. **Failure degradation**: subject options are fetched from the Student service over gRPC and return an error when the service is unavailable; grade options come from a static dictionary in the Controller with no external dependency
5. **Pagination parameters**: page defaults to 1 (minimum 1), pageSize defaults to 20 (range 1-100)
6. **Identity account validation**: on create, IdentityAccountIds is required and every entry must be a legal GUID; on update it is optional, but every provided entry must be a legal GUID

## Key acceptance conditions summary

1. Creating a student profile requires a name, a grade that is a valid 1-12 value, and at least one identity account ID in legal GUID format
2. Updating a student profile requires a name and a valid 1-12 grade; IdentityAccountIds is optional
3. After a student profile is deleted, that student no longer appears in list queries
4. Paged queries support filtering by name and grade and return paged results
5. Viewing a single student returns the complete detailed information
6. The grade options endpoint returns static 1-12 grade labels without depending on external services
7. The subject options endpoint returns the dynamic subject list via a gRPC call to the Student service

## Out of scope

- Batch import/export of student profiles
- Soft delete and restoration of student profiles
- Cross-service aggregate queries over student data
- Creation and management of identity accounts (see AccountLinking)

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
