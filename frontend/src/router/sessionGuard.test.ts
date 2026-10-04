import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { Router } from 'vue-router'
let router: Router | undefined
const profile = (authenticated: boolean, requiresReauthentication = false) => ({ authenticated, requiresReauthentication, displayName: authenticated ? 'Server name' : null })
const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status })
beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()); window.history.replaceState({}, '', '/'); localStorage.clear() })
afterEach(() => { router?.options.history.destroy(); router = undefined; vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('async authoritative route guard', () => {
  it('waits for single-flight authority before allowing a protected deep link', async () => {
    let release!: (value: Response) => void
    vi.mocked(fetch).mockReturnValue(new Promise(yes => { release = yes }))
    router = (await import('./index')).default
    const pending = router.push('/students?grade=7')
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    expect(router.currentRoute.value.path).not.toBe('/students')
    release(response(profile(true))); await pending
    expect(router.currentRoute.value.fullPath).toBe('/students?grade=7')
  })

  it('ignores obsolete browser tokens and preserves the unauthenticated target', async () => {
    localStorage.setItem('adminAuthToken', 'obsolete synthetic value')
    vi.mocked(fetch).mockResolvedValue(response(profile(false)))
    router = (await import('./index')).default; await router.push('/upload-records?status=1')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.returnUrl).toBe('/upload-records?status=1')
    expect(localStorage.getItem('adminAuthToken')).toBeNull(); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it.each([[401, 'anonymous'], [403, 'forbidden'], [503, 'unavailable']])('keeps HTTP%s at the status page without OIDC looping', async (status, expected) => {
    vi.mocked(fetch).mockResolvedValue(response({}, Number(status)))
    router = (await import('./index')).default; await router.push('/teachers')
    const auth = await import('../services/auth')
    expect(auth.session.status).toBe(expected); expect(router.currentRoute.value.name).toBe('login'); expect(fetch).toHaveBeenCalledTimes(1)
    await router.push('/login'); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('retains explicit expiry and callback/exit status instead of bouncing away', async () => {
    vi.mocked(fetch).mockResolvedValue(response(profile(false, true)))
    router = (await import('./index')).default; await router.push('/students?grade=7')
    const auth = await import('../services/auth'); expect(auth.session.status).toBe('reauthentication')
    await router.push('/login?authError=cancelled&returnUrl=%2Fstudents%3Fgrade%3D7')
    expect(router.currentRoute.value.path).toBe('/login')
  })

  it('returns a valid administrator to the safe original business query', async () => {
    vi.mocked(fetch).mockResolvedValue(response(profile(true)))
    router = (await import('./index')).default
    await router.push('/login?returnUrl=%2Fstudents%3Fgrade%3D7')
    expect(router.currentRoute.value.fullPath).toBe('/students?grade=7')
    await router.push('/login?loggedOut=1'); expect(router.currentRoute.value.path).toBe('/login')
  })

  it('routes API invalidation to one controlled status page while retaining the current deep link', async () => {
    vi.mocked(fetch).mockResolvedValue(response(profile(true)))
    router = (await import('./index')).default; await router.push('/assistants?page=2')
    const auth = await import('../services/auth'); auth.invalidateSession('forbidden')
    await vi.waitFor(() => expect(router!.currentRoute.value.name).toBe('login'))
    expect(router.currentRoute.value.query.returnUrl).toBe('/assistants?page=2')
    expect(fetch).toHaveBeenCalledTimes(1)
  })
})
