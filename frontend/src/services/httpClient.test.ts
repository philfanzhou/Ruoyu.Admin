import { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

function response(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status }) }
function rejected(config: InternalAxiosRequestConfig, status: number, data: unknown = {}) {
  return new AxiosError('fixed failure', 'ERR_BAD_RESPONSE', config, undefined, { status, statusText: '', data, headers: {}, config })
}
async function setup() {
  const auth = await import('./auth')
  vi.mocked(fetch).mockResolvedValue(response({ authenticated: true, displayName: null, requiresReauthentication: false }))
  await auth.initializeSession()
  vi.mocked(fetch).mockReset().mockResolvedValue(response({ requestToken: 'synthetic-business-csrf' }))
  const { default: client } = await import('./httpClient')
  return { auth, client }
}
beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()) })
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('same-origin Cookie management requests', () => {
  it('never sends Authorization or basic auth and does not request CSRF for reads', async () => {
    const { client } = await setup(); const adapter = vi.fn(async config => ({ status: 200, statusText: '', data: {}, headers: {}, config }))
    client.defaults.adapter = adapter
    await client.get('/api/admin/students', { headers: { Authorization: 'obsolete value' }, auth: { username: 'obsolete', password: 'synthetic' } })
    expect(adapter.mock.calls[0]?.[0].headers.has('Authorization')).toBe(false)
    expect(adapter.mock.calls[0]?.[0].auth).toBeUndefined()
    expect(fetch).not.toHaveBeenCalled()
  })

  it.each(['post', 'put', 'patch', 'delete'])('adds exactly one ordinary CSRF header for %s without Bearer', async method => {
    const { client } = await setup(); let sent!: InternalAxiosRequestConfig
    client.defaults.adapter = async config => { sent = config; return { status: 200, statusText: '', data: {}, headers: {}, config } }
    await client.request({ url: '/api/teacher-portal/admin/teachers', method, headers: { 'X-CSRF-TOKEN': ['obsolete', 'duplicate'] } })
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch).toHaveBeenCalledWith('/api/auth/csrf', expect.objectContaining({ credentials: 'same-origin' }))
    expect(sent.headers.get('X-CSRF-TOKEN')).toBe('synthetic-business-csrf')
    expect(sent.headers.has('Authorization')).toBe(false)
  })

  it('single-flights concurrent first CSRF requests to avoid conflicting initial Cookie tokens', async () => {
    const { client } = await setup(); let release!: (value: Response) => void
    vi.mocked(fetch).mockReturnValue(new Promise(yes => { release = yes }))
    const sent: InternalAxiosRequestConfig[] = []
    client.defaults.adapter = async config => { sent.push(config); return { status: 200, statusText: '', data: {}, headers: {}, config } }
    const writes = [client.post('/api/admin/students', {}), client.post('/api/identity/gateway/users/batch', [])]
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    release(response({ requestToken: 'same-csrf' })); await Promise.all(writes)
    expect(sent.map(x => x.headers.get('X-CSRF-TOKEN'))).toEqual(['same-csrf', 'same-csrf'])
  })

  it.each([[400, 'unchanged'], [401, 'anonymous'], [403, 'forbidden']])('does not send or replay a write after CSRF HTTP%s', async (status, expected) => {
    const { auth, client } = await setup(); const navigate = vi.fn(); auth.onSessionInvalidated(navigate)
    vi.mocked(fetch).mockResolvedValue(response({}, Number(status)))
    const adapter = vi.fn(); client.defaults.adapter = adapter
    await expect(client.post('/api/admin/oss-audit/trigger')).rejects.toThrow()
    expect(adapter).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1)
    expect(auth.session.status).toBe(expected === 'unchanged' ? 'authenticated' : expected)
    expect(navigate).toHaveBeenCalledTimes(status === 400 ? 0 : 1)
  })

  it('preserves the fixed expired-token reason from CSRF and never sends the original write', async () => {
    const { auth, client } = await setup(); vi.mocked(fetch).mockResolvedValue(response({ error: 'reauthentication_required' }, 401))
    const adapter = vi.fn(); client.defaults.adapter = adapter
    await expect(client.post('/api/admin/students', {})).rejects.toThrow()
    expect(adapter).not.toHaveBeenCalled(); expect(auth.session.status).toBe('reauthentication')
  })

  it('does not refresh CSRF or retry a failed business write', async () => {
    const { client } = await setup(); const adapter = vi.fn(async config => { throw rejected(config, 400, { error: 'csrf_invalid' }) }); client.defaults.adapter = adapter
    await expect(client.post('/api/admin/oss-audit/records/1/resolve')).rejects.toThrow()
    expect(adapter).toHaveBeenCalledTimes(1); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('cancellation during CSRF never reaches the business adapter', async () => {
    const { client } = await setup(); let release!: (value: Response) => void
    vi.mocked(fetch).mockReturnValue(new Promise(yes => { release = yes }))
    const adapter = vi.fn(); client.defaults.adapter = adapter; const cancel = new AbortController()
    const write = client.post('/api/admin/students', {}, { signal: cancel.signal }); const result = expect(write).rejects.toThrow()
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(1)); cancel.abort(); release(response({ requestToken: 'synthetic' }))
    await result; expect(adapter).not.toHaveBeenCalled()
  })

  it('rejects external transport before any Cookie/CSRF request', async () => {
    const { client } = await setup(); const adapter = vi.fn(); client.defaults.adapter = adapter
    await expect(client.post('https://outside.invalid/api/admin/students')).rejects.toThrow('同源 BFF')
    expect(fetch).not.toHaveBeenCalled(); expect(adapter).not.toHaveBeenCalled()
  })
})

describe('generation-bound API invalidation', () => {
  it('multiple concurrent 401s navigate once and do not automatically retry writes', async () => {
    const { auth, client } = await setup(); const navigate = vi.fn(); auth.onSessionInvalidated(navigate)
    const adapter = vi.fn(async config => { throw rejected(config, 401, { error: 'reauthentication_required' }) }); client.defaults.adapter = adapter
    const outcomes = await Promise.allSettled([client.get('/api/admin/students'), client.get('/api/teacher-portal/admin/teachers'), client.get('/api/assistant-portal/admin/assistants')])
    expect(outcomes.every(x => x.status === 'rejected')).toBe(true)
    expect(adapter).toHaveBeenCalledTimes(3); expect(navigate).toHaveBeenCalledTimes(1)
    expect(auth.session.status).toBe('reauthentication'); expect(fetch).not.toHaveBeenCalled()
  })

  it('403 displays a forbidden state with one status navigation rather than initiating authorization', async () => {
    const { auth, client } = await setup(); const navigate = vi.fn(); auth.onSessionInvalidated(navigate)
    client.defaults.adapter = async config => { throw rejected(config, 403) }
    await expect(client.get('/api/identity/gateway/users/search')).rejects.toThrow()
    expect(auth.session.status).toBe('forbidden'); expect(navigate).toHaveBeenCalledTimes(1)
    expect(fetch).not.toHaveBeenCalled()
  })

  it('does not invalidate a newer session for a delayed 401 from the previous generation', async () => {
    const { auth, client } = await setup(); let reject!: (error: unknown) => void; let sent!: InternalAxiosRequestConfig
    client.defaults.adapter = config => { sent = config; return new Promise((_yes, no) => { reject = no }) }
    const old = client.get('/api/admin/students'); const outcome = expect(old).rejects.toThrow()
    await vi.waitFor(() => expect(sent).toBeDefined())
    auth.invalidateSession('anonymous', auth.sessionGeneration(), false)
    vi.mocked(fetch).mockResolvedValue(response({ authenticated: true, displayName: 'New session', requiresReauthentication: false }))
    await auth.initializeSession(true); const navigate = vi.fn(); auth.onSessionInvalidated(navigate)
    reject(rejected(sent, 401)); await outcome
    expect(auth.session.status).toBe('authenticated'); expect(auth.session.displayName).toBe('New session'); expect(navigate).not.toHaveBeenCalled()
  })
})
