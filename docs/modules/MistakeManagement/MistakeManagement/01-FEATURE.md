# Mistake Management (MistakeManagement)

## Feature name and one-line summary

**MistakeManagement** — administrators view and edit mistake records and migrate mistake image storage paths.

## Core user stories

As an administrator, I want to view and edit mistake records and migrate image storage paths so that mistake data stays accurate and storage stays consistent.

## Supplementary constraints

1. **Idempotency**: image migration is idempotent; when every path is already under `mistakes/` it returns migratedCount=0
2. **Concurrency**: migration uses a `pathMapping` dictionary so the same old path is migrated only once within a request, but there is no cross-request concurrency control
3. **Transaction boundary**: a single-image migration failure does not abort the whole flow; it only logs an Error and keeps the original path; already-migrated images are not rolled back
4. **Failure degradation**: when the student-name lookup fails, the response falls back to showing the studentId
5. **Pagination parameters**: page ≤ 0 resets to 1, size ≤ 0 resets to 10, size > 100 is capped at 100
6. **Migration path mapping**: handled server-side by Student gRPC `MigrateImagesToMistake` (`uploads/xxx` → `mistakes/xxx`); the Admin Portal updates paths from the returned result
7. **UpdateMistakeItem optional fields**: StudentId, Subject, and Grade are all optional; defaults are used when omitted

## Key acceptance conditions summary

1. The mistake-record list can be browsed page by page, filterable by student, subject, grade, and review status
2. A single mistake's details can be viewed, including sourceRegions and boundingBox
3. All mistakes associated with an upload record can be viewed
4. A mistake's student, subject, and grade information can be edited
5. Mistake images can be migrated from the `uploads/` path to the `mistakes/` path
6. Image migration is idempotent: when every path is already migrated, it returns migratedCount=0
7. The list endpoints do not return sourceRegions and boundingBox; only the detail endpoint does

## Out of scope

- Creating mistakes (produced by upload-record assignment)
- Changing mistake review status (done on the teacher side)
- Batch deletion of mistakes
- Intelligent recognition and annotation of mistake content

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
