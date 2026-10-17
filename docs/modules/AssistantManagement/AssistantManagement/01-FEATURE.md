# Assistant Management

## Overview

The Admin Portal adds an assistant management capability, letting administrators manage assistant accounts from a unified entry point, including granting/revoking assistant permissions and managing assistant subjects.

## Background

- Teacher management is already wired into the Admin Portal through TeacherPortalProxyMiddleware
- The Assistant Portal (AssistantWebApi) already has complete Admin APIs (grant/revoke/subjects), but the Admin Portal was not wired to them
- Assistant-student association management is done uniformly in the "Linked accounts" dialog on the student management page, not on the assistant management page

## Goals

1. The Admin Portal can manage assistant permissions (grant, revoke, subject management)
2. Keep the interaction patterns consistent with teacher management

## Scope

### Included

- Admin backend: AssistantPortalProxyMiddleware, proxying /api/assistant-portal/* → the Assistant Portal
- Admin frontend: AssistantView.vue page + assistantPortalApi.ts client

### Not included

- Assistant-student association management (done uniformly in the "Linked accounts" dialog on the student management page; see [StudentManagement/AccountLinking](../../StudentManagement/AccountLinking/01-FEATURE.md))
- Assistant Portal frontend changes (a separate iteration)

## Removed features

- **Assistant-student association**: the original design provided a "Linked students" column and a Manage button on the assistant management page. The frontend and backend implementations were deleted on 2026-07-27; association management is done uniformly on the student management page.
- **Linked-account management (PUT /api/admin/assistants/{userId}/user-id)**: the original design offered "link/change Identity account". Deleted on 2026-07-27.
