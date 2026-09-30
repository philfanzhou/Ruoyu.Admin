import axios from 'axios'

export function checkSuccess<T extends { success: boolean; message?: string }>(result: T): T {
  if (!result.success) {
    throw new Error(result.message || 'Operation failed')
  }
  return result
}

export function extractMsg(error: unknown): string {
  if (error instanceof Error) return error.message
  return '操作失败'
}

// Single normalization point for API error responses (#56): the backend now answers
// unhandled exceptions with application/problem+json (fixed fields type/title/status/
// correlationId/errorCode, never an exception message), while endpoint-returned business
// errors keep their existing {success,message} / {message} / {error} JSON bodies.
// Priority: business `message` -> problem+json `title` (only when the content type is
// problem+json) -> the axios transport message. `detail` is never read and raw JSON is
// never rendered.
export function extractApiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const contentType = error.response?.headers?.['content-type']
    if (typeof contentType === 'string' && contentType.includes('application/problem+json')) {
      const title = (error.response?.data as { title?: unknown } | undefined)?.title
      if (typeof title === 'string' && title) return title
    }
    const message = (error.response?.data as { message?: unknown } | undefined)?.message
    if (typeof message === 'string' && message) return message
    return error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
