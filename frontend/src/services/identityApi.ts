import axios, { type AxiosInstance } from 'axios'

export interface IdentityPagedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface IdentityUser {
  userId: string
  username: string
  phone: string
  isActive: boolean
  remark: string
  createdAt: number
  displayName: string
}

class IdentityAdminApiClient {
  private client: AxiosInstance

  constructor() {
    this.client = axios.create({
      timeout: 15000,
    })
  }

  async testConnection(): Promise<void> {
    await this.client.get('/api/identity/gateway/users/search', {
      params: {
        page: 1,
        pageSize: 1,
      },
    })
  }

  async getUsers(params: { username?: string; phone?: string; page?: number; pageSize?: number }) {
    const response = await this.client.get<IdentityPagedResponse<IdentityUser>>('/api/identity/gateway/users/search', { params })
    return response.data
  }

  async getUsersByIds(userIds: string[]) {
    const response = await this.client.post<IdentityUser[]>('/api/identity/gateway/users/batch', userIds)
    return response.data
  }
}

let _instance: IdentityAdminApiClient | null = null

export function getIdentityAdminApiClient(): IdentityAdminApiClient {
  if (!_instance) {
    _instance = new IdentityAdminApiClient()
  }
  return _instance
}

export function getIdentityErrorMessage(error: unknown) {
  if (axios.isAxiosError(error)) {
    return (error.response?.data as { message?: string } | undefined)?.message ?? error.message
  }
  if (error instanceof Error) return error.message
  return 'Unknown error occurred.'
}
