import axios from 'axios'
import { extractApiErrorMessage } from './apiBase'
import httpClient from './httpClient'

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

export interface AuditRunInfo {
  id: number
  startedAt: number
  completedAt: number | null
  durationSeconds: number | null
  newZombieCount: number
  triggerType: string
}

export interface AuditFailedInfo {
  id: number
  startedAt: number
  completedAt: number | null
  errorMessage: string
  triggerType: string
}

export interface AuditStatusResponse {
  isRunning: boolean
  lastCompleted: AuditRunInfo | null
  lastFailed: AuditFailedInfo | null
  pendingCount: number
}

class OssAuditApiClient {
  private client = httpClient

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

  async getStatus() {
    const response = await this.client.get<AuditStatusResponse>('/api/admin/oss-audit/status')
    return response.data
  }
}

export const ossAuditApi = new OssAuditApiClient()
export const ossAuditClient = ossAuditApi

export default ossAuditApi

export function getOssAuditErrorMessage(error: unknown) {
  // Delegates to the shared extractor so problem+json titles (#56) surface like business
  // messages everywhere; non-axios inputs keep the previous behavior.
  return extractApiErrorMessage(error)
}

export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}
