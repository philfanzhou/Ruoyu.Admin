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

export interface SetAssistantStudentsRequest {
  studentIds: string[]
}

export interface SubjectOption {
  value: number
  name: string
}

class AssistantPortalApiClient {
  private client = httpClient

  private checkSuccess<T extends { success: boolean; message?: string }>(result: T): T {
    if (!result.success) {
      throw new Error(result.message || 'Operation failed')
    }
    return result
  }

  async getAssistants() {
    const response = await this.client.get<{ success: boolean; data: AssistantAccountDto[] }>('/api/assistant-portal/admin/assistants')
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

  // Student associations
  async getAssistantStudents(userId: string) {
    const response = await this.client.get<{ success: boolean; data: string[] }>(`/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/students`)
    return response.data
  }

  async setAssistantStudents(userId: string, studentIds: string[]) {
    const response = await this.client.post<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/students`,
      { studentIds } as SetAssistantStudentsRequest
    )
    return this.checkSuccess(response.data)
  }

  async addAssistantStudent(userId: string, studentId: string) {
    const response = await this.client.post<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
    return this.checkSuccess(response.data)
  }

  async removeAssistantStudent(userId: string, studentId: string) {
    const response = await this.client.delete<AssistantOperationResponse>(
      `/api/assistant-portal/admin/assistants/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
    return this.checkSuccess(response.data)
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
