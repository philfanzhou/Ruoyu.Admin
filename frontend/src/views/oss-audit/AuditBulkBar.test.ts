import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import AuditBulkBar from './AuditBulkBar.vue'

/**
 * 批量条的计数与运行态（#24 批量表「快速重复点击只发一次」的 UI 一半；提交守卫
 * 本身在 OssAuditView 用例中断言）。count 由父组件传入，只统计 status===0 的勾选
 * （统计逻辑在 OssAuditView 的 selectedIds 不变量中）。
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
