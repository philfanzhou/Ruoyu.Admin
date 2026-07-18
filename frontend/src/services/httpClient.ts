import axios, { type AxiosInstance } from 'axios'
import { clearAuth, getAuthToken } from './auth'

// Shared axios instance for all admin_portal API clients.
//
// Why a shared instance: axios instances created via axios.create() do NOT
// inherit interceptors from the global `axios`. Each API client that calls
// axios.create() gets its own interceptor chain, so interceptors registered
// on the global `axios` (e.g. the auth-header injector and the 401 redirect)
// never fire for those clients. This previously caused every authenticated
// /api/admin/* request to be sent without an Authorization header, yielding
// HTTP 401 even though the user had logged in successfully.
//
// All API clients (studentAdminApi / teacherPortalApi / assistantPortalApi /
// identityApi / ossAuditApi) MUST reuse this instance instead of calling
// axios.create() themselves. Per-client timeout overrides can still be passed
// to individual requests via the axios `timeout` config option.
const httpClient: AxiosInstance = axios.create({
  timeout: 30000,
})

// Request interceptor: attach JWT Bearer token from localStorage.
httpClient.interceptors.request.use((config) => {
  const token = getAuthToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// Response interceptor: on 401, clear stored credentials and redirect to login.
// Uses window.location to avoid a circular import with the Vue router
// (router imports from services/auth, which would be re-entered via this module).
httpClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearAuth()
      if (window.location.pathname !== '/login') {
        window.location.href = '/login'
      }
    }
    return Promise.reject(error)
  },
)

export default httpClient
