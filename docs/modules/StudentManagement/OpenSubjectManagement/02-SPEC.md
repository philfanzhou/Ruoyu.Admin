# OpenSubjectManagement — Detailed Requirements Specification (SPEC)

## Feature overview and user stories

**Overview**: open subject management lets administrators configure which subjects a student may study and the time range during which each is open, persisting the data through gRPC calls to the Student service. Administrators can view a student's open-subject list (filterable by active status) and fully set a student's open-subject configuration.

**User story 1 (administrator views open subjects)**: as an administrator, I want to view a student's open-subject list to learn which subjects the student can currently study, plus each subject's open start/end dates and active status.

**User story 2 (administrator filters active subjects)**: as an administrator, I want to view only the currently active subjects to quickly see the subjects available to the student right now.

**User story 3 (administrator sets open subjects)**: as an administrator, I want to set a student's open-subject configuration, specifying each subject's open start date and optional end date, to control which subjects the student may study.

## Requirements list (independently testable)

- [x] FR-01 Can query a student's open-subject list.
- [x] FR-02 The query supports an activeOnly parameter filter that returns only the currently active subjects.
- [x] FR-03 Can fully set a student's open subjects; each entry contains Subject, OpenStartDate, and optional OpenEndDate.
- [x] FR-04 Subject values must pass the SubjectConstants.IsValid check; an invalid subject returns 400.
- [x] FR-05 OpenStartDate is required and must not be empty, otherwise 400.
- [x] FR-06 OpenStartDate must be a legal DateOnly format, otherwise 400.
- [x] FR-07 OpenEndDate is optional; when provided it must be a legal DateOnly format, otherwise 400.
- [x] FR-08 When setting and gRPC returns Success=false, the API returns 400.
- [x] FR-09 When setting and gRPC returns InvalidArgument, the API returns 400.
- [x] FR-10 Every OpenSubjectDto returned by the query contains Id, Subject, OpenStartDate, OpenEndDate (nullable), and IsActive.

## Detailed acceptance criteria (automatically verifiable)

- AC-FR-01: calling `GET /api/admin/students/{studentId}/open-subjects` returns `List<OpenSubjectDto>`.
- AC-FR-02: calling `GET /api/admin/students/{studentId}/open-subjects?activeOnly=true` sends `ActiveOnly=true` in the gRPC request.
- AC-FR-03: calling `PUT /api/admin/students/{studentId}/open-subjects` with `SetOpenSubjectsRequest` returns `OperationResponse(true, "Open subjects updated successfully.")` on success.
- AC-FR-04: when setting, an invalid subject value (for example Subject=-1) returns 400 `ErrorResponse("Invalid subject value: -1")`.
- AC-FR-05: when setting, an empty-string OpenStartDate returns 400 `ErrorResponse("Open start date is required")`.
- AC-FR-06: when setting, an illegal date format for OpenStartDate (for example "not-a-date") returns 400 `ErrorResponse("Invalid start date format: not-a-date")`.
- AC-FR-07: when setting, an illegal date format for OpenEndDate returns 400 `ErrorResponse("Invalid end date format: {value}")`.
- AC-FR-08: when setting and gRPC returns `Success=false`, the API returns 400 `ErrorResponse(ErrorMessage)`.
- AC-FR-09: when setting and gRPC throws `RpcException(InvalidArgument)`, the API returns 400.
- AC-FR-10: in the returned OpenSubjectDto, a null OpenEndDate is absent from or null in the JSON; IsActive is a bool.

## Non-functional requirements

- **Performance**: query and set operations P95 < 500ms (including the gRPC call).
- **Security**: only administrators may operate (authorization is handled by the upper Identity proxy middleware).
- **Reliability**: depends on the availability of the Student gRPC service; when gRPC is unavailable, returns 502.
- **Observability**: key errors should be logged through `ILogger`.

## Test strategy

- **Coverage target**: ≥ 80% line coverage of the open-subject-related methods in StudentsController.
- **Error paths that must be tested**: invalid subject value, empty OpenStartDate, illegal date formats (start and end), gRPC InvalidArgument, gRPC Success=false.
- **Test environment requirements**: unit tests with `xUnit + Moq`; the gRPC client is replaced with a Mock.
- **Prohibitions**: unit tests must not connect to a real gRPC service.
