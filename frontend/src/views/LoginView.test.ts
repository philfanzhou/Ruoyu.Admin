import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import ElementPlus from 'element-plus'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

let wrapper: VueWrapper | undefined
const anonymous = { authenticated: false, displayName: null, requiresReauthentication: false }
function response(data: unknown, status = 200) { return new Response(JSON.stringify(data), { status }) }
async function page(data: unknown = anonymous, status = 200, query = '') {
  const auth = await import('../services/auth')
  vi.mocked(fetch).mockResolvedValue(response(data, status)); await auth.initializeSession()
  const { default: Login } = await import('./LoginView.vue')
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/login', component: Login }, { path: '/students', component: { template: '<p>students</p>' } }, { path: '/dashboard', component: { template: '<p>dashboard</p>' } }] })
  await router.push('/login' + query); await router.isReady()
  wrapper = mount(Login, { global: { plugins: [router, ElementPlus] } }); await flushPromises()
  return { auth, router, wrapper }
}
beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()) })
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('hosted login status page', () => {
  it('contains no password, username or remember form and offers only the controlled hosted link', async () => {
    const { wrapper } = await page(anonymous, 200, '?returnUrl=%2Fstudents%3Fgrade%3D7')
    expect(wrapper.findAll('input')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('记住用户名')
    expect(wrapper.find('a').attributes('href')).toBe('/api/auth/oidc/start?returnUrl=%2Fstudents%3Fgrade%3D7')
    expect(fetch).toHaveBeenCalledTimes(1) // no automatic authorization
  })

  it.each([['cancelled', '已取消登录'], ['sign_in_failed', '登录未完成'], ['identity_unavailable', '身份服务暂时不可用']])('shows the fixed %s callback status without initiating another authorization', async (error, message) => {
    const { wrapper } = await page(anonymous, 200, '?authError=' + error)
    expect(wrapper.text()).toContain(message); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('never reflects arbitrary callback query contents or an unsafe return URL', async () => {
    const { wrapper } = await page(anonymous, 200, '?authError=raw-sensitive-fixture&returnUrl=https%3A%2F%2Foutside.invalid')
    expect(wrapper.text()).not.toContain('raw-sensitive-fixture')
    expect(wrapper.find('a').attributes('href')).toBe('/api/auth/oidc/start?returnUrl=%2Fdashboard')
  })

  it('shows a distinct forbidden state with own logout rather than an authorization loop', async () => {
    const { wrapper } = await page({}, 403)
    expect(wrapper.text()).toContain('无管理权限'); expect(wrapper.text()).toContain('退出当前会话')
    expect(wrapper.find('a').exists()).toBe(false); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('lets expired access credentials explicitly authenticate or end their own session', async () => {
    const { wrapper } = await page({ ...anonymous, displayName: 'Server name', requiresReauthentication: true })
    expect(wrapper.text()).toContain('需要重新认证'); expect(wrapper.find('a').exists()).toBe(true)
    expect(wrapper.text()).toContain('退出当前会话')
  })

  it('does not claim an unavailable session is anonymous and permits an explicit status retry', async () => {
    const { wrapper, router, auth } = await page({}, 503, '?returnUrl=%2Fstudents%3Fgrade%3D7')
    expect(wrapper.text()).toContain('会话服务不可用'); expect(wrapper.find('a').exists()).toBe(false)
    vi.mocked(fetch).mockResolvedValue(response({ authenticated: true, displayName: null, requiresReauthentication: false }))
    const retry = wrapper.findAll('button').find(button => button.text().includes('重新检查'))!
    await retry.trigger('click'); await flushPromises()
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(auth.session.status).toBe('authenticated')
    await vi.waitFor(() => expect(router.currentRoute.value.fullPath).toBe('/students?grade=7'))
  })

  it('logout completion remains a status page without automatically signing in', async () => {
    const { wrapper } = await page(anonymous, 200, '?loggedOut=1')
    expect(wrapper.text()).toContain('已退出管理后台'); expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('uses the hosted-login logout CSRF and navigates away from a forbidden session', async () => {
    const { wrapper, auth } = await page({}, 403)
    const submissions: HTMLFormElement[] = []
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(function (this: HTMLFormElement) { submissions.push(this) })
    vi.mocked(fetch).mockReset().mockResolvedValueOnce(response({ token: 'synthetic' }))
    const leave = wrapper.findAll('button').find(button => button.text().includes('退出当前会话'))!
    await leave.trigger('click'); await flushPromises()
    expect(auth.session.status).toBe('anonymous')
    expect(vi.mocked(fetch).mock.calls.map(call => call[0])).toEqual(['/api/auth/oidc/csrf'])
    expect(submissions).toHaveLength(1); expect(submissions[0].action).toContain('/api/auth/oidc/logout')
    submit.mockRestore()
  })
})
