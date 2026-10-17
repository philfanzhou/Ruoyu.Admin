# Open Subject Management (OpenSubjectManagement)

## Feature name and one-line summary

**OpenSubjectManagement** — administrators configure a student's open subjects and their effective date ranges.

## Core user stories

As an administrator, I want to set and view a student's open-subject configuration so that I can control which subjects the student may study and when it takes effect.

## Supplementary constraints

1. **Idempotency**: SetStudentOpenSubjects has full-replacement semantics; calling it repeatedly with the same list yields the same result (idempotent)
2. **Concurrency**: full replacement has no explicit concurrency control; the later write overwrites the earlier one
3. **Transaction boundary**: the set operation is a single gRPC call; the full replacement happens server-side
4. **Failure degradation**: subject options are fetched over gRPC; an unavailable service returns an error
5. **Subject validity**: subject values must pass the `SubjectConstants.IsValid` check
6. **Date rules**: OpenStartDate is required and must be a legal DateOnly format; OpenEndDate is optional, and empty means open-ended
7. **Active-status computation**: IsActive is computed by the gRPC server from the current date and OpenStartDate/OpenEndDate

## Key acceptance conditions summary

1. A student's open-subject list can be viewed, including effective dates and active status
2. Passing activeOnly=true returns only the currently active subjects
3. Setting a student's open subjects is a full replacement: the passed list replaces all of that student's existing configuration
4. Every subject's OpenStartDate is required; OpenEndDate is optional
5. Subject values must pass the `SubjectConstants.IsValid` check
6. IsActive is computed server-side from the current date and the date range

## Out of scope

- Incremental add/remove of individual subjects (only full replacement is supported)
- Partial updates of a subject's open dates
- An approval workflow for open subjects
- Management of subject definitions (maintained by the Student service)

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
