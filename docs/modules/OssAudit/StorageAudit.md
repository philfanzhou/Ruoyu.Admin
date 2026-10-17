# Storage Audit

Storage Audit uses [StorageReferences v1](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/StorageReferences.md) and its [JSON Schema](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/storage-references-v1.schema.json), both maintained by Ruoyu.Study. This repository maintains only the rules by which Admin calls these interfaces and its hand-written DTO copies. The reference scope, the canonical encoding of the digest, the dedicated RS256 signature, pagination, validity periods, and the migration requirements for the three provider services all follow the same version of the upstream interface specification.

## Observation and persistence

All scans and deletion requests share `StorageReferenceCollector`. It gathers the complete snapshot metadata (`metadata`) and every page from the three services — Student, Mistake, and Homework — validating the version, identity, reference coverage, item counts, ordering, uniqueness, the final page, the digest, the validity period, and the `deletionAuthorized` field, which is always `false`. Any dependency error, redirect, TLS error, timeout, cancellation, or response that does not conform to the interface specification fails the whole collection run; there is no fallback to the old offset pagination, gRPC, or empty collections. **All of these validations must complete before any S3 object listing.** The named HttpClient forbids redirects and uses the normal CA trust chain and hostname verification; HTTP is not accepted, and no credentials are issued to arbitrary routes. An S3 listing failure must throw an error and must not be treated as a successful empty collection.

`OssAuditWorker` lists only `uploads/` and `mistakes/`, and keeps skipping thumbnails derived by the existing rules. `Questions` and `Documents` have no complete reference source and are not scanned. A complete scan commits all new observations together with the finished run record in a single database transaction; on failure it rolls back the run's observations, sets the run record to `Status=2`, and saves only a fixed error reason that contains no sensitive values. Run-record `Status=0/1/2` means running, completed, and failed respectively; the run record stores the three `ReferenceSnapshots` metadata JSON blobs and `ReferenceContractVersion="storage-references-v1"`. In the new version the compatibility field `NewZombieCount` means the number of newly added observations; old run records keep their original historical meaning, and no new-version snapshots or validation results are fabricated for them.

| Record.Status | Meaning and allowed operations |
|---|---|
| 0 Pending | historically pending; only a note can be saved via the original `ignore` action, never deletion |
| 1 Resolved | the historical already-deleted state; the original record is kept and cannot be used to delete again |
| 2 Ignored | historically ignored; the original record is kept |
| 3 UnreferencedObservation | a read-only report that "no reference was observed"; it cannot be selected or targeted by `resolve`/`ignore` |

When a record of any status already exists for the same path, no duplicate is created. At startup, historical review-derived images and questions/documents records are not deleted; the three legacy cleanup helper methods produce no side effects. New business references may appear after collection, so the report cannot conclude that an object has already become an orphan, and it grants no deletion permission. `deletionAuthorized=false` is unaffected by configuration, requests, or old statuses.

## HTTP and the current UI

The existing authentication and browser CSRF rules are unchanged; see [API authentication](../../api.md). `GET records` keeps its fields and supports pagination plus status/bucket filtering; `statusCounts` includes status 3, and `bucketCounts` counts statuses 0 and 3. `GET status` adds `observationCount` and the always-`false` `deletionAuthorized`; `lastCompleted` adds the version field and the three metadata blobs. Failure responses expose no internal exceptions.

Existing single-record and non-empty batch `resolve` requests always go through the same collector, covering old statuses 0/1/2, the new status 3, and batch requests in which some or all IDs do not exist. When references are incomplete they return 502 `references_unavailable`; after complete v1 reference validation succeeds they deterministically return 409 `cleanup_not_authorized`. These requests execute no object Delete, no Copy, and no audit-record deletion. An unknown single-record ID returns 404, and an empty batch returns 400. `ignore` is allowed only for historical status 0; every other status returns 400. Manual triggering still uses the existing in-process `Task.Run` and worker scheduling, and re-triggering is refused while a run is in progress; this does not guarantee persistent queuing or recovery after the process exits. For reliable queuing, recovery, and extension strategies see the [existing pending decisions](../../pending-decisions.md).

The current OSS audit view shows no selection, deletion, or batch cleanup operations. New run records display "no reference observed" together with the item counts and collection time of the three reference provider services; old run records display "historical findings", and no new-version validation results are fabricated. The statistics display "historical pending" and the new observation count separately; an old Pending record can only have a note saved via `ignore`. On request failure the list is cleared and a retry is offered, and after a scan completes the view refreshes automatically; search still filters only the current page. The UI specification is in [frontend-spec](../../../frontend/docs/frontend-spec.md#6-oss-audit-page-oss-audit).

## Configuration, upgrade, rollback

`StorageReferences:Enabled` defaults to `false`. When enabling it you must configure the issuer/audience/subject, a unique KeyId, a read-only private-key mount with 0600 permissions, and three fixed HTTPS root BaseUrls. Configuration values, the provider services' configuration and key rotation, normal TLS, and the credential-externalization rules all follow the upstream interface specification. The Mistake signer or ordinary user JWTs must not be reused, and there is no falling back to the old client. Disabling the capability does not release the historical `pins`: these are object references that business source snapshots, receipts, and attempt records keep permanently retained — they are not a cache that can be discarded when an audit snapshot expires, and they are not deletion authorization. Disabling the capability likewise does not lift the delete-refusal rules of `resolve`.

The EF Core migration chain appends `20261003030002_AddReferenceObservations` after the original baseline, adding two nullable text columns to the run records. A legal legacy database without migration history must first have the old baseline structure verified, then the baseline migration history registered, and only then the new migration executed. The migration history must exactly match, in order, a prefix of the known migration ids; unknown migrations and incompatible structures must still be refused. When non-empty `ReferenceSnapshots` or status-3 records exist, `Down` refuses to run. See the [migration documentation](../../database/migrations.md) for details.

Validate upgrades only in an isolated environment owned by whoever executes them: upgrade the reference provider services first, then deploy the new Admin, configure normal TLS and a dedicated key, and explicitly enable the reporting capability on both sides last. Back up the database with a full `pg_dump`, including the run records, the audit records, and the migration history; restore it into a new isolated database created and owned by the executor, and verify the original data, the migration history, and the actual reports item by item without cleaning up historical objects. Deactivating the new capability must preserve the data; performing cleanup by rolling back to an old Admin that can delete objects is forbidden. This capability provides no physical garbage collection, `SourceReleased`, historical-record cleanup, or global concurrency coordination between business writes and audit deletions, and it does not take over shared environments.

## Verification strategy and commands

Controller tests cover the unified reference check and delete-refusal rules for all record statuses and missing IDs; collector tests cover the exact HTTP requests to the three services, the signature, and the faults and boundaries of every page; worker tests cover listing only after validation is complete, status 3, S3 errors, rollback on cancellation, and no legacy cleanup at startup. Real PostgreSQL tests cover the exact migration-history prefixes of the old and new versions, upgrading a legal legacy database after verification, and refusing `Down` when new observation data exists. The frontend uses real Element Plus DOM to verify the absence of delete or selection operations and checks the display of the three metadata blobs and the historical records.

Real joint-deployment acceptance must record the exact commits and image digests of both sides, use three real service hosts, PG 15.18, Seaweed 4.48, the normal CA trust chain and RS256, plus the real worker, Controllers, current UI, and a full backup restore. The test environment is created and cleaned up by whoever executes it; unit-test fault doubles cannot replace real acceptance.

```bash
cd backend
dotnet build Ruoyu.Admin.sln --configuration Release
dotnet test Ruoyu.Admin.sln --configuration Release --no-build
# ADMIN_MIGRATION_TEST_CONNECTION is imported from your own restricted file; when not provided, the original Testcontainers are used.
cd ../frontend
npm ci
npm run build
npm test
```
