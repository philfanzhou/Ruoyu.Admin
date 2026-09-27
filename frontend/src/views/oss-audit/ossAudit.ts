import type { StatusTagType } from '../../components/list/StatusTag.vue'

export interface AuditStatusMeta {
  value: number
  label: string
  tagType: StatusTagType
}

/**
 * Chinese labels for OssAuditRecord.Status. The backend `statusText`
 * (Pending/Resolved/Ignored) is intentionally not shown.
 */
export const AUDIT_STATUSES: readonly AuditStatusMeta[] = [
  { value: 0, label: '待处理', tagType: 'warning' },
  { value: 1, label: '已删除', tagType: 'danger' },
  { value: 2, label: '已忽略', tagType: 'info' },
]

export const AUDIT_STATUS_VALUES = AUDIT_STATUSES.map((s) => s.value)

export function getAuditStatusMeta(status: number): AuditStatusMeta {
  return AUDIT_STATUSES.find((s) => s.value === status) ?? { value: status, label: '未知', tagType: 'info' }
}

export function isImagePath(path: string): boolean {
  if (!path) return false
  const ext = path.split('.').pop()?.toLowerCase() || ''
  return ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp'].includes(ext)
}

export function getImageUrl(path: string): string {
  return `/api/admin/image?path=${encodeURIComponent(path)}`
}

export function getBucketStyle(bucket: string): Record<string, string> {
  const b = (bucket || '').toLowerCase()
  if (b.includes('mistake')) {
    return { background: '#faf5ff', color: '#7c3aed', borderColor: '#ddd6fe' }
  }
  if (b.includes('homework') || b.includes('hw')) {
    return { background: '#f0fdf4', color: '#16a34a', borderColor: '#bbf7d0' }
  }
  return {}
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null) return '-'
  if (seconds < 60) return `${seconds} 秒`
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`
  return `${Math.floor(seconds / 3600)} 时 ${Math.floor((seconds % 3600) / 60)} 分`
}
