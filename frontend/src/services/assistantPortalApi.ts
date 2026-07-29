import axios from 'axios'
import httpClient from './httpClient'

export interface AssistantAccountDto {
  id: number
  userId: string | null
  phone: string | null
  username: string | null
  subjects: number[]
  createdAt: string
  updatedAt: string
}

export interface GrantAssistantRequest {
  subjects: number[]
}

export interface AssistantOperationResponse {
  success: boolean
  message?: string
  data?: AssistantAccountDto
}

export interface SetSubjectsRequest {
  subjects: number[]
}

export interface SubjectOption {
  value: number
  name: string
}

export interface AssistantListParams {
  keyword?: string
  subject?: number
  page?: number
  pageSize?: number
}

export interface AssistantPagedResponse {
  success: boolean
  data: AssistantAccountDto[]
  total: number
  page: number
  pageSize: number
}

import { checkSuccess } from './apiBase'

class AssistantPortalApiClient {
  private client = httpClient

  async getAssistants(params: AssistantListParams = {}) {
    const response = await this.client.get<AssistantPagedResponse>('/api/assistant-portal/admin/assistants', { params })
    return response.data
  }

  async grantAssistant(userId: string, payload: GrantAssistantRequest) {
    const response = await this.client.post<AssistantOperationResponse>(`/api/assistant-portal/admin/identity-users/${encodeURIComponent(userId)}/grant-assistant`, payload)
    return this.checkSuccess(response.data)
  }

  async revokeAssistant(userId: string) {
    const response = await this.client.post<AssistantOperationResponse>(`/api/assistant-portal/admin/identity-users/${encodeURIComponent(userId)}/revoke-assistant`)
    return this.checkSuccess(response.data)
  }

  async getAssistantSubjects(userId: string) {
    const response = await this.client.get<{ success: boolean; data: number[] }>(`/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/subjects`)
    return response.data
  }

  async setAssistantSubjects(userId: string, subjects: number[]) {
    const response = await this.client.put<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/subjects`,
      { subjects } as SetSubjectsRequest
    )
    return this.checkSuccess(response.data)
  }

  async addAssistantSubject(userId: string, subject: number) {
    const response = await this.client.post<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/subjects/${subject}`
    )
    return this.checkSuccess(response.data)
  }

  async removeAssistantSubject(userId: string, subject: number) {
    const response = await this.client.delete<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/subjects/${subject}`
    )
    return this.checkSuccess(response.data)
  }

  async getAvailableSubjects() {
    const response = await this.client.get<{ success: boolean; data: SubjectOption[] }>('/api/assistant-portal/admin/available-subjects')
    return response.data
  }
}

export const assistantPortalClient = new AssistantPortalApiClient()

export default assistantPortalClient

export function getAssistantPortalErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
