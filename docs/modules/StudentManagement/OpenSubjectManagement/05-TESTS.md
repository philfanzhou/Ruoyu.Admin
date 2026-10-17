# OpenSubjectManagement — Test Plan (TESTS)

Test tooling: `xUnit + Moq + FluentAssertions`. The existing model test file: `test/Admin.WebApi.Tests/Models/ModelTests.cs`.

## Existing tests

### Model constructor tests (implemented)

`ModelTests.cs` currently has **no** constructor tests for OpenSubjectDto, SetOpenSubjectsRequest, SubjectItem, or SubjectOption. The following models lack tests:

- `OpenSubjectDto`
- `SetOpenSubjectsRequest`
- `SubjectItem`
- `SubjectOption`

## Implemented tests

### OpenSubjectManagementTests (Controller tests)

| Test method | What it verifies |
| --- | --- |
| `GetStudentOpenSubjects_Success` | UT-01 querying a student's open subjects succeeds |
| `GetStudentOpenSubjects_ActiveOnlyTrue` | UT-02 querying a student's open subjects — activeOnly=true |
| `GetStudentOpenSubjects_EmptyEndDate_MapsToNull` | UT-03 querying a student's open subjects — OpenEndDate mapping |
| `SetStudentOpenSubjects_Success` | UT-04 setting a student's open subjects succeeds |
| `SetStudentOpenSubjects_InvalidSubjectValue_Returns400` | UT-05 setting open subjects — invalid subject value |
| `SetStudentOpenSubjects_EmptyOpenStartDate_Returns400` | UT-06 setting open subjects — empty OpenStartDate |
| `SetStudentOpenSubjects_InvalidOpenStartDateFormat_Returns400` | UT-07 setting open subjects — illegal OpenStartDate format |
| `SetStudentOpenSubjects_InvalidOpenEndDateFormat_Returns400` | UT-08 setting open subjects — illegal OpenEndDate format |
| `SetStudentOpenSubjects_GrpcSuccessFalse_Returns400` | UT-09 setting open subjects — gRPC returns Success=false |
| `SetStudentOpenSubjects_GrpcInvalidArgument_Returns400` | UT-10 setting open subjects — gRPC InvalidArgument |
| `SetStudentOpenSubjects_EmptySubjectsList_SkipsValidation_CallsGrpc` | EX-01 empty Subjects list → validation skipped, gRPC called directly |
| `SetStudentOpenSubjects_NullOpenEndDate_SendsEmptyStringInGrpcRequest` | EX-04 OpenEndDate is null → empty string in the gRPC request |

### OpenSubjectModelTests (model tests)

| Test method | What it verifies |
| --- | --- |
| `OpenSubjectDto_Constructor_SetsProperties` | the OpenSubjectDto constructor assigns correctly |
| `OpenSubjectDto_WithNullOpenEndDate_SetsProperty` | OpenSubjectDto sets correctly when OpenEndDate is null |
| `SubjectItem_Constructor_SetsProperties` | the SubjectItem constructor assigns correctly |
| `SetOpenSubjectsRequest_Constructor_SetsProperties` | the SetOpenSubjectsRequest constructor assigns correctly |
| `SubjectOption_Constructor_SetsProperties` | the SubjectOption constructor assigns correctly |

## Unit tests — Given-When-Then format

### Model tests

#### UT-M01 OpenSubjectDto constructor test

- **Given**: a valid Id, Subject=1, OpenStartDate="2024-01-01", OpenEndDate="2024-12-31", IsActive=true.
- **When**: `OpenSubjectDto` is created.
- **Then**: all properties are assigned correctly.

#### UT-M02 OpenSubjectDto constructor — OpenEndDate is null

- **Given**: OpenEndDate=null.
- **When**: `OpenSubjectDto` is created.
- **Then**: `OpenEndDate` is null.

#### UT-M03 SubjectItem constructor test

- **Given**: Subject=1, OpenStartDate="2024-01-01", OpenEndDate=null.
- **When**: `SubjectItem` is created.
- **Then**: all properties are assigned correctly.

#### UT-M04 SetOpenSubjectsRequest constructor test

- **Given**: Subjects contains 2 SubjectItems.
- **When**: `SetOpenSubjectsRequest` is created.
- **Then**: `Subjects.Count == 2`.

#### UT-M05 SubjectOption constructor test

- **Given**: Value=1, Name="Math", DisplayName="Mathematics".
- **When**: `SubjectOption` is created.
- **Then**: all properties are assigned correctly.

### Controller tests

#### UT-01 Query a student's open subjects successfully (verifies SPEC FR-01)

- **Given**: Mock `_grpcClient.GetStudentOpenSubjectsAsync` returns a response with 2 subjects.
- **When**: `GetStudentOpenSubjects(validGuid, activeOnly: null)` is called.
- **Then**: returns 200 with a `List<OpenSubjectDto>` body, Count == 2; the gRPC request has `ActiveOnly == false`.

#### UT-02 Query a student's open subjects — activeOnly=true (verifies SPEC FR-02)

- **Given**: Mock `_grpcClient.GetStudentOpenSubjectsAsync` returns a response with 1 active subject.
- **When**: `GetStudentOpenSubjects(validGuid, activeOnly: true)` is called.
- **Then**: returns 200; the gRPC request has `ActiveOnly == true`.

#### UT-03 Query a student's open subjects — OpenEndDate mapping (verifies SPEC FR-10)

- **Given**: in the Mock gRPC response, one subject's OpenEndDate is an empty string and another's is "2024-12-31".
- **When**: `GetStudentOpenSubjects` is called.
- **Then**: in the returned DTOs, the first OpenEndDate is null and the second is "2024-12-31".

#### UT-04 Set a student's open subjects successfully (verifies SPEC FR-03)

- **Given**: Mock `_grpcClient.SetStudentOpenSubjectsAsync` returns `Success=true`.
- **When**: `SetStudentOpenSubjects(validGuid, new SetOpenSubjectsRequest(subjects))` is called, subjects containing valid subjects.
- **Then**: returns 200 with body `OperationResponse(true, "Open subjects updated successfully.")`.

#### UT-05 Set open subjects — invalid subject value (verifies SPEC FR-04)

- **Given**: a SubjectItem with Subject=-1 (an invalid value).
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Invalid subject value: -1")`.

#### UT-06 Set open subjects — empty OpenStartDate (verifies SPEC FR-05)

- **Given**: a SubjectItem whose OpenStartDate is an empty string.
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Open start date is required")`.

#### UT-07 Set open subjects — illegal OpenStartDate format (verifies SPEC FR-06)

- **Given**: a SubjectItem whose OpenStartDate is "not-a-date".
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Invalid start date format: not-a-date")`.

#### UT-08 Set open subjects — illegal OpenEndDate format (verifies SPEC FR-07)

- **Given**: a SubjectItem whose OpenEndDate is "invalid-date".
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Invalid end date format: invalid-date")`.

#### UT-09 Set open subjects — gRPC returns Success=false (verifies SPEC FR-08)

- **Given**: Mock `_grpcClient.SetStudentOpenSubjectsAsync` returns `Success=false, ErrorMessage="Student not found"`.
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Student not found")`.

#### UT-10 Set open subjects — gRPC InvalidArgument (verifies SPEC FR-09)

- **Given**: Mock `_grpcClient.SetStudentOpenSubjectsAsync` throws `RpcException(StatusCode.InvalidArgument, "Duplicate subject")`.
- **When**: `SetStudentOpenSubjects` is called.
- **Then**: returns 400 with body `ErrorResponse("Duplicate subject")`.

## Boundary and exception tests

- EX-01 an empty Subjects list → the validation loop is skipped and gRPC is called directly.
- EX-02 a null Subjects list → the validation loop is skipped and gRPC is called directly.
- EX-03 a Subjects list with multiple valid subjects → all pass validation; the gRPC request contains all of them.
- EX-04 OpenEndDate is null → the gRPC request's OpenEndDate is an empty string.
- EX-05 OpenEndDate is a legal date → the gRPC request's OpenEndDate is formatted with SubjectConstants.DateFormat.
- EX-06 the first of several SubjectItems is invalid → 400 immediately, without validating the rest.
- EX-07 a gRPC exception in GetStudentOpenSubjects → not caught, propagates upward [inferred].

## Existing test mapping (to the SPEC requirements)

### Controller tests

| Test method | SPEC items verified |
| --- | --- |
| `GetStudentOpenSubjects_Success` | FR-01 |
| `GetStudentOpenSubjects_ActiveOnlyTrue` | FR-02 |
| `GetStudentOpenSubjects_EmptyEndDate_MapsToNull` | FR-10 |
| `SetStudentOpenSubjects_Success` | FR-03 |
| `SetStudentOpenSubjects_InvalidSubjectValue_Returns400` | FR-04 |
| `SetStudentOpenSubjects_EmptyOpenStartDate_Returns400` | FR-05 |
| `SetStudentOpenSubjects_InvalidOpenStartDateFormat_Returns400` | FR-06 |
| `SetStudentOpenSubjects_InvalidOpenEndDateFormat_Returns400` | FR-07 |
| `SetStudentOpenSubjects_GrpcSuccessFalse_Returns400` | FR-08 |
| `SetStudentOpenSubjects_GrpcInvalidArgument_Returns400` | FR-09 |

### Model tests

| Test method | SPEC items verified |
| --- | --- |
| `OpenSubjectDto_Constructor_SetsProperties` | FR-01/FR-03 (model layer) |
| `OpenSubjectDto_WithNullOpenEndDate_SetsProperty` | FR-10 (model layer) |
| `SubjectItem_Constructor_SetsProperties` | FR-03 (model layer) |
| `SetOpenSubjectsRequest_Constructor_SetsProperties` | FR-03 (model layer) |
| `SubjectOption_Constructor_SetsProperties` | FR-07 (model layer) |
