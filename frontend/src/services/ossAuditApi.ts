import axios, { type AxiosInstance } from 'axios'

export interface OssObjectInfoDto {
  objectPath: string
  size: number
  lastModified: number | null
  isZombie: boolean
}

export interface OssBucketAuditResultDto {
  bucket: number
  totalObjects: number
  totalSize: number
  zombieObjects: OssObjectInfoDto[]
  zombieSize: number
}

export interface OssAuditResultDto {
  auditTime: number
  bucketResults: OssBucketAuditResultDto[]
  totalZombieObjects: number
  totalZombieSize: number
}

export interface DeleteZombieObjectsRequest {
  objectPaths: string[]
}

export interface DeleteZombieObjectsResponse {
  deletedCount: number
}

export interface OperationResponse {
  success: boolean
  message?: string
}

export interface UploadRecordDto {
  id: string
  studentId: string
  status: number
  imagePaths: string[]
  comments: string
  createdAt: number
  updatedAt: number
  classification: number
}

export interface UploadRecordsPageResult {
  items: UploadRecordDto[]
  totalCount: number
  page: number
  pageSize: number
}

export interface AssignZombieImageRequest {
  objectPath: string
  studentId: string
  uploadRecordId: string
  imageHash: string
}

class OssAuditApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      timeout: 30000,
    })
  }

  async auditAllBuckets() {
    const response = await this.client.get<OssAuditResultDto>('/api/admin/oss-audit/audit-all')
    return response.data
  }

  async auditBucket(bucket: number) {
    const response = await this.client.get<OssBucketAuditResultDto>(`/api/admin/oss-audit/audit-bucket/${bucket}`)
    return response.data
  }

  async deleteZombieObject(objectPath: string) {
    const response = await this.client.delete<OperationResponse>('/api/admin/oss-audit/zombie-object', {
      params: { objectPath }
    })
    return response.data
  }

  async deleteZombieObjects(objectPaths: string[]) {
    const response = await this.client.delete<DeleteZombieObjectsResponse>('/api/admin/oss-audit/zombie-objects', {
      data: { objectPaths } as DeleteZombieObjectsRequest
    })
    return response.data
  }

  async getAllUploadRecords(page: number = 1, pageSize: number = 20, status: number = -1, studentId?: string) {
    const params: any = { page, pageSize, status }
    if (studentId) {
      params.studentId = studentId
    }
    const response = await this.client.get<UploadRecordsPageResult>('/api/admin/oss-upload-records', { params })
    return response.data
  }

  async assignZombieImageToRecord(request: AssignZombieImageRequest) {
    const response = await this.client.post<OperationResponse>('/api/admin/oss-upload-records/assign-zombie', request)
    return response.data
  }
}

export const ossAuditApi = new OssAuditApiClient()
export const ossAuditClient = ossAuditApi

export function getOssAuditErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}

export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

export function getBucketName(bucket: number): string {
  switch (bucket) {
    case 1: return 'Mistakes'
    case 2: return 'Uploads'
    case 3: return 'Questions'
    default: return 'Unknown'
  }
}

export function getUploadStatusName(status: number): string {
  switch (status) {
    case 1: return 'Pending'
    case 2: return 'Processing'
    case 3: return 'Completed'
    case 4: return 'Failed'
    case 5: return 'Returned'
    default: return 'Unknown'
  }
}

export function generateImageHash(): string {
  const chars = 'abcdef0123456789'
  let hash = ''
  for (let i = 0; i < 64; i++) {
    hash += chars.charAt(Math.floor(Math.random() * chars.length))
  }
  return hash
}
