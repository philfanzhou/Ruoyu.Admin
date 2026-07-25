import httpClient from './httpClient'

class StudentAssociationApiClient {
  private checkSuccess<T extends { success: boolean; message?: string }>(result: T): T {
    if (!result.success) {
      throw new Error(result.message || 'Operation failed')
    }
    return result
  }

  /**
   * Get the student IDs associated with a teacher/assistant.
   */
  async getStudents(userId: string, role: 'teacher' | 'assistant'): Promise<string[]> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    const response = await httpClient.get<{ success: boolean; data: string[] }>(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students`
    )
    const result = this.checkSuccess(response.data)
    return result.data
  }

  /**
   * Add a student association for a teacher/assistant.
   */
  async addStudent(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    const response = await httpClient.post<{ success: boolean; message?: string }>(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
    this.checkSuccess(response.data)
  }

  /**
   * Remove a student association from a teacher/assistant.
   */
  async removeStudent(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    const response = await httpClient.delete<{ success: boolean; message?: string }>(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
    this.checkSuccess(response.data)
  }
}

export const studentAssociationClient = new StudentAssociationApiClient()

export default studentAssociationClient
