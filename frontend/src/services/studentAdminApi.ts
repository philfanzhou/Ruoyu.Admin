import axios, { type AxiosInstance } from 'axios'
import { getAuthToken } from './auth'

// Attach the admin JWT (stored in localStorage) to every admin API request.
axios.interceptors.request.use((config) => {
  const token = getAuthToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

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
  warning?: string
}

// Identity 账户批量查询响应
export interface IdentityAccountBatchResponse {
  accounts: IdentityAccountDto[]
  partialFailure: boolean
  warning: string | null
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

export interface EnumOption {
  value: number
  name: string
  displayName: string
}

export interface EnumOptionsResponse {
  uploadStatuses: EnumOption[]
  grades: EnumOption[]
  subjects: EnumOption[]
  classifications: EnumOption[]
  reviewStatuses: EnumOption[]
}

// Upload Record Types
export interface UploadRecordDto {
    id: string
    studentId: string
    studentName: string
    status: number // 1: Pending, 2: Processing, 3: UnderReview, 4: Failed, 5: Returned
    imagePaths: string[]
    comments: string
    createdAt: number | string
    updatedAt: number | string
    imageRotations: number[]
}

export interface ImageAssignmentPayload {
    imageIndices: number[]
    subject: number
    grade: number
    comments?: string
}

export interface AssignUploadRecordRequest {
    studentId: string
    assignments: ImageAssignmentPayload[]
}

export interface RotateImageRequest {
    studentId: string
    imageIndex: number
    rotation: number
}

export interface UploadRecordListResponse {
  items: UploadRecordDto[]
  totalCount: number
  page: number
  pageSize: number
}

// VL Analysis Types
export interface VlAnalysisGroup {
  imageIndices: number[]
  subject: number
  grade: number
  description: string
}

export interface VlAnalysisResponse {
  success: boolean
  errorMessage: string
  rawResponse: string
  skipped: boolean
  groups: VlAnalysisGroup[]
  prompt: string
  compressedImages: string[]
}

// ========== Mistake Query Types ==========
export interface SourceRegionDto {
  sourceImagePath: string
  boundingBox: {
    x1: number
    y1: number
    x2: number
    y2: number
  } | null
}

export interface MistakeItemDto {
  id: string
  studentId: string
  studentName: string
  subject: number
  grade: number
  sourceUploadId: string
  reviewStatus: number // 1: PendingReview, 2: Confirmed, 3: Rejected
  reviewerId: string
  reviewedAt: string
  reviewComment: string
  type: number
  questionId: string
  createdAt: string
  updatedAt: string
  imageCount: number
  firstImagePath: string
  sourceRegions?: SourceRegionDto[]
}

export interface MistakeListResponse {
  items: MistakeItemDto[]
  total: number
  page: number
  pageSize: number
  totalPages: number
}

class StudentAdminApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      timeout: 15000,
    })
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
    const response = await this.client.post<IdentityAccountBatchResponse>('/api/admin/identity-accounts/batch', accountIds)
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

  // ========== Upload Records ==========

  async getUploadRecords(params: {
    page?: number
    pageSize?: number
    status?: number // -1: all, 1: pending, 2: processing, 3: underReview, 4: failed, 5: returned
    studentId?: string
  }) {
    const response = await this.client.get<UploadRecordListResponse>('/api/admin/oss-upload-records', { params })
    return response.data
  }

  async resetUploadRecordStatus(recordId: string, studentId: string, targetStatus: number) {
    const response = await this.client.post<OperationResponse>(
      `/api/admin/oss-upload-records/${recordId}/reset-status`,
      { studentId, targetStatus }
    )
    return response.data
  }

  async assignUploadRecord(recordId: string, payload: AssignUploadRecordRequest) {
        const response = await this.client.post<OperationResponse>(
            `/api/admin/oss-upload-records/${recordId}/assign`,
            payload
        )
        return response.data
    }

    async rotateUploadImage(recordId: string, payload: RotateImageRequest) {
        const response = await this.client.post<OperationResponse>(
            `/api/admin/oss-upload-records/${recordId}/rotate`,
            payload
        )
        return response.data
    }

  async getMistakeItems(params: {
    studentId?: string
    subject?: number
    grade?: number
    reviewStatus?: number
    page?: number
    size?: number
  }) {
    const response = await this.client.get<MistakeListResponse>('/api/admin/mistakes', { params })
    return response.data
  }

  async getMistakeItem(id: string) {
    const response = await this.client.get<MistakeItemDto>(`/api/admin/mistakes/${id}`)
    return response.data
  }

  async updateMistakeItem(id: string, data: {
    studentId?: string
    subject?: number
    grade?: number
  }) {
    const response = await this.client.put<MistakeItemDto>(`/api/admin/mistakes/${id}`, data)
    return response.data
  }

  async migrateMistakeImages(id: string) {
    const response = await this.client.post<{ success: boolean; message: string; migratedCount: number }>(`/api/admin/mistakes/migrate-images/${id}`)
    return response.data
  }

  async getMistakesByUploadId(uploadId: string) {
    const response = await this.client.get<{ items: MistakeItemDto[], total: number }>(`/api/admin/mistakes/by-upload/${uploadId}`)
    return response.data
  }

  async getEnumOptions() {
    const response = await this.client.get<EnumOptionsResponse>('/api/admin/enum-options')
    return response.data
  }

  async legacyCheck(id: string) {
    const response = await this.client.get<{ isLegacy: boolean; mistakeCount?: number; hasUploadPathImage?: boolean; message: string }>(`/api/admin/oss-upload-records/legacy-check/${id}`)
    return response.data
  }

  async legacyClean(id: string) {
    const response = await this.client.post<{ success: boolean; message: string; uploadRecordId: string }>(`/api/admin/oss-upload-records/legacy-clean/${id}`)
    return response.data
  }

  async removeImageFromRecord(recordId: string, studentId: string, imageIndex: number) {
    const response = await this.client.delete<{
      success: boolean
      message: string
      recordDeleted: boolean
      remainingImageCount: number
    }>(`/api/admin/oss-upload-records/${recordId}/images/${imageIndex}`, {
      params: { studentId }
    })
    return response.data
  }

  async analyzeUploadRecord(id: string) {
    const response = await this.client.post<VlAnalysisResponse>(`/api/admin/oss-upload-records/${id}/analyze`, null, {
      timeout: 120000, // VL analysis can take a long time
    })
    return response.data
  }
}

export const studentAdminClient = new StudentAdminApiClient()

export default studentAdminClient

export function getStudentErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
