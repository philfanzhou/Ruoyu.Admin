# Pending Decisions

This file is Ruoyu.Admin's only entry point for pending design decisions. It records only questions that the current code can still confirm and that need a product or architecture trade-off.

- Once a decision is made, move it into the corresponding ADR, capability document, or implementation Issue, and remove it from this file.
- A pending item blocks only the scope declared in its own section; it does not block unrelated fixes, tests, or feature development.
- Do not record execution logs, completed items, ordinary code-hygiene suggestions, or test results here.
- For cross-repository contract questions whose primary responsibility lies with Ruoyu.Study, record them in that repository's `docs/pending-decisions.md`; this file keeps only the Admin-side trade-offs.

PD-001 through PD-003 were carried into this product from the Ruoyu.Study monorepo on 2026-09-24, originally numbered PD-009, PD-010, and PD-011 (see ADR-0010 in the Ruoyu.Study repository (extract-ruoyu-admin; that repository is not public)).

## PD-001 Reliable scheduling and mutual exclusion for OSS audit jobs

- **Status**: pending decision
- **Scope of impact**: Storage Audit
- **Current evidence**: `OssAuditController.TriggerAudit` uses fire-and-forget `Task.Run`; the running check in `OssAuditWorker.RunAuditAsync` (querying other `Status=0` runs) and the Run record creation are not a single atomic mutual-exclusion operation, relying only on `DbUpdateException` as a backstop, and the job can be lost when the process exits.
- **A (recommended)**: queue to the existing BackgroundService via a bounded Channel, and guarantee mutual exclusion with a database unique-run constraint.
- **B**: use a persistent job table or a dedicated job framework, with retry and recovery support.
- **C**: keep the current in-process trigger mechanism.
- **Blocked scope**: only blocks the manual OSS audit trigger and the job recovery model.
- **Related**: the concurrent mutual-exclusion paths of `OssAuditWorker` currently have no test coverage (see the trigger boundary in `docs/modules/OssAudit/StorageAudit.md`); the decision's implementation must add them at the same time.

## PD-002 Scaling strategy for full OSS audit scans

- **Status**: pending decision
- **Scope of impact**: Storage Audit; cross-repository dependencies on Learner, Mistake Learning, Homework, and object storage
- **Current evidence**: the current v1 collector reads three immutable snapshots of at most 100000 keys / 32MiB canonical each, builds sets in memory, and then compares them against the S3 listing; every non-empty single/batch resolve runs the collector once and then refuses deletion. Reference sets exceeding the v1 budget fail explicitly. Scaling beyond the budget, streaming set operations, and S3 listing scale still need separate trade-offs.
- **A (recommended)**: the data-owning services provide cursor-based streams of reference paths, and Storage Audit uses chunked set operations and batch upserts.
- **B**: keep the current APIs but add pagination concurrency, timeouts, caching, and batched database queries.
- **C**: accept the current full in-memory aggregation.
- **Blocked scope**: only blocks scalability work on large-volume OSS audits.
- **Cross-repository note**: option A beyond the current fixed v1 budget requires Student, Mistake, and Homework to change their contracts separately, which is Ruoyu.Study's implementation scope; this repository can only choose the consumption mode and cannot land A unilaterally. Options B and C can be implemented independently.

## PD-003 Default policy when CORS is not configured

- **Status**: pending decision
- **Scope of impact**: Admin Web API deployment
- **Current evidence**: in `Program.cs`, when `AdminWeb:AllowedOrigins` is empty, the `AdminWeb` policy calls `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`. After the repository was extracted as a public open-source project, this default's exposure surface is larger than it was inside the private monorepo.
- **A (recommended)**: fail startup outside the Development environment; in Development allow only explicit localhost origins.
- **B**: when not configured, do not enable a cross-origin policy at all.
- **C**: keep the AllowAnyOrigin default.
- **Blocked scope**: only blocks the CORS default behavior and deployment configuration.
- **Related**: in the integrated deployment mode the SPA and the API are same-origin and browsers need no cross-origin requests; `AllowedOrigins` only serves split frontend/backend deployments. This keeps the startup-failure risk of option A controllable.
