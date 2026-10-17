# Upload Record Management (UploadRecordManagement)

## Feature name and one-line summary

**UploadRecordManagement** — administrators view and review student upload records and assign images to mistake items.

## Core user stories

As an administrator, I want to view and manage student upload records and assign images to mistake items, to complete the review of uploaded images and the filing of mistakes.

## Supplementary constraints

1. **Idempotency**: the legacy mode keeps the original entry point; managed-v1 fixes the revision, requestKey, and payload, and replaying the same group returns the original IDs stored by Mistake.
2. **Concurrency**: in the managed mode, Mistake decides by canonical grouping, same-group request association (canonical alias), and source-lock leases and protection rules; deduplication by source alone is not valid. Currently either whole-image paths or regions are accepted, exclusively; the interface limitation that different legal crops of the same original image could not be expressed was lifted with #77 — see the managed assignment notes for the concrete definitions and historical acceptance.
3. **Transaction boundary**: per-group HTTP calls with no whole-batch cross-request atomicity; on partial success or Unknown the original request is kept and the user retries explicitly.
4. **Failure degradation**: when the student-name lookup fails, the response falls back to showing the studentId
5. **Reviewed-image protection**: images already approved in review cannot be assigned again
6. **Image indices**: legacy uses the current record indices; in the managed mode the indices are used only for selection within the trusted details, sending exactly the fixed `sourcePaths` or `sourceRegions`, with no remapping against a refreshed record afterwards.
7. **Cross-subject assignment**: multiple assignments may each specify a different subject/grade

## Key acceptance conditions summary

1. All students' upload records can be browsed page by page, filterable by status and student
2. The images in an upload record can be viewed
3. Images in an upload record can be rotated to fix their orientation
4. Unneeded images can be removed from an upload record
5. Images in an upload record can be assigned to mistake items in groups, supporting cross-subject assignment
6. An upload record's status can be reset
7. An upload record can undergo VL analysis, automatically recognizing image groupings and subject/grade
8. Images already approved in review cannot be assigned again

## Out of scope

- Creating upload records (done on the student side)
- The review confirmation of mistake items (done on the teacher side)
- Training and tuning of the VL model
- Batch export of upload records

## Document index

- [Managed assignment integration contract](../../../Integration/ManagedAssignment.md) — configuration, trusted metadata, managed HTTP, results and recovery boundaries

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
