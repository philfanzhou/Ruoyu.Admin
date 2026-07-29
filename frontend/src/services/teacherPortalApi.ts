import axios from 'axios'
import httpClient from './httpClient'

export interface TeacherAccountDto {
  id: number
  userId: string | null
  phone: string | null
  username: string | null
  subjects: number[]
  createdAt: string
  updatedAt: string
}

export interface AddTeacherByUserIdRequest {
  userId: string
  phone?: string
  username?: string
  subjects: number[]
}

export interface GrantTeacherRequest {
  subjects: number[]
}

export interface TeacherOperationResponse {
  success: boolean
  message?: string
  data?: TeacherAccountDto
}

export interface SetSubjectsRequest {
  subjects: number[]
}

export interface SubjectOption {
  value: number
  name: string
}

export interface TeacherListParams {
  keyword?: string
  subject?: number
  page?: number
  pageSize?: number
}

export interface TeacherPagedResponse {
  success: boolean
  data: TeacherAccountDto[]
  total: number
  page: number
  pageSize: number
}

import { checkSuccess } from './apiBase'

class TeacherPortalApiClient {
  private client = httpClient

  async getTeachers(params: TeacherListParams = {}) {
    const response = await this.client.get<TeacherPagedResponse>('/api/teacher-portal/admin/teachers', { params })
    return response.data
  }

  async addTeacherByUserId(payload: AddTeacherByUserIdRequest) {
    const response = await this.client.post<TeacherOperationResponse>('/api/teacher-portal/admin/teachers/by-user', payload)
    return this.checkSuccess(response.data)
  }

  async grantTeacher(userId: string, payload: GrantTeacherRequest) {
    const response = await this.client.post<TeacherOperationResponse>(`/api/teacher-portal/admin/identity-users/${encodeURIComponent(userId)}/grant-teacher`, payload)
    return this.checkSuccess(response.data)
  }

  async removeTeacherByUserId(userId: string) {
    const response = await this.client.delete<TeacherOperationResponse>(`/api/teacher-portal/admin/teachers/by-user/${encodeURIComponent(userId)}`)
    return this.checkSuccess(response.data)
  }

  async checkTeacher(userId: string, phone?: string) {
    const params = new URLSearchParams()
    params.set('userId', userId)
    if (phone) params.set('phone', phone)
    const response = await this.client.get<{ isTeacher: boolean }>(`/api/teacher-portal/auth/check-teacher?${params.toString()}`)
    return response.data
  }

  async getTeacherSubjects(userId: string) {
    const response = await this.client.get<{ success: boolean; data: number[] }>(`/api/teacher-portal/admin/teachers/${encodeURIComponent(userId)}/subjects`)
    return response.data
  }

  async setTeacherSubjects(userId: string, subjects: number[]) {
    const response = await this.client.put<TeacherOperationResponse>(
      `/api/teacher-portal/admin/teachers/${encodeURIComponent(userId)}/subjects`,
      { subjects } as SetSubjectsRequest
    )
    return this.checkSuccess(response.data)
  }

  async addTeacherSubject(userId: string, subject: number) {
    const response = await this.client.post<TeacherOperationResponse>(
      `/api/teacher-portal/admin/teachers/${encodeURIComponent(userId)}/subjects/${subject}`
    )
    return this.checkSuccess(response.data)
  }

  async removeTeacherSubject(userId: string, subject: number) {
    const response = await this.client.delete<TeacherOperationResponse>(
      `/api/teacher-portal/admin/teachers/${encodeURIComponent(userId)}/subjects/${subject}`
    )
    return this.checkSuccess(response.data)
  }

  async getAvailableSubjects() {
    const response = await this.client.get<{ success: boolean; data: SubjectOption[] }>('/api/teacher-portal/admin/available-subjects')
    return response.data
  }
}

export const teacherPortalClient = new TeacherPortalApiClient()

export default teacherPortalClient

export function getTeacherPortalErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
