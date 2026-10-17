# Admin Portal REST API documentation

## Overview

Admin Portal provides REST API endpoints for managing students, mistake records, OSS upload records, audits, and more.

## Basics

- Base path: `/api/admin`
- Data format: JSON
- Authentication: management APIs use only the server-side AdminSession; any inbound `Authorization` gets a fixed 401, and browser JWTs are not used.
- Anonymous entry points: SPA static files, the `/` home fallback, `/api/auth/login`, `/api/auth/callback`, `/api/auth/oidc/start`, `/api/auth/oidc/callback`, and `/api/auth/session` are marked `[AllowAnonymous]` and handled anonymously per their own protocols (see "Authentication flow" below)

---

## Authentication flow

Admin Portal uses **mode-1 integrated deployment**: the same container (port 5020) serves both the backend REST API and, through `wwwroot`, the frontend static files with SPA route fallback. Authentication therefore depends on the request path:

- **Frontend entry (anonymous)**: `/` and every non-`/api/*` path is handled by the SPA fallback; the login page loads without signing in
- **API (authentication required)**: `/api/*` is protected by `FallbackPolicy = RequireAuthenticatedUser()`; management paths and proxies use the administrator session as described below. Anonymous protocol entry points are exempt per their own rules.

### Hosted login foundation (#38)

The current version runs only the server-session hosted login; the protocol and security responsibilities are carried by the official package `SignaCore.Client.AspNetCore` (since #83, replacing the hand-written implementation): authorization code + PKCE S256 callback, server-side ticket storage, pending state, prepared logout, and the user-neutral antiforgery boundary of its endpoints. `IdentityService:Authority/AppId/AppSecret`, `AdminOidc:RedirectUri/PostLogoutRedirectUri`, and the current administrator allowlist must be configured; `IdentityService:ClockSkewSeconds` still participates in startup validation but is fixed at 30 seconds at runtime (the in-package value). The administrator allowlist is enforced through the package's PreSignInAuthorizationDecision (before a new session signs in) and AuthorizationDecision (when session state is read) adapters: a subject not on the allowlist is not signed in and gets no ticket and no Cookie. The five legacy keys `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` no longer select the run mode: remove them; only omission or the canonical lowercase `true` passes migration validation, and an explicit `false`, an empty value, or a malformed value is rejected at startup in every environment. The password entry point is permanently `410 legacy_login_disabled`; API/image/three-proxy/association queries always use the same administrator session and CSRF boundary, any inbound Authorization gets a fixed 401; there is no capability switch and no browser JWT fallback mode.

Missing or invalid required authentication configuration is rejected before any host, migration, worker, or listener is created; no disabled instance is created. For configuration and the read-only preflight, see [Deployment.md](development/Deployment.md#signacore-hosted-login-the-only-mode).

| Anonymous entry point | Behavior |
|---|---|
| `GET /api/auth/oidc/start?returnUrl=/students` | Package endpoint: reads Discovery and then initiates the Confidential code + PKCE S256 flow; scope is only `openid profile`, the return path is a single-value local path defaulting to `/`; Discovery failure redirects to `/login?authError=identity_unavailable` |
| `GET /api/auth/oidc/callback` | Package endpoint: single-value state/iss/code/error checks, atomic consumption of the 5-minute server-side pending state, strict validation of the RS256 ID Token and (after the configuration gate) the at+jwt access token plus iss/sub correlation; the ticket and Cookie are written only after the allowlist Decision passes |
| `GET /api/auth/session` | Always returns only the three fields `authenticated/displayName/requiresReauthentication`; anonymous/bad ticket false/null/false, valid token expired false/display name/true, no lifetime extension; a Bearer is never treated as this session |

Return targets allow only absolute in-site paths and reject external, encoded-external, control-character, backslash, and `/api/auth/*` and `/login` loops; on an invalid target it falls back to `/dashboard`. Cancelling returns `/login?authError=cancelled`; an unavailable Discovery/JWKS in start returns `identity_unavailable`; callback protocol/redemption/signature and similar failures uniformly return `sign_in_failed` without reflecting error_description or exceptions. state is never reused whether the flow succeeds or fails; a failed redemption must start again and cannot retry the old code. Both the entry and redirect responses are no-store/no-cache/no-referrer.

The session Cookie (name `adminSession`) is HttpOnly, Path=/, SameSite=Lax, Secure (with an http entry scheme the package Cookie has no Secure flag; switching the entry scheme requires re-login), and its value is the opaque key of the in-package ticket store; the access/id token, ticket principal, and deadlines live only in single-process memory. The session lifetime does not exceed access-token expiry; expired tickets are reclaimed by the store on read, and expiry is indistinguishable from absence. Request cancellation publishes no partial ticket; the store is bounded (default capacity 10000) and reclaimed periodically, and restarts/multiple replicas do not support state continuation. The display name comes from nickname/name, null when absent; ID Token roles do not enter the business-boundary principal (the accessor rebuilds only iss/sub/display_name). Challenges/rejections are handled by the package session scheme (challenge redirects to start, forbid is 403), while the business surface still short-circuits with 401/403 JSON. The base Code request contains no offline_access/refresh; SPA logout goes through the prepared logout described below.

### Admin session and CSRF (#42)

The API always uses AdminSession; the legacy keys exist only for startup migration validation and cannot restore any legacy credential path.

Management paths first explicitly read the server AdminSession ticket, require a unique `iss/sub` matching the validated ticket stamp, and require the issuer to be exactly equal to the Authority; authorization uses only the **current** `AdminPortal:AdminUserIds` (case-insensitive IDs) and does not trust ID Token roles or browser headers. Any inbound `Authorization` (empty, repeated, valid or invalid) gets a fixed 401, with no fallback to the new Cookie or the old `adminAuthToken`. Missing/expired/lost ticket, missing access token, or invalid/expired `expires_at` yields `401 {"error":"unauthorized"}`; a valid identity not on the allowlist yields `403 {"error":"forbidden"}`; both are JSON, never an HTML redirect. The 8-hour ticket lifetime cannot extend the access-token deadline, and there is currently no refresh.

`GET /api/auth/csrf` requires the same administrator session and a valid token and rejects Authorization. Success returns `200 {"requestToken":"..."}`, no-store/no-cache, and sets the separate `adminCsrf` Cookie (HttpOnly, Path=/, SameSite=Lax; the Secure policy derives from the configured entry scheme: Secure for an https entry, `SameAsRequest` for an http entry). The request token is bound to the principal and the CSRF Cookie and never outputs the server access/id token.

All management write methods (everything other than GET/HEAD/OPTIONS/TRACE, including POST/PUT/PATCH/DELETE and OSS trigger/resolve/batch-resolve) explicitly validate framework antiforgery before controller/business/outbound/deletion, requiring a single-value `X-CSRF-TOKEN`. A missing/wrong/duplicated header, wrong Cookie, token bound to a different principal, or a form-token-only submission yields `400 {"error":"csrf_invalid"}`; a successful CSRF check only continues the current request and never replays write requests. The audit's existing single-record and non-empty batch `resolve` requests both run the same collector's reference check against the three services; when v1 validation is complete it returns 409, when references are unavailable it returns 502, and deletion is always refused; anonymous claims/OIDC callback do not get this browser CSRF boundary. The default CORS is unchanged and cross-origin Cookies are not enabled.

`POST /api/auth/login` returns a fixed `410 {"error":"legacy_login_disabled"}` before reading the password or doing model binding, forwards nothing to Identity, and issues no JWT Cookie. Logout goes through the package endpoint `POST /api/auth/oidc/logout` (see Prepared logout); non-POST gets a fixed 405 with no ticket revocation or preparation, and every POST attempt clears the old `adminAuthToken` Cookie. The three proxies and association queries use only the in-ticket server token. SPA/non-API stays anonymous, and health semantics are unchanged.

### Retired password login

`POST /api/auth/login` permanently returns `410 {"error":"legacy_login_disabled"}`. All valid configurations and case/trailing-slash variants, empty or malformed bodies, and any Cookie/Authorization are rejected before authentication and model binding; the password is never read, no Identity token is redeemed, no Cookie is issued, and no access/refresh token is returned. Configuration cannot restore the password entry point. Use SignaCore hosted login via `/api/auth/oidc/start`.

The `{ "userId": "..." }` → `{ "roles": ["admin"] }` allowlist contract of `POST /api/auth/callback` is retained; non-allowlisted users get empty roles. The SPA and non-API paths stay anonymous. Rolling back to an old complete image is a separate deployment action; this version cannot resurrect password login or revoked tickets. The final release must include the hosted-login SPA from #41; intermediate images that still contain the old password form are not release candidates.

---

## Table of contents

1. [Enum options](#enum-options)
2. [Identity accounts](#identity-accounts)
3. [Images](#images)
4. [Mistakes](#mistakes)
5. [OSS audit](#oss-audit)
6. [OSS upload records](#oss-upload-records)
7. [Students](#students)
8. [Teacher/assistant linked accounts](#teacherassistant-linked-accounts)

---

## Enum options

### Get all enum options

Returns all enum options available in the system, including upload statuses, grades, subjects, classifications, and review statuses.

**Endpoint:** `GET /api/admin/enum-options`

**Response example:**

```json
{
  "uploadStatuses": [
    { "value": 0, "name": "Unspecified", "displayName": "Not specified" },
    { "value": 1, "name": "Uploaded", "displayName": "Uploaded" }
  ],
  "grades": [...],
  "subjects": [...],
  "classifications": [...],
  "reviewStatuses": [...]
}
```

> **Change**: since June 2026 the `mistakeTypes` field is no longer returned; mistake error types have been taken offline (the field's definition and decommission record are maintained in the shared constants documentation of the Ruoyu.Study repository).

---

## Identity accounts

### Batch-get identity account information

Batch-fetches identity account information by a list of account IDs.

**Endpoint:** `POST /api/admin/identity-accounts/batch`

**Request body:**

```json
{
  "accountIds": ["550e8400-e29b-41d4-a716-446655440000", "..."]
}
```

**Response example:**

```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "username": "john_doe",
    "displayName": "Zhang San",
    "phone": "13800138000",
    "remark": "Remark"
  }
]
```

### Get student list by identity account

Returns all students linked to the given identity account.

**Endpoint:** `GET /api/admin/accounts/{accountId:guid}/students`

**Path parameters:**
- `accountId` (UUID): identity account ID

**Response example:**

```json
[
  {
    "id": "string",
    "name": "Zhang San",
    "grade": 1,
    "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
    "createdAt": 1234567890,
    "updatedAt": 1234567890
  }
]
```

---

## Images

### Get image

Fetches an image file from OSS storage.

**Endpoint:** `GET /api/admin/image`

**Query parameters:**
- `path` (string, required): OSS object path

**Response:** binary image stream

---

## Mistakes

When `MistakeService:UseSessionToken=true`, the management calls below plus upload classification/LegacyCheck/LegacyClean/mistake images share the current server ticket for the outbound call; any inbound Authorization still gets 401 and unsafe methods still validate CSRF first. An invalid ticket/deadline before sending yields 401 (valid-token expiry `reauthentication_required`), a current-allowlist rejection yields 403, and both mean zero Mistake HTTP.

Target 401/403/3xx, illegal/missing required data, JSON/TLS/network/timeout, or unknown write results yield a safe 502. JSON controllers use fixed ProblemDetails: `mistake.unavailable` (502), `mistake.request_rejected` (legitimate business failure 400), `mistake.conflict` (legitimate business conflict 409); images use the same statuses with a fixed safe error, legitimate results are a signed-URL redirect, and the URL carries no server Bearer. Real detail-GET 404 and legitimate empty lists are preserved and target rejections are never disguised as empty DTOs. Cancellation propagates, each unsafe method sends the application at most once, a failure stops later Student/S3 writes, there is no refresh/replay; the commit state of an already-sent write may be unknown. The default false keeps the original Mistake downstream compatibility and does not change management inbound identity.

### Get mistake list

Returns a paginated list of mistake records with multiple filter conditions.

**Endpoint:** `GET /api/admin/mistakes`

**Query parameters:**
- `studentId` (string, optional): student ID
- `subject` (int, optional): subject
- `grade` (int, optional): grade
- `reviewStatus` (int, optional): review status
- `page` (int, optional, default: 1): page number
- `size` (int, optional, default: 20, max: 100): page size

**Response example:**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "Zhang San",
      "subject": 1,
      "grade": 1,
      "sourceUploadId": "string",
      "reviewStatus": 0,
      "reviewerId": "string",
      "reviewedAt": 1234567890,
      "reviewComment": "string",
      "type": 0,
      "questionId": "string",
      "createdAt": 1234567890,
      "updatedAt": 1234567890,
      "imageCount": 1,
      "firstImagePath": "string"
    }
  ],
  "total": 100,
  "page": 1,
  "pageSize": 20,
  "totalPages": 5
}
```

### Get single mistake detail

Returns the details of the given mistake ID.

**Endpoint:** `GET /api/admin/mistakes/{id}`

**Path parameters:**
- `id` (string): mistake ID

**Response example:**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "Zhang San",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0,
  "reviewerId": "string",
  "reviewedAt": 1234567890,
  "reviewComment": "string",
  "type": 0,
  "questionId": "string",
  "createdAt": 1234567890,
  "updatedAt": 1234567890,
  "sourceRegions": [
    {
      "sourceImagePath": "string",
      "boundingBox": {
        "x1": 0,
        "y1": 0,
        "x2": 100,
        "y2": 100
      }
    }
  ]
}
```

### Get mistakes by upload record

Returns all mistakes linked to the given upload record.

**Endpoint:** `GET /api/admin/mistakes/by-upload/{uploadId}`

**Path parameters:**
- `uploadId` (string): upload record ID

**Response example:**

```json
{
  "items": [...],
  "total": 10
}
```

### Update mistake information

Updates the information of the given mistake.

**Endpoint:** `PUT /api/admin/mistakes/{id}`

**Path parameters:**
- `id` (string): mistake ID

**Request body:**

```json
{
  "studentId": "string",
  "subject": 1,
  "grade": 1
}
```

**Response example:**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "Zhang San",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0
}
```

---

## OSS audit

Detailed rules are in [StorageAudit](./modules/OssAudit/StorageAudit.md); the upstream v1 field specification is maintained by Ruoyu.Study.

| Method and path | Current behavior |
|---|---|
| GET `/api/admin/oss-audit/records` | Paginated by page/pageSize with optional status/bucket filters; keeps items/totalCount/statusCounts/bucketCounts. Status 3 is UnreferencedObservation; bucketCounts counts both historical status 0 and the new status 3 |
| GET `/api/admin/oss-audit/status` | isRunning, lastCompleted/lastFailed, pendingCount, observationCount, deletionAuthorized=false; completed runs include referenceContractVersion/referenceSnapshots (the snapshot metadata JSON of the three services) |
| POST `/api/admin/oss-audit/trigger` | Starts a run asynchronously after the existing authentication and CSRF checks; returns 400 while a run is in progress, otherwise 200 OperationResponse |
| POST `/api/admin/oss-audit/records/{id}/resolve` | For an existing record of any status 0/1/2/3, first runs the full check with the reference collector; after successful v1 validation it always returns 409 cleanup_not_authorized, and when dependencies are incomplete it returns 502 references_unavailable. Unknown ID returns 404; nothing is written to S3 and no audit record is deleted |
| POST `/api/admin/oss-audit/records/batch-resolve` | Non-empty IDs use the same collector and return 409/502 depending on reference completeness, including requests where some or all IDs do not exist; empty IDs return 400. Nothing is written to S3 and no audit record is deleted |
| POST `/api/admin/oss-audit/records/{id}/ignore` | body `{note}`; only historical status 0 is updated to 2 with ResolvedAt/Note recorded; other statuses return 400 and unknown IDs return 404 |

Rejection responses are `{success:false,errorKind:...}`. The `NewZombieCount` compatibility field of new runs counts only newly added read-only observations; old runs (version=null) keep the original historical meaning. v1 provides no garbage collection and grants no physical deletion permission.

---

## OSS upload records

### Get all upload records

Returns all upload records paginated, filterable by status and student.

**Endpoint:** `GET /api/admin/oss-upload-records`

**Query parameters:**
- `page` (int, optional, default: 1): page number
- `pageSize` (int, optional, default: 20): page size
- `status` (int, optional, default: -1): upload status (-1 means all)
- `studentId` (string, optional): student ID

**Response example:**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "Zhang San",
      "status": 1,
      "imagePaths": ["path/to/image.jpg"],
      "comments": "string",
      "createdAt": 1234567890,
      "updatedAt": 1234567890,
      "imageRotations": []
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

### Get business entities linked to an upload record

Returns the mistake/homework entries linked to the given upload record, used to show the record's classification ownership.

**Endpoint:** `GET /api/admin/oss-upload-records/{id}/linked-items`

**Path parameters:**
- `id` (string): upload record ID

**Response example:**

```json
{
  "mistakeItems": [
    {
      "id": "string",
      "subject": 1,
      "grade": 1,
      "reviewStatus": 0,
      "imageIndices": [0, 1]
    }
  ],
  "homeworkItems": [],
  "remainingImageCount": 7,
  "totalImageCount": 9
}
```

> **Note**: `imageIndices` indicates which images of the upload record the business entity references (**original indices**, i.e. the upload-time order, unaffected by removals). `remainingImageCount` is the number of images remaining in `image_paths`, and `totalImageCount` is the total number of images at original upload.

### Reset upload record status

Resets the status of an upload record.

**Endpoint:** `POST /api/admin/oss-upload-records/{id}/reset-status`

**Path parameters:**
- `id` (string): upload record ID

**Request body:**

```json
{
  "studentId": "string",
  "targetStatus": 1
}
```

**Response example:**

```json
{
  "success": true,
  "message": "Status reset successfully"
}
```

### Assign upload records at image granularity

Assigns the specified images of an upload record to mistake entries. One request can contain multiple assignments; the images within each assignment share one mistake (one `mistake_item`).

**Endpoint:** `POST /api/admin/oss-upload-records/{id}/assign`

**Path parameters:**
- `id` (string): upload record ID

**Request body:**

```json
{
  "studentId": "string",
  "assignments": [
    {
      "subject": 1,
      "grade": 7,
      "imageIndices": [0, 1],
      "comments": "Images 1 and 2 are the same Chinese mistake"
    },
    {
      "subject": 3,
      "grade": 7,
      "imageIndices": [2],
      "comments": "Image 3 is a separate English mistake"
    }
  ]
}
```

**Request field notes:**
- `studentId` (string, required): student ID
- `assignments` (array, required): the list of assignment entries
  - `subject` (int, required): subject (1-9)
  - `grade` (int, required): grade (1-12)
  - `imageIndices` (array, required): the image indices referenced by this assignment (based on the upload record's original indices). `imageIndices` must contain at least 1 element.
  - `comments` (string, optional): remark

**Core semantics**:
- **One assignment = one mistake = one `mistake_item`**.
- The multiple images within one assignment (for example question + solution) are packed into the same `mistake_item`'s `source_regions`.
- Multiple assignments can specify different `subject` / `grade` each (enabling cross-subject assignment).
- The same upload record can be assigned multiple times (each request creates new `mistake_item`s).
- Any image already approved for review (i.e. not present in `image_upload_record.image_paths`) cannot be assigned again.

**Response example:**

```json
{
  "success": true,
  "message": "Assignment successful",
  "createdItems": [
    {
      "subject": 1,
      "grade": 7,
      "itemId": "mistake-item-id-1",
      "imageCount": 2
    },
    {
      "subject": 3,
      "grade": 7,
      "itemId": "mistake-item-id-2",
      "imageCount": 1
    }
  ],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**Response field notes:**
- `createdItems[]` (array): one element per assignment
  - `subject` (int): subject
  - `grade` (int): grade
  - `itemId` (string): the ID of the newly created `mistake_item`
  - `imageCount` (int): the number of images referenced by the item
- `remainingImageCount` (int): the number of images remaining in `image_upload_record.image_paths` after assignment (used by the frontend to decide whether to continue assigning)
- `totalImageCount` (int): the original total number of images in the upload record

> **Follow-up flow**: the assigned `mistake_item` enters `PendingReview` for teacher review. After the teacher completes the whole batch review (`CompleteUploadReview`):
> 1. `Confirmed` images are copied to `mistakes/`.
> 2. The paths of both `Rejected` and `Confirmed` images are removed from `image_upload_record.image_paths`.
> 3. When `image_paths` becomes empty, the whole upload record is deleted automatically.

### Get upload record image

Fetches an image from an upload record.

**Endpoint:** `GET /api/admin/oss-upload-records/image`

**Query parameters:**
- `path` (string, required): image path

**Response:** binary image stream

### Rotate upload record image

Rotates an image in an upload record.

**Endpoint:** `POST /api/admin/oss-upload-records/{id}/rotate`

**Path parameters:**
- `id` (string): upload record ID

**Request body:**

```json
{
  "studentId": "string",
  "imageIndex": 0,
  "rotation": 90
}
```

**Response example:**

```json
{
  "success": true
}
```

---

## Students

### Get student list

Returns students paginated, filterable by name and grade.

**Endpoint:** `GET /api/admin/students`

**Query parameters:**
- `name` (string, optional): student name
- `grade` (int, optional): grade
- `page` (int, optional, default: 1): page number
- `pageSize` (int, optional, default: 20, max: 100): page size

**Response example:**

```json
{
  "items": [
    {
      "id": "string",
      "name": "Zhang San",
      "grade": 1,
      "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
      "createdAt": 1234567890,
      "updatedAt": 1234567890
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

### Get grade options

Returns all available grade options.

**Endpoint:** `GET /api/admin/students/grades`

**Response example:**

```json
[
  {
    "value": 1,
    "label": "Primary school grade 1"
  }
]
```

### Get student detail

Returns the details of the given student.

**Endpoint:** `GET /api/admin/students/{studentId:guid}`

**Path parameters:**
- `studentId` (UUID): student ID

**Response example:**

```json
{
  "id": "string",
  "name": "Zhang San",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
  "createdAt": 1234567890,
  "updatedAt": 1234567890
}
```

### Create student

Creates a new student record.

**Endpoint:** `POST /api/admin/students`

**Request body:**

```json
{
  "name": "Zhang San",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"]
}
```

**Response example:**

```json
{
  "id": "string",
  "name": "Zhang San",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
  "createdAt": 1234567890,
  "updatedAt": 1234567890
}
```

### Update student

Updates student information.

**Endpoint:** `PUT /api/admin/students/{studentId:guid}`

**Path parameters:**
- `studentId` (UUID): student ID

**Request body:**

```json
{
  "name": "Zhang San",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"]
}
```

**Response example:**

```json
{
  "success": true,
  "message": "Student updated successfully."
}
```

### Delete student

Deletes a student record.

**Endpoint:** `DELETE /api/admin/students/{studentId:guid}`

**Path parameters:**
- `studentId` (UUID): student ID

**Response example:**

```json
{
  "success": true,
  "message": "Student deleted."
}
```

### Get identity accounts linked to a student

Returns all identity account IDs linked to the given student.

**Endpoint:** `GET /api/admin/students/{studentId:guid}/accounts`

**Path parameters:**
- `studentId` (UUID): student ID

**Response example:**

```json
["550e8400-e29b-41d4-a716-446655440000"]
```

### Link an identity account to a student

Links an identity account to a student.

**Endpoint:** `POST /api/admin/students/{studentId:guid}/accounts`

**Path parameters:**
- `studentId` (UUID): student ID

**Request body:**

```json
{
  "identityAccountId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Response example:**

```json
{
  "success": true,
  "message": "Identity account linked to student successfully."
}
```

### Unlink an identity account from a student

Unlinks an identity account from a student.

**Endpoint:** `DELETE /api/admin/students/{studentId:guid}/accounts/{accountId:guid}`

**Path parameters:**
- `studentId` (UUID): student ID
- `accountId` (UUID): identity account ID

**Response example:**

```json
{
  "success": true,
  "message": "Identity account unlinked from student."
}
```

### Get student open subjects

Returns the student's open subject configuration.

**Endpoint:** `GET /api/admin/students/{studentId:guid}/open-subjects`

**Path parameters:**
- `studentId` (UUID): student ID

**Query parameters:**
- `activeOnly` (bool, optional, default: false): return only active subjects

**Response example:**

```json
[
  {
    "id": "string",
    "subject": 1,
    "openStartDate": "2024-01-01",
    "openEndDate": "2024-12-31",
    "isActive": true
  }
]
```

### Set student open subjects

Sets the student's open subject configuration.

**Endpoint:** `PUT /api/admin/students/{studentId:guid}/open-subjects`

**Path parameters:**
- `studentId` (UUID): student ID

**Request body:**

```json
{
  "subjects": [
    {
      "subject": 1,
      "openStartDate": "2024-01-01",
      "openEndDate": "2024-12-31"
    }
  ]
}
```

**Response example:**

```json
{
  "success": true,
  "message": "Open subjects updated successfully."
}
```

### Get subject options

Returns all available subject options.

**Endpoint:** `GET /api/admin/students/subject-options`

**Response example:**

```json
[
  {
    "value": 1,
    "name": "Math",
    "displayName": "Math"
  }
]
```

### Get teachers/assistants linked to a student

Queries all teachers and assistants linked to the student by ID (via the student's identityAccountIds against TeacherPortal/AssistantPortal).

**Endpoint:** `GET /api/admin/students/{studentId:guid}/linked-accounts`

**Path parameters:**
- `studentId` (UUID): student ID

**Response example:**

```json
{
  "teachers": [
    {
      "userId": "550e8400-e29b-41d4-a716-446655440000",
      "displayName": "Teacher Zhang",
      "phone": "13800138000",
      "subjects": "1,3",
      "role": "Teacher"
    }
  ],
  "assistants": [
    {
      "userId": "660e8400-e29b-41d4-a716-446655440001",
      "displayName": "Assistant Li",
      "phone": "13900139000",
      "subjects": "2",
      "role": "Assistant"
    }
  ]
}
```

> **Note**: when TeacherPortal or AssistantPortal is unavailable, the corresponding list degrades to an empty array.

---

## Teacher/assistant linked accounts

### Change a teacher's linked account

Changes the identity account currently linked to a teacher.

**Endpoint:** `PUT /api/teacher-portal/admin/teachers/{userId}/user-id`

**Path parameters:**
- `userId` (string): the user ID currently linked to the teacher

**Request body:**

```json
{
  "newUserId": "660e8400-e29b-41d4-a716-446655440001"
}
```

**Response example:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "userId": "660e8400-e29b-41d4-a716-446655440001",
    "phone": "13800138000",
    "username": "Teacher Zhang",
    "subjects": [1, 3],
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

**Error cases:**
- NewUserId is already taken by another teacher: `{ "success": false, "message": "NewUserId is already bound to another teacher" }`
- Teacher not found: `{ "success": false, "message": "Teacher not found" }`

### Change an assistant's linked account

Changes the identity account currently linked to an assistant.

**Endpoint:** `PUT /api/assistant-portal/admin/assistants/{userId}/user-id`

**Path parameters:**
- `userId` (string): the user ID currently linked to the assistant

**Request body:**

```json
{
  "newUserId": "770e8400-e29b-41d4-a716-446655440002"
}
```

**Response example:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "userId": "770e8400-e29b-41d4-a716-446655440002",
    "phone": "13900139000",
    "username": "Assistant Li",
    "isActive": true,
    "subjects": [2],
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

**Error cases:**
- NewUserId is already taken by another assistant: `{ "success": false, "message": "NewUserId is already bound to another assistant" }`
- Assistant not found: `{ "success": false, "message": "Assistant not found" }`

---

## Error responses

All error responses follow this format:

```json
{
  "message": "Error description"
}
```

Common HTTP status codes:
- `400 Bad Request`: invalid request parameters
- `404 Not Found`: resource does not exist
- `500 Internal Server Error`: internal server error
- `502 Bad Gateway`: downstream service unavailable

### Prepared logout (#46; owned by the package since #83)

Logout always uses prepared logout, handled by the package endpoint `POST /api/auth/oidc/logout`. The required `AdminOidc:PostLogoutRedirectUri` is same-origin with the RedirectUri, with the fixed path `/api/auth/oidc/logout/return` and no query/fragment/userinfo; the maintainer must change the PostLogout callback in the SignaCore application registration to this value. http and https are accepted equally (structural rules unchanged), and public deployments should use HTTPS (a recommendation, not enforced). Non-POST logout gets a fixed 405 (the local guard runs before the authorization pipeline), and every POST attempt clears the old `adminAuthToken` Cookie.

The SPA first GETs `/api/auth/oidc/csrf` (publicly issued; the response is `{"token":"..."}` and rotates the shared `adminCsrf` Cookie; a user-neutral pair sharing the same Cookie as the business-principal-bound pair), then submits `__RequestVerificationToken` via a navigational form POST to `/api/auth/oidc/logout`; antiforgery validation also accepts a single-value `X-CSRF-TOKEN` header. A missing/wrong token yields the fixed `400 {"outcome":"csrf_rejected"}` without touching the session. Logout itself is never blocked by access-token expiry or the administrator allowlist (a ticket is enough); with no session Cookie it directly returns `200 {"outcome":"local_only"}`.

Inside a serialized gate keyed by the session, the package first removes the server ticket and deletes the session Cookie (local revocation first, never rolled back, with at most one revocation and one preparation under concurrent logouts), then sends server-side HTTP Basic authentication, the server-retained id_token_hint, the exact PostLogout URI, and a one-time state to the Authority's `/oauth2/logout/requests`, with no automatic retry/redirect following and no ID token/secret sent to the browser; the backchannel shares a named client with the rest of the package protocol traffic and propagates the correlation id, but does not enter telemetry.

Success is a 302 to the validated same-origin `logout_uri` (the unique 43-character base64url `logout_handle`); preparation failure, malformed/cross-origin/oversized URL, error status, timeout, or cancellation all keep the local logout with the fixed response `200 {"outcome":"local_only"}` (shown directly as that JSON when the browser submits by navigation). The return `GET /api/auth/oidc/logout/return?state=...` consumes, atomically and once only, a single-value state plus the separate `adminSession-logout-return` binding Cookie (HttpOnly/Secure/SameSite=Lax/Path=`/api/auth/oidc/logout`, 5 minutes); the correct case is a 302 to `/login`, everything else a fixed 400 HTML page. Tokens already issued downstream are not guaranteed to be invalidated immediately. The real PostLogout registration change is performed by the maintainer at deployment; the dedicated `PreparedLogout_` tests use an isolated database and fictitious credentials, with no production OSS deletion.

### Token expiry and explicit reauthentication (#45)

In the only-session mode, tickets are reclaimed by the store when the access token expires, and expiry is indistinguishable from absence: the business gate returns `401 {"error":"unauthorized"}`, with zero downstream/OSS deletion and no write replay (since #83 there is no separate reauthentication_required business error; the SPA treats an expired session as anonymous and redirects to login). `GET /api/auth/session` returns `{authenticated,displayName,requiresReauthentication}`: valid token true/false; ticket missing but session Cookie still present (expired/revoked) false/true with null display name; never logged in false/false. Non-admin 403 takes precedence; any Authorization gets 401. The browser only enters the controlled authorization at `/api/auth/oidc/start?returnUrl=...` explicitly (returnUrl is a single-value local path defaulting to `/`); when the upstream session can be reused it immediately returns, otherwise the hosted login is shown; cancel/failure keeps the existing fixed results (cancelled/sign_in_failed/identity_unavailable mappings unchanged), and the BFF never auto-challenges.

### Portal server-side credentials (#44)

The full Teacher/Assistant proxy prefixes always share the administrator/CSRF gate before authentication/authorization; a valid access token at expiry returns `401 reauthentication_required` with zero outbound, only the server Bearer is sent, and browser Cookie, Host, CSRF, and gateway headers plus downstream Set-Cookie are stripped. The admin/auth/other path mappings, query/body, 503/502, and downstream 401/403 are preserved; cancellation propagates without replay. Association queries share the same administrator boundary, forward only the server token, and reject before the Student/portal calls on failure. A single-sided business failure or malformed response keeps that side an empty list while the other side is normal; this aggregation guarantees neither failure visibility nor cross-portal consistency. Production rollout is not in #75's implementation scope; formal verification of the current combination uses the official Provider and real downstream. The dedicated `PortalSession_` and `AssociationsSession_` suites run on an isolated database/fake HTTP and never touch production.

### Identity proxy server-side session (#43)

The full `/api/identity` prefix (case, root, and trailing slash) passes the shared administrator and CSRF boundary before authentication/authorization. Any inbound Authorization is rejected; a valid access token at expiry returns `401 reauthentication_required` with zero outbound; only the valid in-ticket server access token is forwarded, and after stripping browser Cookie, Host, X-CSRF-TOKEN, and forged gateway headers, this service's own AppId/AppSecret is injected. Downstream Set-Cookie does not reach the browser; 401/403/structured 503 and Retry-After pass through as-is, network failure gives 502, and cancellation propagates without replay. AppSecret stripping always remains, and the three proxies share the same session boundary. Production platform registration is not performed by this item; the formal combination gate belongs to #75. The dedicated `FullyQualifiedName~IdentitySession_` tests use fake HTTP and an isolated database, with no production OSS deletion.

Identity proxy retirement of browser-credential compatibility (#73): the middleware's settings are a required dependency, and direct invocation lacking TrustedSessionKey/a valid server token is also rejected with 401. All paths map `/api` using the StartsWithSegments remainder; root/trailing slash/case/query/body are preserved; RequestAborted always applies, and pre-cancellation/cancellation mid-send propagate with no application-layer retry. The non-proxied `IdentityAccountsController` is out of scope for that change. Intermediate images are not deployed; the final must include #41/#75; rollback selects only a known-good old complete image and does not resurrect tickets.

Portal transport retirement of browser-credential compatibility (#74): the two middleware accept only a valid server token from TrustedSessionKey, settings are a required dependency, and direct invocation lacking a trusted item is also 401. The unified remainder uses the three admin/auth/other mappings, always applies RequestAborted, strips browser Authorization/Cookie/Host/CSRF/gateway, and isolates Set-Cookie; cancellation propagates with no application-layer retry. Association queries ride only with the API-session and use the server token even when the portal proxy switch is false; the current-account filter, one-side failure keeping the other side, no-association short circuit, and Student failure response are preserved. This intermediate image is not deployed; the final must combine #41/#75; a full-version rollback does not restore this version's browser-credential path or resurrect revoked tickets.

### Management endpoints uniformly use the local session (#72)

`/api/admin/*` (including native image), ordinary CSRF, the three proxies, and association queries always share the AdminSession boundary; Authorization 401, allowlist 403, deadline 401, ordinary unsafe CSRF 400, and the three-field session status all hold. No global inbound Bearer handler or JWT Cookie validation is registered; the default authentication and FallbackPolicy also select only the server session.

Logout-related endpoints (`/api/auth/oidc/csrf|logout|logout/return`) are provided by the SignaCore package and do not carry the six-header security baseline (their answers carry their own no-store; antiforgery issuance may attach X-Frame-Options: SAMEORIGIN); non-POST logout 405, POST always clears the old adminAuthToken. Logout identity is never blocked by access expiry or the administrator allowlist; success/failure/cancellation and the one-time state follow the package protocol. SPA/health are anonymous, with no change to business/OSS/three proxies. API+SPA must use the final complete integrated image; rollback selects only a known-good old complete image and does not resurrect revoked ticket state.
