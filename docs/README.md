# Ruoyu.Admin documentation

Ruoyu.Admin is the administration console service of the Ruoyu.Study platform, providing student management, upload record review, mistake management, and OSS storage audit capabilities.

## Entry points

| Entry point | Purpose |
|------|------|
| [overview/](./overview/README.md) | Service-level overview |
| [modules/](./modules/README.md) | Internal business capabilities |
| [Integration/](./Integration/README.md) | External system interactions |
| [database/](./database/README.md) | Data structures and maintenance responsibilities |
| [development/](./development/README.md) | Development execution support |
| [pending-decisions.md](./pending-decisions.md) | This repository's only entry point for pending design decisions |
| [api.md](./api.md) | Supplementary API documentation |
| [development/Deployment.md](./development/Deployment.md) | Deployment instructions |
| [../CONTEXT.md](../CONTEXT.md) | Storage Audit context terminology |

## Repository layout

| Path | Contents |
|------|------|
| `backend/Admin.WebApi` | Host, controllers, proxy middleware, audit persistence, `OssAuditWorker` |
| `backend/Ruoyu.Admin.Common` | `IOssService` (S3 + local files), thumbnails, shared JWT utilities (not registered by this host), database initializer, shared constants |
| `backend/Ruoyu.Admin.Consul` | Consul KV configuration source with local cache fallback, Serilog/Loki bootstrap, PostgreSQL connection-string factory |
| `backend/Ruoyu.Admin.ServiceClients` | Hand-written HTTP clients and mirror DTOs for Student / Mistake |
| `backend/Tests` | xUnit + Moq + FluentAssertions unit tests |
| `frontend/` | Vue 3 + Element Plus admin SPA |
| `scripts/` | Docker image build scripts |
| `start.sh` | Integrated image startup script |

## External contract ownership

The interface definitions of Student, Mistake, Homework, Teacher Portal, Assistant Portal, and Identity are **not maintained by this repository**: the first five are maintained by Ruoyu.Study, and Identity is maintained by [SignaCore](https://github.com/philfanzhou/SignaCore). The DTOs in `backend/Ruoyu.Admin.ServiceClients` are hand-written mirror copies of these HTTP contracts: an upstream field change produces no compile error in this repository — only runtime deserialization drift — so upstream changes must be synchronized into ServiceClients along with additional controller unit tests.

## Managed assignment interface progress

[IKJKML](https://gitee.com/philfanzhou/Ruoyu.Study/issues/IKJKML) once required two different crops of the same original image, but the whole-image path input of the old POST upload could not express rectangles. After upstream added `SourceRegions`, [PR #77](https://github.com/philfanzhou/Ruoyu.Admin/pull/77) was merged on 2026-10-07; Admin now supports choosing either `sourcePaths` or `sourceRegions`. The original six real acceptance records are in that PR. Regression of the current deployment environment continues to be tracked by [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24). For the interface, grouping rules, and recovery rules, see the [managed assignment notes](./Integration/ManagedAssignment.md).

## Known documentation debt (inherited from the monorepo)

The following problems existed before the extraction and **have not been corrected yet**; when reading, treat code as the source of truth:

- The integration matrix in `docs/overview/Integration.md` is still described in terms of gRPC, with the "Phase 4 planned changes (not yet implemented)" annotation. The actual implementation has fully moved to HTTP/JSON and gRPC has been removed. The method names and ports in the matrix need to be re-verified against `backend/Admin.WebApi/Program.cs`, `Controllers/`, and the three proxy middleware.
- About 40 files under `docs/modules/` still carry gRPC-era wording (`StudentManagementGrpcServiceClient`, `Ruoyu.Study.Student.Contract.Protos`, `MistakeGrpcService`, etc.). These are historical design drafts describing protocols that have been taken offline.
- `docs/modules/*/04-TASKS.md` files are historical implementation task lists; they do not represent the current state and must not be used as runbooks.
- The monorepo's repository-level ADRs (for example ADR-0003, separating internal and external OSS routing) stayed in the Ruoyu.Study repository; this repository's documentation references them by absolute link.

The dependency diagram in `docs/overview/Design.md`, `docs/api.md`, and this document were updated to current facts at extraction time.

## Storage Audit

For current behavior and verification entry points, see [StorageAudit](./modules/OssAudit/StorageAudit.md). The v1 scan only adds read-only observations and every audit deletion entry point refuses; old audit records and objects are retained, and legacy review images or legacy bucket records are no longer cleaned at startup.

## Inherited defects still to be addressed

### `Steeltoe.Discovery.Consul` 4.2.0 high-severity advisory (GHSA-67c9-f6v2-qv86)

The Consul service discovery/configuration component inherited from the source repository carries a known high-severity advisory: malformed `secure` metadata aborts service instance lookup (a denial of service). Affected range `>= 4.0.0, <= 4.2.0`, fixed in `4.3.0`; `dotnet restore` surfaces it as NU1903.

The trivy image scan step in `ci.yml` (HIGH/CRITICAL + `ignore-unfixed`) is currently **report-only (`exit-code: '0'`)**: the advisory already has a fixed release, so `ignore-unfixed` does not skip it, and making it blocking would turn the required check `Build & Test` red for every PR from that day on. **Upgrade conditions**: in a separate change, bump `Ruoyu.Admin.Consul`'s Steeltoe.Discovery.Consul to `>= 4.3.0` (this touches Consul KV loading and service registration behavior and requires full regression per the verification discipline), and after that review passes, change the step to `exit-code: '1'` (aligning with SignaCore's blocking policy). At the first scan, another image (the deprecated standalone frontend image, whose nginx:1.30.0-alpine base layer contributed 39 HIGH findings) disappeared along with the retirement of the split mode on 2026-09-25; the integrated image is now the only scan target and Steeltoe is the only known finding.

### `Ruoyu.Admin.ServiceClients` still contains unused downstream methods

In the monorepo this library was shared by the Mistake service and several portals. #104 removed the uncalled methods and their companion DTOs in the mistake review / re-analysis / homework image management direction (7 Mistake methods, 10 Student methods, and their exclusive DTOs, including the return-record request block for the mistake→student call). Remaining inventory: all 7 Mistake methods are in use; of Student's 24 methods, 20 are used, while `AppendImagesToRecordAsync`, `DeleteUploadRecordAsync`, `MarkUploadRecordAsReturnedAsync`, `RemoveProcessedImagesAsync` and their exclusive DTOs still have no callers and are items for later cleanup.

### `Ruoyu.Admin.Common.Constants.OssPathPrefixConstants` is unreferenced in this repository

It is a cross-prefix list for lazy validation of Student image fingerprints, copied over with the shared library as a whole; no code in this repository reads it.

Its `All` lists only `uploads/`, `mistakes/`, `homework/`, which does not correspond to the four `OssBucket` values (`Uploads`, `Mistakes`, `Questions`, `Documents`). **Do not** treat it as the basis for audit scope — audit scope is defined by `OssAuditWorker.AuditedBuckets`.

Also, the `homework/` prefix itself is misleading: the Homework service has no OSS integration, and homework images are actually stored by Student under `uploads/homework/` (the old startup cleanup once handled this path; the current v1 has stopped that cleanup and keeps the objects), and the source comments also mark the constant as "reserved for future homework-service image upload". When deciding which service owns a class of objects, go by the actual writer, not by this constant.

Handling options: delete the file, or add a comment explaining its real ownership and the reserved status of `homework/`.
