/**
 * Subject utilities - shared across admin portal views.
 *
 * Subject value mapping (matches SubjectConstants.cs in ruoyu.common):
 *   1=语文(chinese), 2=数学(math), 3=英语(english), 4=物理(physics),
 *   5=化学(chemistry), 6=生物(biology), 7=历史(history), 8=地理(geography), 9=政治(politics)
 */

export interface SubjectMeta {
  cssClass: string
  label: string
}

const SUBJECT_MAP: Record<number, SubjectMeta> = {
  1: { cssClass: 'chinese', label: '语文' },
  2: { cssClass: 'math', label: '数学' },
  3: { cssClass: 'english', label: '英语' },
  4: { cssClass: 'physics', label: '物理' },
  5: { cssClass: 'chemistry', label: '化学' },
  6: { cssClass: 'biology', label: '生物' },
  7: { cssClass: 'history', label: '历史' },
  8: { cssClass: 'geography', label: '地理' },
  9: { cssClass: 'politics', label: '政治' },
}

const UNKNOWN_SUBJECT: SubjectMeta = { cssClass: '', label: '未知' }

export function getSubjectMeta(subject: number): SubjectMeta {
  return SUBJECT_MAP[subject] ?? UNKNOWN_SUBJECT
}

export function getSubjectLabel(subject: number): string {
  return getSubjectMeta(subject).label
}

export function getSubjectCssClass(subject: number): string {
  return getSubjectMeta(subject).cssClass
}

/**
 * Generate a stable background gradient for an avatar based on a string seed.
 * Used for student/teacher avatars when no image is available.
 */
const AVATAR_GRADIENTS = [
  'linear-gradient(135deg, #a78bfa, #8b5cf6)',
  'linear-gradient(135deg, #60a5fa, #3b82f6)',
  'linear-gradient(135deg, #34d399, #10b981)',
  'linear-gradient(135deg, #fb923c, #f97316)',
  'linear-gradient(135deg, #f87171, #ef4444)',
  'linear-gradient(135deg, #22d3ee, #06b6d4)',
  'linear-gradient(135deg, #fbbf24, #f59e0b)',
  'linear-gradient(135deg, #f472b6, #ec4899)',
  'linear-gradient(135deg, #4ade80, #22c55e)',
  'linear-gradient(135deg, #94a3b8, #64748b)',
]

export function getAvatarGradient(seed: string): string {
  let hash = 0
  for (let i = 0; i < seed.length; i++) {
    hash = (hash << 5) - hash + seed.charCodeAt(i)
    hash |= 0
  }
  return AVATAR_GRADIENTS[Math.abs(hash) % AVATAR_GRADIENTS.length]
}

/**
 * Get the first character of a name for avatar display.
 */
export function getAvatarChar(name: string): string {
  return (name || '?').charAt(0)
}

/**
 * Mask a phone number, keeping the first 3 and last 4 digits.
 * Example: 13812341234 -> 138****1234
 */
export function maskPhone(phone: string): string {
  if (!phone) return ''
  const digits = phone.replace(/\D/g, '')
  if (digits.length < 7) return phone
  return digits.slice(0, 3) + '****' + digits.slice(-4)
}

/**
 * Format a Unix timestamp (seconds or ms) or ISO string as YYYY-MM-DD.
 */
export function formatDate(input: number | string): string {
  if (!input) return '-'
  let d: Date
  if (typeof input === 'number') {
    // Detect seconds vs ms
    d = new Date(input < 1e12 ? input * 1000 : input)
  } else {
    d = new Date(input)
  }
  if (isNaN(d.getTime())) return String(input)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * Format a Unix timestamp (seconds or ms) or ISO string as YYYY-MM-DD HH:mm.
 */
export function formatDateTime(input: number | string): string {
  if (!input) return '-'
  let d: Date
  if (typeof input === 'number') {
    d = new Date(input < 1e12 ? input * 1000 : input)
  } else {
    d = new Date(input)
  }
  if (isNaN(d.getTime())) return String(input)
  const date = formatDate(input)
  const h = String(d.getHours()).padStart(2, '0')
  const min = String(d.getMinutes()).padStart(2, '0')
  return `${date} ${h}:${min}`
}
