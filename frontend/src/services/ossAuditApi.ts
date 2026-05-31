import axios, { type AxiosInstance } from 'axios'

export interface OssAuditRecordDto {
  id: number
  objectPath: string
  bucket: string
  size: number
  lastModified: number
  status: number
  statusText: string
  createdAt: number
  resolvedAt: number | null
  note: string | null
}

export interface OssAuditRecordsResponse {
  items: OssAuditRecordDto[]
  totalCount: number
  page: number
  pageSize: number
  statusCounts: Record<number, number>
  bucketCounts: Record<string, number>
}

export interface OperationResponse {
  success: boolean
  message?: string
}

export interface BatchResolveResponse {
  resolvedCount: number
  errors: string[]
  totalRequested: number
}

class OssAuditApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      timeout: 30000,
    })
  }

  async getRecords(page: number = 1, pageSize: number = 20, status?: number, bucket?: string) {
    const params: any = { page, pageSize }
    if (status !== undefined && status !== null) params.status = status
    if (bucket) params.bucket = bucket
    const response = await this.client.get<OssAuditRecordsResponse>('/api/admin/oss-audit/records', { params })
    return response.data
  }

  async triggerAudit() {
    const response = await this.client.post<OperationResponse>('/api/admin/oss-audit/trigger')
    return response.data
  }

  async resolveRecord(id: number) {
    const response = await this.client.post<OperationResponse>(`/api/admin/oss-audit/records/${id}/resolve`)
    return response.data
  }

  async ignoreRecord(id: number, note?: string) {
    const response = await this.client.post<OperationResponse>(`/api/admin/oss-audit/records/${id}/ignore`, { note })
    return response.data
  }

  async batchResolve(ids: number[]) {
    const response = await this.client.post<BatchResolveResponse>('/api/admin/oss-audit/records/batch-resolve', { ids })
    return response.data
  }
}

export const ossAuditApi = new OssAuditApiClient()
export const ossAuditClient = ossAuditApi

export default ossAuditApi

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
