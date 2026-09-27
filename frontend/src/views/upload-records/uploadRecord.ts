import type { GradeOption } from '../../services/studentAdminApi'
import type { StatusTagType } from '../../components/list/StatusTag.vue'

export interface UploadStatusMeta {
  value: number
  label: string
  tagType: StatusTagType
  /** Stat-card colour key (scoped CSS in UploadRecordView). */
  key: 'pending' | 'processing' | 'review' | 'failed' | 'returned'
}

/**
 * The single upload-status mapping for this page: labels mirror
 * UploadStatusConstants.DisplayNames in Ruoyu.Admin.Common.
 */
export const UPLOAD_STATUSES: readonly UploadStatusMeta[] = [
  { value: 1, label: '待处理', tagType: 'warning', key: 'pending' },
  { value: 2, label: '处理中', tagType: 'primary', key: 'processing' },
  { value: 3, label: '审核中', tagType: 'success', key: 'review' },
  { value: 4, label: '处理失败', tagType: 'danger', key: 'failed' },
  { value: 5, label: '已退回', tagType: 'info', key: 'returned' },
]

export const UPLOAD_STATUS_VALUES = UPLOAD_STATUSES.map((s) => s.value)

export function getUploadStatusLabel(status: number): string {
  return UPLOAD_STATUSES.find((s) => s.value === status)?.label ?? `状态${status}`
}

export function getUploadStatusTagType(status: number): StatusTagType {
  return UPLOAD_STATUSES.find((s) => s.value === status)?.tagType ?? 'warning'
}

export function getGradeLabel(options: GradeOption[], grade: number): string {
  return options.find((g) => g.value === grade)?.label || `年级${grade || '-'}`
}

export function getImageUrl(path: string, size?: 'small' | 'medium'): string {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  const params = new URLSearchParams({ path })
  if (size) params.set('size', size)
  return `/api/admin/image?${params.toString()}`
}

export function hideBrokenImage(e: Event): void {
  ;(e.target as HTMLImageElement).style.display = 'none'
}

/** Server message first; `withErrorMessage` also falls back to the thrown error's own message. */
export function getErrorMessage(error: unknown, fallback: string, withErrorMessage = false): string {
  const e = error as { response?: { data?: { message?: string } }; message?: string }
  return e?.response?.data?.message || (withErrorMessage ? e?.message : undefined) || fallback
}
