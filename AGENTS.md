# Ruoyu.Admin Collaboration Guidelines

Ruoyu.Admin is the admin console of the Ruoyu.Study platform: a .NET BFF API, a Vue 3 / Element Plus admin frontend, and a single-container deployment. It holds almost no business data of its own; it only maintains Storage Audit data.

## Maintenance

- This file is the single entry point for the AI collaboration process, project boundaries, and verification constraints.
- Codex reads this file directly; Claude Code imports it through the root `CLAUDE.md`.
- Tool directories such as `.agent`, `.claude`, and `.trae` may add tool-specific rules, but must not override this file's boundaries, testing, and security constraints.
- CI lives in `.github/workflows/`: `ci.yml` (Build & Test: backend build + tests, frontend build + component tests, integration image build verification with trivy scan; on tag push, publishes the GHCR image and GitHub Release) and `codeql.yml` (Analyze csharp / javascript-typescript). `main` is protected by the `Protect main` ruleset: direct pushes, force pushes, and deletion are forbidden; PRs must pass the three required checks above before merging.
- Release contract: tags carry the `v` prefix (`vX.Y.Z-rc.N` / `vX.Y.Z`). `vX.Y.Z-rc.N` publishes only a **test image** with an immutable version tag and creates a pre-release; `vX.Y.Z` publishes the **production image** and also moves `X.Y` and `latest`. Image tags carry no `v` prefix (keeping the historical `X.Y.Z` form; the CI semver-metadata step strips it), and the GitHub Release title is the tag itself (no repository-name prefix). The image is `ghcr.io/philfanzhou/ruoyu.admin` — the API + SPA single integrated container is the product's only release form (the standalone frontend image inherited from the monorepo was never used by any deployment and has been removed).

## Documentation and communication language

- Repository documentation and communication use English: the root `README.md`, everything under `docs/`, `CONTEXT.md`, code comments, GitHub issue titles and bodies, PR bodies, and review comments.
- Write natural, concrete English that directly states the object, action, condition, and result. Avoid stiff translationese, abstract process jargon, and unnecessary language mixing; when a technical term is unavoidable, explain it on first use.
- PR titles and commit messages use the English conventional commit format (`feat:` / `fix:` / `docs:` / `test:` / `refactor:` / `chore:` etc.).
- Keep code, commands, paths, JSON fields, and configuration keys exactly as they are when quoting them.

## Project boundaries and architecture

- `backend/Ruoyu.Admin.Common`, `backend/Ruoyu.Admin.Consul`, and `backend/Ruoyu.Admin.ServiceClients` are class libraries and must not reference `backend/Admin.WebApi` back.
- `backend/Admin.WebApi` owns HTTP, authentication, configuration, the proxy middleware, and service composition at application startup. Controllers must not construct `HttpClient` directly; always go through the interfaces in `Ruoyu.Admin.ServiceClients` or an `IHttpClientFactory` named client.
- Authentication uses only `AdminSession` and managed OIDC. The five legacy configuration keys only allow omission or the exact value `true`; `false` and malformed values must be rejected before startup. Do not register inbound Bearer or JWT Cookie authentication schemes. `--validate-auth-config` loads the same configuration as a normal startup, then exits with a fixed result that contains no sensitive values, reading only the Consul cache; `start.sh` must validate the configuration with the target integrated image before stopping or removing the old container.
- The API listen port **5020 is hardcoded** (`const int httpPort` in `Program.cs`); it is not a configuration item. Changing the port is a deployment contract change and must be synchronized with `start.sh`, `frontend/vite.config.js`, and the deployment documentation.
- The only database is `ruoyu_admin`, with only the two tables `OssAuditRuns` and `OssAuditRecords`; their structure is managed by the EF Core migration chain in `Persistence/Migrations`, whose numbers and order must match exactly. At startup, the ServiceMantle-shared `StartupDatabaseGate` (its configuration boundary and deployment declarations live in `Admin.WebApi/Database`) calls `AuditMigrationExecutor` and the shared migration execution components (a PostgreSQL advisory lock lets multiple instances execute the migrations one after another; legacy-database takeover and structure-refusal rules are in `docs/database/migrations.md`). Adding or modifying a migration must keep the `AuditMigrationExecutor.KnownMigrationIds` contract in sync; the semantics of "reject unknown structures (`RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`)" and "a missing database is not created by default (`Database:AllowCreate` defaults to false)" must not be relaxed.
- Storage Audit v1 uses the full reference snapshots persisted by the three services and only adds read-only observations with `Status=3`. `deletionAuthorized` is always `false`. Existing single-record and non-empty batch `resolve` requests, whatever the record status, go through the same reference collector check and are then rejected: 409 when the references are complete, 502 when the references are unavailable. No object Delete or Copy, and no audit-record deletion, is ever executed. Anything that touches aggregation, decisions, or any deletion path must have tests.
- **Audit invariant: OSS objects may be listed only after the snapshots provided by all three reference services — Student, Mistake, and Homework — validate successfully.** If any service is unreachable, references are incomplete, a response does not conform to the interface specification, or the operation is cancelled, the whole run must fail: no listing, deleting, or copying of S3 objects, and no new observations published for that run. Do not fall back to the legacy gRPC path, offset pagination, or empty collections; the reference status captured at collection time is not permission for future deletion.
- Scans cover only `Uploads` and `Mistakes`. `Questions` and `Documents` have no complete reference source and are not scanned. The legacy regex-based image cleanup and legacy bucket-record cleanup no longer run at startup. The object references through which historical business snapshots, receipts, and attempt records stay retained (upstream calls them `pins`), plus the audit records, must be preserved; an old record's `Status` cannot bypass the reference check and the delete-refusal rules that every delete request must pass. The interface specification and Admin's calling responsibilities are in `docs/modules/OssAudit/StorageAudit.md`.
- `/` and every non-`/api` route must allow anonymous access; otherwise the SPA login page cannot load (not signed in → page unavailable → no way to log in: a deadlock). Static files and the SPA fallback must be registered before `UseAuthentication()`.

## External contract ownership

The Student, Mistake, Homework, Teacher Portal, and Assistant Portal interfaces are maintained by Ruoyu.Study; Identity is maintained by [SignaCore](https://github.com/philfanzhou/SignaCore). This repository has no compile-time dependency on them.

- The DTOs in `backend/Ruoyu.Admin.ServiceClients` are **hand-written mirror copies** of the downstream HTTP contracts. Upstream field changes produce no compile errors in this repository — only runtime deserialization drift.
- Therefore, a downstream contract change must be synchronized into ServiceClients, with the corresponding controller unit tests added.
- Do not add loose optional fields to DTOs just to make compilation pass and thereby mask inconsistencies caused by upstream interface changes; confirm the upstream definition, then update the DTOs and the tests together.
- The three proxy middleware (Identity / Teacher Portal / Assistant Portal) use only the server-side in-ticket token, reject inbound Authorization headers, and strip browser Cookie/CSRF/Host headers and downstream Set-Cookie headers. `IdentityProxyMiddleware` strips the inbound `X-Admin-AppSecret` and then injects this service's own value — do not remove that stripping logic, or callers could forge gateway credentials.

## Security and change discipline

- Never commit tokens, passwords, connection strings, the Identity `AppSecret`, database files, personal data, or real credentials, and never write them into logs, screenshots, or test output.
- The masking methods in `StartupDiagnosticsFormatter` (`MaskSecret` / `SummarizePassword`) are the only outlet for startup logging. New startup diagnostics logging must go through them; never print configuration values directly.
- The OSS credentials in `appsettings.json` are mock values for local development; production credentials come only from Consul KV or environment variables. Never write real credentials into any `appsettings*.json`.
- Behavior changes to authentication, authorization, audit deletion, proxying, and CORS must come with targeted tests and documentation.
- Before committing, check documentation links, sensitive information, and the repository state.

## Pending decisions

- Repository-level decisions that have not been made yet go only into `docs/pending-decisions.md`; do not create scattered `HumanReview.md` files or smuggle to-dos into capability documents.
- A pending item blocks only the scope declared in that file; it does not freeze unrelated tasks.
- Once a decision is made, move it into an ADR, a capability document, or an implementation Issue, and remove it from the pending file.
- Cross-repository interface questions owned by Ruoyu.Study are tracked in that repository; this repository keeps only the Admin-side trade-offs.
- Completion records, test results, ordinary defects, and code-hygiene suggestions do not go into the pending file; known defects are recorded in `docs/README.md`.

## Known documentation debt

The known-documentation-debt section of `docs/README.md` records outdated descriptions inherited from the monorepo that have not been corrected yet (mainly the gRPC-era integration matrix and design drafts). **Code, tests, configuration, and scripts are the current implementation truth**; when documentation and code conflict, first determine whether the implementation is unfinished, the documentation is outdated, or the requirement has changed — do not simply assume either side is correct, and do not base design decisions on outdated documentation.

## Verification

Run the minimum verification sufficient for the change's risk (backend `dotnet test` includes the Testcontainers integration tests in `backend/Tests/Integration/`, which need local Docker; without Docker, run the remaining suites with `--filter "FullyQualifiedName!~Admin.WebApi.Tests.Integration"`):

```bash
cd backend
dotnet restore Ruoyu.Admin.sln
dotnet build Ruoyu.Admin.sln --configuration Release
dotnet test Ruoyu.Admin.sln --configuration Release --no-build
```

Frontend:

```bash
cd frontend
npm ci
npm run build
npm test
```

When the Dockerfile or `start.sh` is touched, also build the image for real:

```bash
./scripts/build.sh
```

Cross-service end-to-end cases (Admin creates a student → visible in User Portal) stay in the Ruoyu.Study repository under `tests/e2e/`, run against deployed environments, not in this repository.
