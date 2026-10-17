# Assistant Management — Task List

## Phase 1: Documentation (complete)

- [x] Create the functional spec
- [x] Create the technical design
- [x] Create the task list and test specification

## Phase 2: Backend code

- [x] AssistantPortalProxyMiddleware.cs + AssistantPortalOptions
- [x] Program.cs wiring (Options, HttpClient, Middleware)
- [x] appsettings.json adds the AssistantPortal configuration section

## Phase 3: Frontend code

- [x] assistantPortalApi.ts
- [x] AssistantView.vue (the assistant management main page)
- [x] router/index.ts adds the /assistants route
- [x] The side navigation adds the assistant management entry

## Phase 4: Testing and verification

- [x] AssistantPortalProxyMiddleware unit tests
- [x] AdminController endpoint tests (grant/revoke/subjects)
- [x] Compilation verified
- [x] Committed and pushed

## Deleted tasks (2026-07-27)

The following tasks were deleted together with the assistant-student association feature:

- ~~Assistant Portal: AssistantStudent entity + DbContext update~~
- ~~Assistant Portal: AssistantDbService student-association methods~~
- ~~Assistant Portal: AdminController student-association endpoints~~
- ~~Assistant Portal: RevokeAssistantRole also cleaning up student associations~~
- ~~AssistantDbService student-association method tests~~
- ~~AdminController student-association endpoint tests~~

Association management is done uniformly in the "Linked accounts" dialog on the student management page.
