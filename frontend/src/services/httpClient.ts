import axios, { type AxiosInstance } from 'axios'
import { businessCsrf, invalidateSession, sessionGeneration } from './auth'

declare module 'axios' {
  interface AxiosRequestConfig { sessionGeneration?: number }
}

// All management clients share the same-origin Cookie / CSRF boundary. No write retry.
const httpClient: AxiosInstance = axios.create({ timeout: 30000 })
httpClient.interceptors.request.use(async config => {
  const uri = new URL(config.url ?? '', config.baseURL ?? window.location.origin)
  if (uri.origin !== window.location.origin || !uri.pathname.startsWith('/api/'))
    throw new Error('管理 API 仅支持同源 BFF。')
  config.auth = undefined
  config.headers.delete('Authorization')
  config.sessionGeneration = sessionGeneration()
  if (config.signal?.aborted) throw new axios.CanceledError()
  if (!['get', 'head', 'options', 'trace'].includes((config.method ?? 'get').toLowerCase())) {
    const token = await businessCsrf(config.sessionGeneration)
    if (config.signal?.aborted) throw new axios.CanceledError()
    config.headers.set('X-CSRF-TOKEN', token)
  }
  return config
})
httpClient.interceptors.response.use(response => response, error => {
  const observed = error.config?.sessionGeneration
  if (typeof observed === 'number') {
    if (error.response?.status === 401)
      invalidateSession(error.response.data?.error === 'reauthentication_required' ? 'reauthentication' : 'anonymous', observed)
    else if (error.response?.status === 403) invalidateSession('forbidden', observed)
  }
  return Promise.reject(error)
})

export default httpClient
