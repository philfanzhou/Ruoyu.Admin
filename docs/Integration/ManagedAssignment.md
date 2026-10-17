# Managed Mistake Assignment

The HTTP interfaces of Student and Mistake are maintained by Ruoyu.Study. Admin only aggregates trusted sources, fixes requests, and displays results; it adds no database migrations, idempotency tables, receipt writes, or source-release logic.

## Configuration and authentication

`Mistake:ManagedAssignmentEnabled` defaults to `false` and accepts only booleans. When enabling it, the HTTPS origin `MistakeService:Url` (no path, user info, query, or fragment) and the existing `IdentityService:Authority/Audience` must be configured explicitly, keeping `RequireHttpsMetadata=true`. Real certificates must match the hostname and be entered into the runtime trust chain. Automatic redirects, Cookie forwarding, certificate-verification bypasses, and implicit localhost fallbacks are not enabled. Configuration adjustments to the ordinary outbound transport do not relax this capability's HTTPS requirements.

Listing reuses the existing Student HTTP client. Writes use a separate `IManagedMistakeHttpClient` that calls only `POST /api/mistakes/upload`. Only server-side sessions are accepted currently: the trusted access token is obtained from the Admin session boundary, which has already checked administrator permission and CSRF for non-safe HTTP methods. Any inbound Authorization header, even when accompanied by a valid session and CSRF, returns 401; when the API session capability is off, the fixed answer is 503 `session_api_disabled`, and the retired Bearer authentication is not restored. The token is set per request; actor/role from the request body is not accepted, no worker credentials are issued, and the token is not attached to other routes or redirects.

## Trusted sources and fixed requests

Details come directly from the list items of `GET /api/admin/oss-upload-records`; no new single-item detail GET route was added. List items keep `imagePaths` and add `contentRevision`, the ordered `imageEntries[{path,type}]`, and `assignmentProtocol`. When the capability is on, missing items/revision/entries, illegal source/student GUIDs, empty types, or illegal paths all fail explicitly; a normal empty items array is still a legal empty page. When the name lookup fails, the studentId may be displayed, but name-lookup results must not be used to construct source metadata.

The UI allows selecting only items whose `type` is exactly `"mistake"`. The original index is used only for the current form selection; at send time the exact path is fixed, deduplication uses only Ordinal string comparison, with no trimming, no case folding, and no Unicode normalization. When the capability is off, `assignmentProtocol="legacy"` is returned; unknown or missing protocols are not assignable.

The existing `POST /api/admin/oss-upload-records/{id}/assign` accepts the following managed body:

```json
{
  "mode": "managed-v1",
  "studentId": "11111111-1111-4111-8111-111111111111",
  "expectedContentRevision": "22222222-2222-4222-8222-222222222222",
  "assignments": [{
    "requestKey": "33333333-3333-4333-8333-333333333333",
    "subject": 2,
    "grade": 7,
    "sourcePaths": ["uploads/questions/A.PNG"],
    "comments": "fixed reason"
  }]
}
```

source/student/revision/key are all non-zero GUIDs. Each batch has 1–100 groups; each group's key is unique, subject is 1–9, grade is 1–12, and comments is at most 4096 characters. comments maps to Mistake's rootCause; null is fixed to the empty string. Each group must choose exactly one of `sourcePaths` and `sourceRegions`:

- `sourcePaths`: 1–100 non-empty exact paths, each at most 4096 characters, denoting whole images.
- `sourceRegions`: 1–100 regions, each containing a non-empty exact `sourceImagePath` (at most 4096 characters) and a nullable `boundingBox`. `boundingBox=null` denotes the whole image; the rectangle coordinates `x1,y1,x2,y2` must be non-negative integers with `x1<x2` and `y1<y2`. For example `{"sourceImagePath":"uploads/questions/A.PNG","boundingBox":{"x1":0,"y1":0,"x2":100,"y2":100}}`. When regions are sent, the downstream `imagePaths` is empty.

The body expresses the administrator's assignment request; the authorization basis for paths, students, and classifications remains the source snapshot that Mistake reads. Different legal groups are not coarsely deduplicated by sourceId. Mistake decides grouping by region unit (`atom`): it encodes the original path, whole-image `Full` or the crop rectangle, the source image's byte length, and its SHA-256, so it must not be equated with an image path. Different legal crop regions of the same original image can form different groups; conflict determination still follows the upstream specification.

Submission and retry no longer GET the current Student and are not rebuilt from a new index; the original revision, paths or regions, key, and classification are always sent. When the capability is off, managed input returns 503; when it is on but fields are missing, 400 is returned — neither falls back to the legacy Submit.

## Results, recovery, and boundaries

Each group keeps `requestKey,state,errorKind,createdItemIds,statusCode`. A group's state becomes Completed only on a real HTTP 200, outer success=true, inner success=true, and IDs forming a non-empty set of legal, non-zero, non-duplicate GUIDs. The response body is capped at 64 KiB, a single send plus read totals 30 seconds, and there is no automatic retry. An explicit business rejection is Failed; 5xx, unexpected 503, disconnection, timeout, cancellation, illegal JSON, or missing IDs is Unknown. If the first group does not complete, subsequent groups stop being sent and the remaining groups are NotAttempted; the whole batch returns success=true only when every group is Completed.

The UI shows each group's real IDs and the error kind without sensitive values, and does not equate HTTP 200 with whole-batch success. While sending, selection, classification, editing, and image changes are locked; a double click sends only once. When the user explicitly retries, the entire original batch is sent; completed groups read their original results per Mistake's rules, and existing IDs cannot be erased by a new inconsistent response. While an Unknown is unresolved, it cannot overwrite the original operation. On row switch or drawer close, the operation number `generation` is incremented, and only responses belonging to the current operation may update the UI, preventing a late-arriving old response from overwriting new content. Cancellation only stops waiting for the current operation; the in-page recovery entry keeps the fixed request viewable even if the current source has changed or been deleted. Cancelling the wait does not prove the remote was not submitted.

These records live only in page memory; after the whole page closes, recovery is not guaranteed. Mistake is what durably stores groups, request associations, and results: it derives the canonical grouping from source, revision, and region; when a new requestKey corresponds to the same group and payload, it can associate (alias) and read the durably stored original items and results. Admin adds no database or browser operation-log table and does not proactively seal sources: after a source handover, upstream rejects new groups, while saved requests or group results are still read and replayed first under their own rules. Admin adds no source-handover calls, moves no images, deletes no sources, and runs no legacy cleanup. Legacy data merging, physical garbage collection, and shared-environment deployment belong to other tasks.

## Verification entry points and historical acceptance

The backend runs the Release build/test. `ManagedMistakeHttpClientTests` checks the concrete HTTP encoding and failures; `ManagedAssignmentApiTests` uses real MVC, a trusted session, and CSRF to verify the success path, and checks the rejection of any Bearer, the 503 when the API session capability is off, the 503 when the managed capability is off, the 400 for illegal bodies, the 502 for illegal metadata, and zero outbound calls on rejection; `ManagedAssignmentOptionsTests` checks the enabling conditions. The frontend runs `npm ci`, `npm run build`, and `npm test`; the fixed-operation and drawer tests cover fixed payloads, partial results, Unknown recovery, double clicks, cancellation, and late-arriving old responses.

Real acceptance uses a standalone test environment created and cleaned up by whoever executes it, recording the actual Admin and Study commits, normal TLS, real Student/Mistake, PostgreSQL/S3, and the identity registration. It verifies two legal groups, partial 409/5xx/disconnect/illegal-body cases, lost responses after submission and historical replay, concurrency of same-group request association, replay after source sealing, races between automatic processing and manual assignment, and the single session authentication and CSRF. Keep the raw evidence of failures and recoveries, the exact commands, image digests, UTC times, verification results with zero skips, and state summaries of this test's resources and unrelated environments before and after; credentials, personal data, and raw database backups must not be written into Issues or logs. Passing unit tests or test doubles cannot replace these business verifications.

Historically, [IKJKML](https://gitee.com/philfanzhou/Ruoyu.Study/issues/IKJKML) required two crops of the same original image, but Study `5c84d43d`'s fixed POST upload could only pass whole-image paths and could not express crop rectangles. That limitation was lifted by the upstream `SourceRegions` interface extension; [PR #77](https://github.com/philfanzhou/Ruoyu.Admin/pull/77) was merged on 2026-10-07, Admin now supports both inputs, and the six original acceptance results are in that PR. The original "two authentication pipelines" acceptance was executed per the single-session authentication requirements of #40/#71–#74, without restoring the retired Bearer.

Replaying a request after the downstream business items have been legally deleted and Storage Audit's `resolve` are different operations: the former returns results per Mistake's durable records and the upstream rules; the latter always refuses object cleanup, returning 409 when the references are complete. #77's real acceptance records cannot replace regression of the current deployment environment; subsequent deployment regression is tracked by [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24).
