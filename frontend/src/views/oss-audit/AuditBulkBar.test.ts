import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import AuditBulkBar from './AuditBulkBar.vue'

/**
 * Bulk-bar count and running state (the UI half of the issue #24 bulk table
 * row "rapid repeated clicks send only once"; the submit guard itself is
 * asserted in the OssAuditView cases). count comes from the parent and only
 * tallies checked rows with status===0 (the tallying logic lives in the
 * selectedIds invariant of OssAuditView).
 */

describe('AuditBulkBar', () => {
  it('count > 0 时显示已选数量；count === 0 时不渲染', async () => {
    const wrapper = mount(AuditBulkBar, {
      props: { count: 3, loading: false },
      global: { plugins: [ElementPlus] },
    })
    expect(wrapper.text()).toContain('已选择 3 个文件')
    expect(wrapper.text()).toContain('批量删除')

    await wrapper.setProps({ count: 0 })
    expect(wrapper.find('.bulk-bar').exists()).toBe(false)
    wrapper.unmount()
  })

  it('loading 期间批量删除按钮进入 loading 态，忽略与取消被禁用（防重复提交的 UI 半边）', async () => {
    const wrapper = mount(AuditBulkBar, {
      props: { count: 2, loading: false },
      global: { plugins: [ElementPlus] },
    })

    const buttons = wrapper.findAll('button')
    await wrapper.setProps({ loading: true })

    const [resolveButton, ignoreButton, cancelButton] = wrapper.findAll('button')
    expect(resolveButton.classes()).toContain('is-loading')
    expect((ignoreButton.element as HTMLButtonElement).disabled).toBe(true)
    expect((cancelButton.element as HTMLButtonElement).disabled).toBe(true)
    wrapper.unmount()
  })

  it('点击批量删除/忽略/取消分别 emit 对应事件', async () => {
    const wrapper = mount(AuditBulkBar, {
      props: { count: 1, loading: false },
      global: { plugins: [ElementPlus] },
    })

    const [resolveButton, ignoreButton, cancelButton] = wrapper.findAll('button')
    await resolveButton.trigger('click')
    await ignoreButton.trigger('click')
    await cancelButton.trigger('click')

    expect(wrapper.emitted('resolve')?.length).toBe(1)
    expect(wrapper.emitted('ignore')?.length).toBe(1)
    expect(wrapper.emitted('cancel')?.length).toBe(1)
    wrapper.unmount()
  })
})
