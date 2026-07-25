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
