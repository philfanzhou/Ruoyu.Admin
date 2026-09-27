import type { StatusTagType } from '../../components/list/StatusTag.vue'

export interface Option {
  value: number
  label: string
}

/** Local defaults; replaced by enum-options `reviewStatuses` when the backend provides them. */
export const DEFAULT_REVIEW_STATUS_OPTIONS: Option[] = [
  { value: 1, label: '待审核' },
  { value: 2, label: '已确认' },
  { value: 3, label: '已退回' },
]

export function getReviewStatusTagType(status: number): StatusTagType {
  const map: Record<number, StatusTagType> = { 1: 'warning', 2: 'success', 3: 'danger' }
  return map[status] ?? 'warning'
}

export function getMistakeImageUrl(path: string, size?: 'small' | 'medium'): string {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  const params = new URLSearchParams({ path })
  if (size) params.set('size', size)
  return `/api/admin/image?${params.toString()}`
}

export function hideBrokenImage(e: Event): void {
  ;(e.target as HTMLImageElement).style.display = 'none'
}

export function getErrorMessage(error: unknown, fallback: string): string {
  return (error as { response?: { data?: { message?: string } } })?.response?.data?.message || fallback
}
