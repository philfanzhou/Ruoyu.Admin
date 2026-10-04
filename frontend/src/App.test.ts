import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
let wrapper: VueWrapper | undefined
const response = (data: unknown) => new Response(JSON.stringify(data), { status: 200 })
async function page(displayName: string | null) {
  const auth = await import('./services/auth')
  vi.mocked(fetch).mockResolvedValue(response({ authenticated: true, displayName, requiresReauthentication: false }))
  await auth.initializeSession()
  const { default: App } = await import('./App.vue')
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/students', name: 'students', meta: { title: '学生管理' }, component: { template: '<p>业务页面</p>' } }, { path: '/login', component: { template: '<p>会话状态页</p>' } }] })
  await router.push('/students'); await router.isReady()
  wrapper = mount(App, { global: { plugins: [router, ElementPlus] } }); await flushPromises()
  return { auth, router, wrapper }
}
beforeEach(() => {
  vi.resetModules(); vi.stubGlobal('fetch', vi.fn()); localStorage.clear()
  vi.stubGlobal('matchMedia', vi.fn(query => ({ matches: false, media: query, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
})
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); vi.unstubAllGlobals() })
describe('server-session application shell', () => {
  it('displays only the current server name and never an obsolete stored administrator', async () => {
    localStorage.setItem('adminUsername', 'obsolete stored administrator')
    const { wrapper } = await page('服务器显示名')
    expect(wrapper.find('.name').text()).toBe('服务器显示名')
    expect(wrapper.text()).not.toContain('obsolete stored administrator')
  })
  it('allows a missing server name without inventing an identity', async () => {
    const { wrapper } = await page(null)
    expect(wrapper.find('.name').text()).toBe(''); expect(wrapper.find('.adm-avatar').text()).toBe('')
  })
  it('ends a Cookie session without any browser token and coalesces repeated exit clicks', async () => {
    const { wrapper, router } = await page(null)
    vi.mocked(fetch).mockReset().mockResolvedValueOnce(response({ requestToken: 'synthetic' })).mockResolvedValueOnce(response({ success: true, upstreamLogout: false }))
    const leave = wrapper.findAll('button').find(button => button.text().includes('退出登录'))!
    await Promise.all([leave.trigger('click'), leave.trigger('click')])
    await vi.waitFor(() => expect(router.currentRoute.value.path).toBe('/login'))
    expect(vi.mocked(fetch).mock.calls.map(call => call[0])).toEqual(['/api/auth/logout/csrf', '/api/auth/logout'])
    expect(localStorage.getItem('adminAuthToken')).toBeNull()
  })
  it('keeps a still-valid local session visible with an unknown exit result after disconnect', async () => {
    const { wrapper, router, auth } = await page('Server name')
    vi.mocked(fetch).mockReset().mockResolvedValueOnce(response({ requestToken: 'synthetic' })).mockRejectedValueOnce(new Error('lost'))
      .mockResolvedValueOnce(response({ authenticated: true, displayName: 'Server name', requiresReauthentication: false }))
    const leave = wrapper.findAll('button').find(button => button.text().includes('退出登录'))!
    await leave.trigger('click')
    await vi.waitFor(() => expect(auth.session.signingOut).toBe(false)); await flushPromises()
    expect(router.currentRoute.value.path).toBe('/students'); expect(wrapper.text()).toContain('退出结果未确认')
    expect(wrapper.text()).not.toContain('已退出管理后台')
    expect(vi.mocked(fetch).mock.calls.filter(call => call[1]?.method === 'POST')).toHaveLength(1)
  })
})
