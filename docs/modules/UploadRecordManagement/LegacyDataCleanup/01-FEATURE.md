# Legacy Data Cleanup (LegacyDataCleanup)

## Feature name and one-line summary

**LegacyDataCleanup** — administrators check and clean up upload records that finished review but were left behind, along with their images.

## Core user stories

As an administrator, I want to check and clean up leftover upload-record data, so that storage and records still lingering after the associated mistakes finish review are released in time.

## Supplementary constraints

1. **Idempotency**: LegacyCheck is an idempotent query; LegacyClean is not idempotent — re-running it attempts the deletion calls again
2. **Concurrency**: no explicit concurrency control; cleaning the same record concurrently may cause duplicated operations
3. **Transaction boundary**: LegacyClean is a multi-step serial gRPC sequence (CompleteUploadReview → RemoveImagesFromRecord → DeleteUploadRecordAfterReview) with no overall transaction; if a middle step fails, already-executed steps are not rolled back
4. **Failure degradation**: when preconditions are not met, a 400 error is returned and no cleanup runs
5. **LegacyCheck preconditions**: none; a record can be checked even with no associated mistakes (returns isLegacy=false)
6. **LegacyClean preconditions**: there must be associated mistakes and all of them must have finished review, otherwise 400
7. **ReviewerId**: in LegacyClean, CompleteUploadReview's ReviewerId is fixed to `"admin-legacy-cleanup"`

## Key acceptance conditions summary

1. An upload record can be checked for being legacy data (all associated mistakes finished review but the record still exists)
2. The legacy check returns isLegacy, mistakeCount, and hasUploadPathImage
3. With no associated mistakes, isLegacy=false; with pending-review mistakes, isLegacy=false plus pendingCount
4. The cleanup operation requires the preconditions: associated mistakes exist and all have finished review
5. The cleanup flow runs in order: CompleteUploadReview → RemoveImagesFromRecord (optional) → DeleteUploadRecordAfterReview
6. It can detect whether any image of the legacy data is still stored under the `uploads/` path
7. studentId is taken from the StudentId of the first associated mistake item; when empty, 400 is returned

## Out of scope

- Automatic scheduled cleanup of legacy data (manual triggering only)
- Batch cleanup of multiple legacy records
- Undo/rollback of the cleanup operation
- The mistake review workflow itself (done on the teacher side)

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
