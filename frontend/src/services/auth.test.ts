import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

function response(data: unknown, status = 200) { return new Response(JSON.stringify(data), { status }) }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(yes => { resolve = yes })
  return { promise, resolve }
}
const valid = { authenticated: true, displayName: 'Server name', requiresReauthentication: false }
const anonymous = { authenticated: false, displayName: null, requiresReauthentication: false }

beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()); localStorage.clear() })
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.useRealTimers() })

describe('server session authority', () => {
  it.each([
    [valid, 'authenticated', 'Server name'],
    [{ ...valid, displayName: null }, 'authenticated', null],
    [anonymous, 'anonymous', null],
    [{ authenticated: false, displayName: 'Server name', requiresReauthentication: true }, 'reauthentication', 'Server name'],
    [{ authenticated: true, displayName: 'old two-field response' }, 'unavailable', null],
    [{ ...valid, requiresReauthentication: true }, 'unavailable', null],
  ])('maps only the complete BFF profile %j', async (data, status, name) => {
    vi.mocked(fetch).mockResolvedValue(response(data))
    const auth = await import('./auth')
    expect(await auth.initializeSession()).toBe(status)
    expect(auth.session.displayName).toBe(name)
    expect(fetch).toHaveBeenCalledWith('/api/auth/session', expect.objectContaining({ credentials: 'same-origin', redirect: 'error', cache: 'no-store' }))
  })

  it.each([[401, 'anonymous'], [403, 'forbidden'], [503, 'unavailable']])('maps HTTP%s without displaying raw errors', async (status, expected) => {
    vi.mocked(fetch).mockResolvedValue(response({ error: 'private detail must not appear' }, Number(status)))
    const auth = await import('./auth')
    expect(await auth.initializeSession()).toBe(expected)
    expect(auth.session.displayName).toBeNull()
  })

  it('coalesces concurrent initialization and removes expired legacy values without reading them', async () => {
    for (const key of ['adminAuthToken', 'adminRefreshToken', 'adminUsername']) localStorage.setItem(key, 'obsolete fixture value')
    const getter = vi.spyOn(Storage.prototype, 'getItem')
    const pending = deferred<Response>(); vi.mocked(fetch).mockReturnValue(pending.promise)
    const auth = await import('./auth'); const first = auth.initializeSession(); const second = auth.initializeSession()
    expect(first).toBe(second); expect(fetch).toHaveBeenCalledTimes(1)
    pending.resolve(response(anonymous)); expect(await first).toBe('anonymous')
    expect(getter).not.toHaveBeenCalled()
    expect(localStorage.length).toBe(0)
  })

  it('does not depend on available browser storage or successful network', async () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('storage denied') })
    vi.mocked(fetch).mockRejectedValue(new Error('raw network details'))
    const auth = await import('./auth'); expect(await auth.initializeSession()).toBe('unavailable')
  })

  it('discards a delayed authenticated result after invalidation', async () => {
    const pending = deferred<Response>(); vi.mocked(fetch).mockReturnValue(pending.promise)
    const auth = await import('./auth'); const first = auth.initializeSession(); const observed = auth.sessionGeneration()
    auth.invalidateSession('reauthentication', observed, false)
    pending.resolve(response(valid)); expect(await first).toBe('reauthentication'); expect(auth.session.displayName).toBeNull()
  })

  it('bounds a stalled status request and exposes a retryable unavailable state', async () => {
    vi.useFakeTimers()
    vi.mocked(fetch).mockImplementation((_path, options) => new Promise((_yes, no) => options?.signal?.addEventListener('abort', () => no(new DOMException('Aborted', 'AbortError')))))
    const auth = await import('./auth'); const pending = auth.initializeSession()
    await vi.advanceTimersByTimeAsync(12000); expect(await pending).toBe('unavailable')
  })
})

describe('controlled return paths', () => {
  it('retains a business deep link and its query', async () => {
    const auth = await import('./auth'); const path = '/upload-records?status=1&student=example'
    expect(auth.safeReturnUrl(path)).toBe(path)
    expect(auth.hostedLoginUrl(path)).toBe('/api/auth/oidc/start?returnUrl=' + encodeURIComponent(path))
  })
  it.each(['https://outside.invalid', '//outside.invalid', '/\\outside', '/%2foutside', '/%252foutside', '/a/../students', '/a/%2e%2e/students', '/LOGIN', '/api/auth/oidc/start', '/%6cogin', '/students%0a', '/%', '/%252525252foutside', [' /students'], 'x'.repeat(2049)])('rejects unsafe or ambiguous return %j', async path => {
    const auth = await import('./auth'); expect(auth.safeReturnUrl(path)).toBe('/dashboard')
  })
})

describe('one-shot dedicated logout', () => {
  it.each([403, 200])('permits own logout independently of current administrator/access authorization (%s)', async status => {
    vi.mocked(fetch).mockResolvedValueOnce(response(status === 403 ? {} : { ...anonymous, requiresReauthentication: true }, status))
      .mockResolvedValueOnce(response({ requestToken: 'synthetic-csrf' })).mockResolvedValueOnce(response({ success: true, upstreamLogout: false }))
    const auth = await import('./auth'); await auth.initializeSession()
    expect(await auth.logoutSession()).toEqual({ kind: 'local' })
    expect(vi.mocked(fetch).mock.calls.map(call => call[0])).toEqual(['/api/auth/session', '/api/auth/logout/csrf', '/api/auth/logout'])
    expect(fetch).toHaveBeenLastCalledWith('/api/auth/logout', expect.objectContaining({ method: 'POST', headers: { 'X-CSRF-TOKEN': 'synthetic-csrf' } }))
    expect(auth.session.status).toBe('anonymous'); expect(auth.session.logoutNotice).toContain('尚未确认')
  })

  it('coalesces repeated clicks and returns only the prepared opaque BFF URL', async () => {
    const pending = deferred<Response>(); const url = 'https://issuer.invalid/oauth2/logout?logout_handle=' + 'a'.repeat(43)
    vi.mocked(fetch).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(response({ success: true, upstreamLogout: true, logoutUrl: url }))
    const auth = await import('./auth'); const first = auth.logoutSession(); const second = auth.logoutSession()
    expect(first).toBe(second); pending.resolve(response({ requestToken: 'synthetic-csrf' }))
    expect(await first).toEqual({ kind: 'redirect', url }); expect(fetch).toHaveBeenCalledTimes(2)
  })

  it.each(['network', 'abort', 'invalid'])('reconciles unknown %s against the local fact without replaying POST', async mode => {
    vi.mocked(fetch).mockResolvedValueOnce(response({ requestToken: 'synthetic-csrf' }))
    if (mode === 'invalid') vi.mocked(fetch).mockResolvedValueOnce(response({ success: true, upstreamLogout: true, logoutUrl: 'https://outside.invalid/arbitrary' }))
    else vi.mocked(fetch).mockRejectedValueOnce(mode === 'abort' ? new DOMException('Aborted', 'AbortError') : new Error('response lost'))
    vi.mocked(fetch).mockResolvedValueOnce(response(anonymous))
    const auth = await import('./auth'); expect(await auth.logoutSession()).toEqual({ kind: 'unknown' })
    expect(auth.session.status).toBe('anonymous'); expect(auth.session.logoutNotice).toContain('退出结果未确认')
    expect(vi.mocked(fetch).mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1)
  })

  it('does not announce success when the lost logout response leaves a valid local session', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response({ requestToken: 'synthetic-csrf' })).mockRejectedValueOnce(new Error('lost'))
      .mockResolvedValueOnce(response(valid))
    const auth = await import('./auth'); expect(await auth.logoutSession()).toEqual({ kind: 'unknown' })
    expect(auth.session.status).toBe('authenticated'); expect(auth.session.logoutNotice).toContain('退出结果未确认')
    expect(auth.session.logoutNotice).not.toContain('会话已结束')
  })

  it('cannot revive a revoked frontend state from a previously pending initialization', async () => {
    const old = deferred<Response>(); vi.mocked(fetch).mockReturnValueOnce(old.promise)
      .mockResolvedValueOnce(response({ requestToken: 'synthetic-csrf' })).mockResolvedValueOnce(response({ success: true, upstreamLogout: false }))
    const auth = await import('./auth'); const initial = auth.initializeSession(); await auth.logoutSession()
    old.resolve(response(valid)); await initial; expect(auth.session.status).toBe('anonymous'); expect(auth.session.displayName).toBeNull()
  })
})


describe('status queries begun during logout', () => {
  it('discards a late pre-revocation fact even when initialization begins after logout started', async () => {
    const csrf = deferred<Response>(); const oldStatus = deferred<Response>()
    vi.mocked(fetch).mockReturnValueOnce(csrf.promise).mockReturnValueOnce(oldStatus.promise)
      .mockResolvedValueOnce(response({ success: true, upstreamLogout: false }))
    const auth = await import('./auth'); const logout = auth.logoutSession(); const status = auth.initializeSession(true)
    csrf.resolve(response({ requestToken: 'synthetic-csrf' })); expect(await logout).toEqual({ kind: 'local' })
    oldStatus.resolve(response(valid)); await status
    expect(auth.session.status).toBe('anonymous'); expect(auth.session.displayName).toBeNull()
  })

  it('uses a new authoritative status query after a lost logout response, not an earlier in-flight fact', async () => {
    const csrf = deferred<Response>(); const oldStatus = deferred<Response>()
    vi.mocked(fetch).mockReturnValueOnce(csrf.promise).mockReturnValueOnce(oldStatus.promise)
      .mockRejectedValueOnce(new Error('response lost')).mockResolvedValueOnce(response(anonymous))
    const auth = await import('./auth'); const logout = auth.logoutSession(); const status = auth.initializeSession(true)
    csrf.resolve(response({ requestToken: 'synthetic-csrf' })); expect(await logout).toEqual({ kind: 'unknown' })
    expect(vi.mocked(fetch).mock.calls.filter(call => call[0] === '/api/auth/session')).toHaveLength(2)
    oldStatus.resolve(response(valid)); await status
    expect(auth.session.status).toBe('anonymous'); expect(auth.session.logoutNotice).toContain('会话已结束')
    expect(vi.mocked(fetch).mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1)
  })
})
