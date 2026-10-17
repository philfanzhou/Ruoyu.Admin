# AccountLinking — Test Plan (TESTS)

Test tooling: `xUnit + Moq + FluentAssertions`. The existing model test file: `test/Admin.WebApi.Tests/Models/ModelTests.cs`.

## Existing tests

### Model constructor tests (implemented)

| Test class | Test method | What it verifies |
| --- | --- | --- |
| `LinkUserRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | LinkUserRequest.IdentityAccountId assigned correctly |
| `IdentityAccountDtoTests` | `Constructor_ShouldSetPropertiesCorrectly` | all IdentityAccountDto properties assigned correctly |
| `IdentityAccountDtoTests` | `Constructor_WithEmptyValues_ShouldAcceptEmptyStrings` | works when all fields are empty strings |

## Implemented tests

### AccountLinkingTests (StudentsController link/unlink)

| Test method | What it verifies |
| --- | --- |
| `GetIdentityAccountsByStudentId_Success` | UT-01 querying a student's linked identity account IDs succeeds |
| `LinkIdentityAccountToStudent_Success` | UT-02 linking an identity account succeeds |
| `LinkIdentityAccount_EmptyIdentityAccountId_Returns400` | UT-03 linking — empty IdentityAccountId |
| `LinkIdentityAccount_InvalidGuid_Returns400` | UT-04 linking — illegal GUID format |
| `LinkIdentityAccount_GrpcSuccessFalse_Returns404` | UT-05 linking — gRPC returns Success=false |
| `LinkIdentityAccount_GrpcInvalidArgument_Returns400` | UT-06 linking — gRPC InvalidArgument |
| `UnlinkIdentityAccount_Success` | UT-07 unlinking an identity account succeeds |
| `UnlinkIdentityAccount_GrpcSuccessFalse_Returns404` | UT-08 unlinking — gRPC returns Success=false |
| `UnlinkIdentityAccount_GrpcInvalidArgument_Returns400` | UT-09 unlinking — gRPC InvalidArgument |
| `LinkIdentityAccount_WhitespaceOnlyIdentityAccountId_Returns400` | EX-01 whitespace-only IdentityAccountId → 400 |

### IdentityAccountsControllerTests

| Test method | What it verifies |
| --- | --- |
| `GetIdentityAccountsBatch_NullInput_ReturnsEmptyAccountsWithNoFailure` | null input returns an empty list |
| `GetIdentityAccountsBatch_EmptyInput_ReturnsEmptyAccountsWithNoFailure` | empty-list input returns an empty list |
| `GetIdentityAccountsBatch_MissingCredentials_ReturnsEmptyAccountsWithNoFailure` | unconfigured AppId returns an empty list |
| `GetIdentityAccountsBatch_Success_ReturnsFilteredAccounts` | a successful batch query returns the filtered accounts |
| `GetIdentityAccountsBatch_FiltersOutUsersNotInTargetIds` | filters out users not in targetIds |
| `GetIdentityAccountsBatch_CaseInsensitiveMatching` | case-insensitive matching |
| `GetIdentityAccountsBatch_NullFieldsDefaultToEmptyString` | null fields degrade to empty strings |
| `GetIdentityAccountsBatch_NonSuccessStatusCode_ReturnsEmptyAccountsWithNoFailure` | a failed HTTP request returns an empty list |
| `GetIdentityAccountsBatch_NullDeserializedResponse_ReturnsEmptyAccountsWithNoFailure` | a null deserialized response returns an empty list |
| `GetIdentityAccountsBatch_ExceptionDuringHttp_ReturnsPartialFailureWithWarning` | an HTTP exception returns partial failure with a warning logged |
| `GetStudentsByIdentityAccountId_Success_ReturnsStudentDtos` | reverse student lookup by identity account succeeds |
| `GetStudentsByIdentityAccountId_GrpcException_Propagates` | gRPC exceptions propagate upward |

## Unit tests — Given-When-Then format

### StudentsController link/unlink tests

#### UT-01 Query a student's linked identity account IDs successfully (verifies SPEC FR-01)

- **Given**: Mock `_grpcClient.GetIdentityAccountsByStudentIdAsync` returns a response with 2 AccountIds.
- **When**: `GetIdentityAccountsByStudentId(validGuid)` is called.
- **Then**: returns 200 with an `IReadOnlyList<string>` body, Count == 2.

#### UT-02 Link an identity account successfully (verifies SPEC FR-02)

- **Given**: Mock `_grpcClient.LinkIdentityAccountToStudentAsync` returns `Success=true`.
- **When**: `LinkIdentityAccountToStudent(validGuid, new LinkUserRequest(validAccountId))` is called.
- **Then**: returns 200 with body `OperationResponse(true, "Identity account linked to student successfully.")`.

#### UT-03 Link an identity account — empty IdentityAccountId (verifies SPEC FR-02)

- **Given**: `LinkUserRequest.IdentityAccountId` is an empty string.
- **When**: `LinkIdentityAccountToStudent` is called.
- **Then**: returns 400 with body `ErrorResponse("Identity Account ID is required.")`.

#### UT-04 Link an identity account — illegal GUID format (verifies SPEC FR-02)

- **Given**: `LinkUserRequest.IdentityAccountId` is "not-a-guid".
- **When**: `LinkIdentityAccountToStudent` is called.
- **Then**: returns 400 with body `ErrorResponse("Invalid Identity Account ID format. Must be a valid GUID.")`.

#### UT-05 Link an identity account — gRPC returns Success=false (verifies SPEC FR-06)

- **Given**: Mock `_grpcClient.LinkIdentityAccountToStudentAsync` returns `Success=false, ErrorMessage="Account not found"`.
- **When**: `LinkIdentityAccountToStudent` is called.
- **Then**: returns 404 with body `ErrorResponse("Account not found")`.

#### UT-06 Link an identity account — gRPC InvalidArgument (verifies SPEC FR-07)

- **Given**: Mock `_grpcClient.LinkIdentityAccountToStudentAsync` throws `RpcException(StatusCode.InvalidArgument, "Already linked")`.
- **When**: `LinkIdentityAccountToStudent` is called.
- **Then**: returns 400 with body `ErrorResponse("Already linked")`.

#### UT-07 Unlink an identity account successfully (verifies SPEC FR-03)

- **Given**: Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` returns `Success=true`.
- **When**: `UnlinkIdentityAccountFromStudent(validStudentId, validAccountId)` is called.
- **Then**: returns 200 with body `OperationResponse(true, "Identity account unlinked from student.")`.

#### UT-08 Unlink — gRPC returns Success=false (verifies SPEC FR-08)

- **Given**: Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` returns `Success=false, ErrorMessage="Mapping not found"`.
- **When**: `UnlinkIdentityAccountFromStudent` is called.
- **Then**: returns 404 with body `ErrorResponse("Mapping not found")`.

#### UT-09 Unlink — gRPC InvalidArgument (verifies SPEC FR-09)

- **Given**: Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` throws `RpcException(StatusCode.InvalidArgument)`.
- **When**: `UnlinkIdentityAccountFromStudent` is called.
- **Then**: returns 400.

### IdentityAccountsController tests

#### UT-10 Batch-query identity accounts successfully (verifies SPEC FR-04)

- **Given**: the HttpClient returned by the Mock `IHttpClientFactory` gets a successful response with 2 users after sending the request.
- **When**: `GetIdentityAccountsBatch(["id1", "id2"])` is called.
- **Then**: returns 200 with a `List<IdentityAccountDto>` body, Count == 2.

#### UT-11 Batch query — empty accountIds (verifies SPEC FR-10)

- **Given**: accountIds is an empty list.
- **When**: `GetIdentityAccountsBatch([])` is called.
- **Then**: returns 200 with an empty-list body.

#### UT-12 Batch query — AppId not configured (verifies SPEC FR-11)

- **Given**: `IdentityServiceOptions.AppId` is an empty string.
- **When**: `GetIdentityAccountsBatch(["id1"])` is called.
- **Then**: returns 200 with an empty-list body; no HTTP request is made.

#### UT-13 Batch query — HTTP request failure (verifies SPEC FR-10)

- **Given**: the Mock HttpClient returns a 500 status code.
- **When**: `GetIdentityAccountsBatch(["id1"])` is called.
- **Then**: returns 200 with an empty-list body.

#### UT-14 Batch query — exception catch (verifies SPEC FR-10)

- **Given**: the Mock HttpClient throws `HttpRequestException` when sending.
- **When**: `GetIdentityAccountsBatch(["id1"])` is called.
- **Then**: returns 200 with an empty-list body; ILogger receives a Warning.

#### UT-15 Batch query — targetIds filtering (verifies SPEC FR-12)

- **Given**: the request is `["id1", "id2"]`, but the Identity Service returns 3 users (id1, id2, id3).
- **When**: `GetIdentityAccountsBatch(["id1", "id2"])` is called.
- **Then**: returns 200 with only the DTOs for id1 and id2 in the body.

#### UT-16 Reverse-look-up students by identity account (verifies SPEC FR-05)

- **Given**: Mock `grpcClient.GetStudentsByIdentityAccountIdAsync` returns a response with 2 students.
- **When**: `GetStudentsByIdentityAccountId(validGuid, grpcClient)` is called.
- **Then**: returns 200 with an `IReadOnlyList<StudentDto>` body, Count == 2.

## Boundary and exception tests

- EX-01 whitespace-only IdentityAccountId → 400 (`IsNullOrWhiteSpace` check).
- EX-02 a user field returned by the Identity Service in a batch query is null → the corresponding DTO field is an empty string (`?? string.Empty`).
- EX-03 case-insensitive batch-query matching → requesting "ID1" matches the "id1" returned by the Identity Service.
- EX-04 a gRPC exception in GetStudentsByIdentityAccountId → not caught, propagates upward [inferred].

## Existing test mapping (to the SPEC requirements)

### Model tests

| Test method | SPEC items verified |
| --- | --- |
| `LinkUserRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-02 (model layer) |
| `IdentityAccountDtoTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04 (model layer) |
| `IdentityAccountDtoTests.Constructor_WithEmptyValues_ShouldAcceptEmptyStrings` | FR-04 (model layer, null degrades to an empty string) |

### Controller tests

| Test method | SPEC items verified |
| --- | --- |
| `GetIdentityAccountsByStudentId_Success` | FR-01 |
| `LinkIdentityAccountToStudent_Success` | FR-02 |
| `LinkIdentityAccount_EmptyIdentityAccountId_Returns400` | FR-02 |
| `LinkIdentityAccount_InvalidGuid_Returns400` | FR-02 |
| `LinkIdentityAccount_GrpcSuccessFalse_Returns404` | FR-06 |
| `LinkIdentityAccount_GrpcInvalidArgument_Returns400` | FR-07 |
| `UnlinkIdentityAccount_Success` | FR-03 |
| `UnlinkIdentityAccount_GrpcSuccessFalse_Returns404` | FR-08 |
| `UnlinkIdentityAccount_GrpcInvalidArgument_Returns400` | FR-09 |
| `GetIdentityAccountsBatch_Success_ReturnsFilteredAccounts` | FR-04 |
| `GetIdentityAccountsBatch_NullInput_ReturnsEmptyAccountsWithNoFailure` | FR-10 |
| `GetIdentityAccountsBatch_MissingCredentials_ReturnsEmptyAccountsWithNoFailure` | FR-11 |
| `GetIdentityAccountsBatch_NonSuccessStatusCode_ReturnsEmptyAccountsWithNoFailure` | FR-10 |
| `GetIdentityAccountsBatch_FiltersOutUsersNotInTargetIds` | FR-12 |
| `GetStudentsByIdentityAccountId_Success_ReturnsStudentDtos` | FR-05 |
