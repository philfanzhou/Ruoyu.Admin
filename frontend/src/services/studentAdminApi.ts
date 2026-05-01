import axios, { type AxiosInstance } from 'axios'

export interface StudentPagedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface StudentDto {
  id: string
  name: string
  grade: number
  identityAccountIds: string[]
  createdAt: number
  updatedAt: number
}

export interface GradeOption {
  value: number
  label: string
}

export interface CreateStudentRequest {
  name: string
  grade: number
  identityAccountIds: string[]
}

export interface UpdateStudentRequest {
  name: string
  grade: number
  identityAccountIds?: string[]
}

export interface OperationResponse {
  success: boolean
  message: string
}

// Identity 账户信息（用于批量查询）
export interface IdentityAccountDto {
  userId: string
  username: string
  displayName: string
  phone: string
  remark: string
}

// ========== Open Subjects ==========
export interface OpenSubjectDto {
  id: string
  subject: number
  openStartDate: string
  openEndDate: string | null
  isActive: boolean
}

export interface SubjectItem {
  subject: number
  openStartDate: string
  openEndDate: string | null
}

export interface SetOpenSubjectsRequest {
  subjects: SubjectItem[]
}

export interface SubjectOption {
  value: number
  name: string
  displayName: string
}

class StudentAdminApiClient {
  private client: AxiosInstance
  private identityCredentials: { appId: string; appSecret: string } | null = null

  constructor() {
    this.client = axios.create({
      timeout: 15000,
    })
  }

  // 设置 Identity 认证凭据 (调用 batch 接口时需要)
  setIdentityCredentials(credentials: { appId: string; appSecret: string }) {
    this.identityCredentials = credentials
  }

  clearIdentityCredentials() {
    this.identityCredentials = null
  }

  async getGrades() {
    const response = await this.client.get<GradeOption[]>('/api/admin/students/grades')
    return response.data
  }

  async getStudents(params: { name?: string; grade?: number; page?: number; pageSize?: number }) {
    const response = await this.client.get<StudentPagedResponse<StudentDto>>('/api/admin/students', { params })
    return response.data
  }

  async getStudent(studentId: string) {
    const response = await this.client.get<StudentDto>(`/api/admin/students/${studentId}`)
    return response.data
  }

  async createStudent(payload: CreateStudentRequest) {
    const response = await this.client.post<StudentDto>('/api/admin/students', payload)
    return response.data
  }

  async updateStudent(studentId: string, payload: UpdateStudentRequest) {
    const response = await this.client.put<OperationResponse>(`/api/admin/students/${studentId}`, payload)
    return response.data
  }

  async deleteStudent(studentId: string) {
    const response = await this.client.delete<OperationResponse>(`/api/admin/students/${studentId}`)
    return response.data
  }

  async getIdentityAccountsByStudentId(studentId: string) {
    const response = await this.client.get<string[]>(`/api/admin/students/${studentId}/accounts`)
    return response.data
  }

  async linkIdentityAccount(studentId: string, accountId: string) {
    const response = await this.client.post<OperationResponse>(`/api/admin/students/${studentId}/accounts`, { identityAccountId: accountId })
    return response.data
  }

  async unlinkIdentityAccount(studentId: string, accountId: string) {
    const response = await this.client.delete<OperationResponse>(`/api/admin/students/${studentId}/accounts/${accountId}`)
    return response.data
  }

  async getStudentsByAccountId(accountId: string) {
    const response = await this.client.get<StudentDto[]>(`/api/admin/accounts/${accountId}/students`)
    return response.data
  }

  // 批量获取 Identity 用户信息
  async getIdentityAccountsBatch(accountIds: string[]) {
    const headers: Record<string, string> = {}
    if (this.identityCredentials) {
      headers['X-Admin-AppId'] = this.identityCredentials.appId
      headers['X-Admin-AppSecret'] = this.identityCredentials.appSecret
    }
    const response = await this.client.post<IdentityAccountDto[]>('/api/admin/identity-accounts/batch', accountIds, { headers })
    return response.data
  }

  // ========== Open Subjects ==========

  async getSubjectOptions() {
    const response = await this.client.get<SubjectOption[]>('/api/admin/students/subject-options')
    return response.data
  }

  async getStudentOpenSubjects(studentId: string, activeOnly: boolean = false) {
    const response = await this.client.get<OpenSubjectDto[]>(`/api/admin/students/${studentId}/open-subjects`, {
      params: { activeOnly }
    })
    return response.data
  }

  async setStudentOpenSubjects(studentId: string, payload: SetOpenSubjectsRequest) {
    const response = await this.client.put<OperationResponse>(`/api/admin/students/${studentId}/open-subjects`, payload)
    return response.data
  }
}

export const studentAdminClient = new StudentAdminApiClient()

export function getStudentErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
