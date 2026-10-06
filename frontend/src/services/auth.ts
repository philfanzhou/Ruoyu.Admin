import { reactive, readonly } from 'vue'

export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous' | 'reauthentication' | 'forbidden' | 'unavailable'
const view = reactive({ status: 'unknown' as SessionStatus, displayName: null as string | null, signingOut: false, logoutNotice: '' })
export const session = readonly(view)
let generation = 0
let sessionFlight: Promise<SessionStatus> | null = null
let sessionAbort: AbortController | null = null
let csrfFlight: { generation: number; promise: Promise<string> } | null = null
let logoutFlight: Promise<LogoutResult> | null = null
let invalidated: (() => void) | null = null

export function removeLegacyCredentials() {
  for (const key of ['adminAuthToken', 'adminRefreshToken', 'adminUsername']) {
    try { localStorage.removeItem(key) } catch { /* Storage is not an authentication dependency. */ }
  }
}

export function sessionGeneration() { return generation }
export function onSessionInvalidated(handler: () => void) { invalidated = handler }

export function invalidateSession(status: SessionStatus, expectedGeneration = generation, notify = true) {
  if (expectedGeneration !== generation) return
  generation++
  sessionAbort?.abort()
  sessionAbort = null
  sessionFlight = null
  csrfFlight = null
  view.status = status
  view.displayName = null
  if (notify) invalidated?.()
}

export class SessionRequestError extends Error {
  constructor(readonly status: number, readonly requiresReauthentication = false) { super('会话请求未完成，请检查状态后重试。') }
}

async function sessionJson(path: string, init: RequestInit = {}) {
  const controller = new AbortController()
  const abort = () => controller.abort()
  if (init.signal?.aborted) abort()
  init.signal?.addEventListener('abort', abort, { once: true })
  const timeout = setTimeout(abort, 12000)
  try {
    const response = await fetch(path, { ...init, signal: controller.signal, credentials: 'same-origin', cache: 'no-store', redirect: 'error' })
    if (!response.ok) {
      let requiresReauthentication = false
      if (response.status === 401) {
        try { requiresReauthentication = (await response.json())?.error === 'reauthentication_required' } catch { /* No raw error reflection. */ }
      }
      throw new SessionRequestError(response.status, requiresReauthentication)
    }
    return await response.json() as unknown
  } finally {
    clearTimeout(timeout)
    init.signal?.removeEventListener('abort', abort)
  }
}

function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }

export function initializeSession(force = false): Promise<SessionStatus> {
  removeLegacyCredentials()
  if (sessionFlight) return sessionFlight
  if (!force && view.status !== 'unknown') return Promise.resolve(view.status)
  const observed = generation
  const controller = new AbortController()
  sessionAbort = controller
  const flight = (async () => {
    let status: SessionStatus = 'unavailable'
    let displayName: string | null = null
    try {
      const data = await sessionJson('/api/auth/session', { signal: controller.signal })
      // The legacy two-field response does not prove that the session API is enabled.
      if (object(data) && typeof data.authenticated === 'boolean' && typeof data.requiresReauthentication === 'boolean'
          && (data.displayName === null || typeof data.displayName === 'string')
          && !(data.authenticated && data.requiresReauthentication)) {
        status = data.authenticated ? 'authenticated' : data.requiresReauthentication ? 'reauthentication' : 'anonymous'
        displayName = data.displayName
      }
    } catch (error) {
      if (error instanceof SessionRequestError && error.status === 403) status = 'forbidden'
      else if (error instanceof SessionRequestError && error.status === 401) status = 'anonymous'
    }
    if (observed === generation) { view.status = status; view.displayName = displayName }
    return view.status
  })()
  sessionFlight = flight
  void flight.finally(() => {
    if (sessionFlight === flight) { sessionFlight = null; sessionAbort = null }
  })
  return flight
}

export function safeReturnUrl(value: unknown): string {
  if (typeof value !== 'string' || !value || value.length > 2048) return '/dashboard'
  let candidate = value
  for (let count = 0; count < 4; count++) {
    if (!candidate.startsWith('/') || candidate.startsWith('//') || /[\\\u0000-\u001f\u007f]/.test(candidate)) return '/dashboard'
    const route = candidate.split(/[?#]/, 1)[0] ?? ''
    if (route.split('/').some(part => part === '.' || part === '..') || /^\/(login(?:\/|$)|api\/auth(?:\/|$))/i.test(route)) return '/dashboard'
    try {
      const decoded = decodeURIComponent(candidate)
      if (decoded === candidate) return value
      candidate = decoded
    } catch { return '/dashboard' }
  }
  return '/dashboard'
}

export function hostedLoginUrl(returnUrl: unknown) {
  return '/api/auth/oidc/start?returnUrl=' + encodeURIComponent(safeReturnUrl(returnUrl))
}

async function requestToken(path: string, signal?: AbortSignal): Promise<string> {
  const data = await sessionJson(path, { signal })
  if (!object(data) || typeof data.requestToken !== 'string' || !data.requestToken) throw new SessionRequestError(503)
  return data.requestToken
}

export function businessCsrf(expectedGeneration: number): Promise<string> {
  if (expectedGeneration !== generation || view.signingOut) return Promise.reject(new SessionRequestError(401))
  if (csrfFlight?.generation === generation) return csrfFlight.promise
  const promise = requestToken('/api/auth/csrf').then(token => {
    if (expectedGeneration !== generation) throw new SessionRequestError(401)
    return token
  }).catch(error => {
    if (error instanceof SessionRequestError && (error.status === 401 || error.status === 403))
      invalidateSession(error.status === 403 ? 'forbidden' : error.requiresReauthentication ? 'reauthentication' : 'anonymous', expectedGeneration)
    throw error
  })
  const flight = { generation, promise }
  csrfFlight = flight
  void promise.finally(() => { if (csrfFlight === flight) csrfFlight = null }).catch(() => {})
  return promise
}

export type LogoutResult = { kind: 'navigating' } | { kind: 'unknown' }

async function logoutAntiforgeryToken(signal?: AbortSignal): Promise<string> {
  const data = await sessionJson('/api/auth/oidc/csrf', { signal })
  if (!object(data) || typeof data.token !== 'string' || !data.token) throw new SessionRequestError(503)
  return data.token
}

export function logoutSession(): Promise<LogoutResult> {
  if (logoutFlight) return logoutFlight
  invalidateSession('unknown', generation, false)
  view.signingOut = true
  view.logoutNotice = ''
  const flight = (async (): Promise<LogoutResult> => {
    try {
      // The logout endpoint deliberately supports expired access tokens and removed administrators.
      const token = await logoutAntiforgeryToken()
      invalidateSession('anonymous', generation, false)
      // Navigational form POST: the browser follows the upstream logout redirect chain itself
      // (the server validates the logout_uri) and lands back on /login. When the upstream
      // preparation failed, the server answers the fixed local-only result and the local
      // session is already gone.
      const form = document.createElement('form')
      form.method = 'post'
      form.action = '/api/auth/oidc/logout'
      const field = document.createElement('input')
      field.type = 'hidden'
      field.name = '__RequestVerificationToken'
      field.value = token
      form.appendChild(field)
      document.body.appendChild(form)
      form.submit()
      return { kind: 'navigating' }
    } catch {
      // Discard any status query started during logout, then recover only the local fact.
      invalidateSession('unknown', generation, false)
      await initializeSession(true)
      view.logoutNotice = view.status === 'anonymous'
        ? '管理后台会话已结束；身份服务退出结果未确认。'
        : '退出结果未确认，请检查当前会话后再操作。'
      return { kind: 'unknown' }
    } finally { view.signingOut = false }
  })()
  logoutFlight = flight
  void flight.finally(() => { if (logoutFlight === flight) logoutFlight = null })
  return flight
}
