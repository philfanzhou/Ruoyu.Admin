import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus, { ElMessage } from 'element-plus'
import { AxiosError } from 'axios'

vi.mock('../services/settingsApi', () => {
  const api = {
    listSettings: vi.fn(),
    updateSetting: vi.fn(),
    deleteSetting: vi.fn(),
  }
  return { default: api, settingsApi: api }
})

import SettingsView from './SettingsView.vue'
import settingsApi, { type SettingDto } from '../services/settingsApi'
import { NAV_GROUPS } from '../router/navigation'

/**
 * The four #119 flows (list render, edit submit, restore default, backend
 * error) plus the front-end interception of out-of-domain input. The settings
 * API is mocked; the error extractor runs for real against AxiosError inputs
 * so the backend `message` surfaces exactly as in production.
 */

const hourOverride: SettingDto = {
  key: 'OssAudit:ScheduledHour',
  value: '2',
  source: 'db',
  updatedAt: 1759000000,
  updatedBy: 'admin-1',
}

const minuteDefault: SettingDto = {
  key: 'OssAudit:ScheduledMinute',
  value: '30',
  source: 'default',
}

// A realistic backend rejection: an AxiosError whose response body carries the
// fixed domain message (same wire shape as the 400/404 from the controller).
function backendError(status: number, message: string): AxiosError {
  return new AxiosError(
    `Request failed with status code ${status}`,
    AxiosError.ERR_BAD_REQUEST,
    undefined,
    undefined,
    { status, data: { message }, headers: {}, config: {} as never } as never,
  )
}

// el-table renders its rows asynchronously (layout pass on a timer); settle the
// micro/timer queue before asserting on the DOM.
async function settle(): Promise<void> {
  await nextTick()
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 20))
}

async function mountView(): Promise<VueWrapper> {
  const wrapper = mount(SettingsView, {
    global: {
      plugins: [ElementPlus],
      stubs: {
        PageHeader: { template: '<div />' },
        // Pass-through so the edit-dialog content renders in jsdom.
        'el-dialog': {
          props: ['modelValue'],
          template: '<div v-if="modelValue" class="dialog-stub"><slot /><slot name="footer" /></div>',
        },
      },
    },
  })
  await settle()
  return wrapper
}

async function openEditDialog(wrapper: VueWrapper): Promise<void> {
  const editButton = wrapper.findAll('tbody tr')[0].findAll('button').find((button) => button.text() === '编辑')!
  expect(editButton).toBeTruthy()
  await editButton.trigger('click')
  await settle()
}

const listSettings = vi.mocked(settingsApi.listSettings)
const updateSetting = vi.mocked(settingsApi.updateSetting)
const deleteSetting = vi.mocked(settingsApi.deleteSetting)

let messageSuccess: ReturnType<typeof vi.spyOn>
let messageError: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.clearAllMocks()
  messageSuccess = vi.spyOn(ElMessage, 'success').mockImplementation((() => undefined) as never)
  messageError = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
  listSettings.mockResolvedValue([hourOverride, minuteDefault])
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('SettingsView 系统设置页', () => {
  it('渲染全部注册键、来源标签与覆盖信息；仅数据库覆盖行可操作', async () => {
    const wrapper = await mountView()

    expect(listSettings).toHaveBeenCalledTimes(1)
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('OssAudit:ScheduledHour')
    expect(rows[0].text()).toContain('数据库覆盖')
    expect(rows[0].text()).toContain('admin-1')
    expect(rows[1].text()).toContain('OssAudit:ScheduledMinute')
    expect(rows[1].text()).toContain('默认值')
    expect(rows[1].text()).toContain('30')

    // Only the db-override row exposes edit/restore actions.
    expect(rows[0].findAll('button').map((button) => button.text())).toEqual(['编辑', '恢复默认'])
    expect(rows[1].findAll('button')).toHaveLength(0)
    expect(rows[1].text()).toContain('—')
    wrapper.unmount()
  })

  it('编辑提交调用更新接口并用返回的有效值刷新该行', async () => {
    const wrapper = await mountView()
    await openEditDialog(wrapper)

    updateSetting.mockResolvedValue({
      key: 'OssAudit:ScheduledHour',
      value: '7',
      source: 'db',
      updatedAt: 1759000099,
      updatedBy: 'admin-1',
    })
    const input = wrapper.find('.dialog-stub input')
    expect((input.element as HTMLInputElement).value).toBe('2')
    await input.setValue('7')
    const save = wrapper.findAll('button').find((button) => button.text() === '保存')!
    await save.trigger('click')
    await settle()

    expect(updateSetting).toHaveBeenCalledTimes(1)
    expect(updateSetting).toHaveBeenCalledWith('OssAudit:ScheduledHour', '7')
    expect(messageSuccess).toHaveBeenCalledWith('已保存')
    // The row refreshes from the returned effective setting.
    const row = wrapper.findAll('tbody tr')[0]
    expect(row.text()).toContain('7')
    expect(row.text()).toContain('数据库覆盖')
    wrapper.unmount()
  })

  it('恢复默认调用删除接口，来源与有效值刷新为回退结果', async () => {
    const wrapper = await mountView()

    const restore = wrapper.findAll('tbody tr')[0].findAll('button').find((button) => button.text() === '恢复默认')!
    deleteSetting.mockResolvedValue({
      key: 'OssAudit:ScheduledHour',
      value: '4',
      source: 'default',
    })
    await restore.trigger('click')
    await settle()

    expect(deleteSetting).toHaveBeenCalledTimes(1)
    expect(deleteSetting).toHaveBeenCalledWith('OssAudit:ScheduledHour')
    expect(messageSuccess).toHaveBeenCalledWith('已恢复默认')
    const row = wrapper.findAll('tbody tr')[0]
    expect(row.text()).toContain('4')
    expect(row.text()).toContain('默认值')
    // The fallback row is no longer editable.
    expect(row.findAll('button')).toHaveLength(0)
    wrapper.unmount()
  })

  it('后端错误消息经 extractApiErrorMessage 原样展示，对话框保持打开', async () => {
    const wrapper = await mountView()
    await openEditDialog(wrapper)

    const fixedMessage = "Setting 'OssAudit:ScheduledHour' must be an integer between 0 and 23."
    updateSetting.mockRejectedValue(backendError(400, fixedMessage))
    await wrapper.find('.dialog-stub input').setValue('7')
    const save = wrapper.findAll('button').find((button) => button.text() === '保存')!
    await save.trigger('click')
    await settle()

    expect(updateSetting).toHaveBeenCalledTimes(1)
    expect(messageError).toHaveBeenCalledWith(fixedMessage)
    expect(wrapper.find('.dialog-stub').exists()).toBe(true)
    wrapper.unmount()
  })

  it.each(['24', 'abc', '2.5'])('越界或非法输入 %s 被前端拦截，不发起请求', async (raw) => {
    const wrapper = await mountView()
    await openEditDialog(wrapper)

    await wrapper.find('.dialog-stub input').setValue(raw)
    const save = wrapper.findAll('button').find((button) => button.text() === '保存')!
    await save.trigger('click')
    await settle()

    expect(updateSetting).not.toHaveBeenCalled()
    expect(wrapper.find('.dialog-stub .edit-error').text()).toContain('请输入 0-23 之间的整数')
    expect(messageError).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

describe('系统设置导航接入', () => {
  it('/settings 路由挂在「系统」分组下，分组位于现有分组之后', async () => {
    const { default: appRouter } = await import('../router/index')
    const route = appRouter.options.routes.find((r) => r.path === '/settings')
    expect(route?.name).toBe('settings')
    expect(route?.meta?.title).toBe('系统设置')
    expect(route?.meta?.group).toBe('system')
    expect(route?.meta?.icon).toBeDefined()
    expect(NAV_GROUPS[NAV_GROUPS.length - 1]).toEqual({ key: 'system', label: '系统' })
  })
})
