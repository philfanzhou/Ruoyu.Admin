import httpClient from './httpClient'

class StudentAssociationApiClient {
  /**
   * Get the student IDs associated with a teacher/assistant.
   */
  async getStudents(userId: string, role: 'teacher' | 'assistant'): Promise<string[]> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    const response = await httpClient.get<{ success: boolean; data: string[] }>(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students`
    )
    return response.data.data
  }

  /**
   * Add a student association for a teacher/assistant.
   */
  async addStudent(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    await httpClient.post(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
  }

  /**
   * Remove a student association from a teacher/assistant.
   */
  async removeStudent(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void> {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    await httpClient.delete(
      `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students/${encodeURIComponent(studentId)}`
    )
  }
}

export const studentAssociationClient = new StudentAssociationApiClient()

export default studentAssociationClient
