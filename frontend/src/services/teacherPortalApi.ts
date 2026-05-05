import axios, { type AxiosInstance } from 'axios'

export interface TeacherAccountDto {
  id: number
  userId: string | null
  phone: string | null
  username: string | null
  createdAt: string
  updatedAt: string
}

export interface AddTeacherByUserIdRequest {
  userId: string
  phone?: string
  username?: string
}

export interface TeacherOperationResponse {
  success: boolean
  message?: string
  data?: TeacherAccountDto
}

class TeacherPortalApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      timeout: 10000,
    })
  }

  async getTeachers() {
    const response = await this.client.get<{ success: boolean; data: TeacherAccountDto[] }>('/api/teacher-portal/admin/teachers')
    return response.data
  }

  async addTeacherByUserId(payload: AddTeacherByUserIdRequest) {
    const response = await this.client.post<TeacherOperationResponse>('/api/teacher-portal/admin/teachers/by-user', payload)
    return response.data
  }

  async removeTeacherByUserId(userId: string) {
    const response = await this.client.delete<TeacherOperationResponse>(`/api/teacher-portal/admin/teachers/by-user/${encodeURIComponent(userId)}`)
    return response.data
  }

  async checkTeacher(userId: string, phone?: string) {
    const params = new URLSearchParams()
    params.set('userId', userId)
    if (phone) params.set('phone', phone)
    const response = await this.client.get<{ isTeacher: boolean }>(`/api/teacher-portal/auth/check-teacher?${params.toString()}`)
    return response.data
  }
}

export const teacherPortalClient = new TeacherPortalApiClient()

export function getTeacherPortalErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
