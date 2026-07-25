import httpClient from './httpClient'

class StudentLinkApi {
  private checkSuccess<T extends { success: boolean; message?: string }>(result: T): T {
    if (!result.success) {
      throw new Error(result.message || 'Operation failed')
    }
    return result
  }

  private url(role: 'teacher' | 'assistant', userId: string): string {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    return `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students`
  }

  async list(userId: string, role: 'teacher' | 'assistant'): Promise<string[]> {
    const response = await httpClient.get<{ success: boolean; data: string[] }>(this.url(role, userId))
    return this.checkSuccess(response.data).data
  }

  async link(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const response = await httpClient.post<{ success: boolean; message?: string }>(
      `${this.url(role, userId)}/${encodeURIComponent(studentId)}`
    )
    this.checkSuccess(response.data)
  }

  async unlink(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const response = await httpClient.delete<{ success: boolean; message?: string }>(
      `${this.url(role, userId)}/${encodeURIComponent(studentId)}`
    )
    this.checkSuccess(response.data)
  }
}

export const studentLinkApi = new StudentLinkApi()
export default studentLinkApi
