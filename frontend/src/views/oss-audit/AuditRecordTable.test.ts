import { describe, expect, it } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus from 'element-plus'
import AuditRecordTable from './AuditRecordTable.vue'
import type { OssAuditRecordDto } from '../../services/ossAuditApi'

/**
 * 非待处理行的不可处置保证（#24：checkbox disabled、操作列「—」、批量计数只统计
 * status===0 的行级一半；计数一半在 AuditBulkBar/OssAuditView 用例中）。
 * 真实渲染 Element Plus（el-table/el-checkbox），断言落在真实 DOM 属性上。
 */

function makeRecord(id: number, status: number): OssAuditRecordDto {
  return {
    id,
    objectPath: `uploads/regression/record-${id}.jpg`,
    bucket: 'uploads',
    size: 1024,
    lastModified: 1759000000,
    status,
    statusText: 'Pending',
    createdAt: 1759000000,
    resolvedAt: null,
    note: null,
  }
}

// el-table renders its rows asynchronously (layout pass on a timer); settle the
// micro/timer queue before asserting on the DOM.
async function settle(): Promise<void> {
  await nextTick()
  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 20))
}

async function mountTable(records: OssAuditRecordDto[], selectedIds: number[] = []): Promise<VueWrapper> {
  const wrapper = mount(AuditRecordTable, {
    props: {
      records,
      loading: false,
      error: null,
      selectedIds,
      resolvingIds: new Set<number>(),
    },
    global: { plugins: [ElementPlus] },
  })
  await settle()
  return wrapper
}

function rowCheckbox(wrapper: VueWrapper, rowIndex: number) {
  return wrapper.findAll('tbody input[type="checkbox"]')[rowIndex]
}

describe('AuditRecordTable 非待处理记录不可处置', () => {  const pending = makeRecord(1, 0)
  const resolved = makeRecord(2, 1)
  const ignored = makeRecord(3, 2)
  const records = [pending, resolved, ignored]

  it('非待处理（已删除/已忽略）行 checkbox disabled，待处理行可选', async () => {
    const wrapper = await mountTable(records)

    expect((rowCheckbox(wrapper, 0).element as HTMLInputElement).disabled).toBe(false)
    expect((rowCheckbox(wrapper, 1).element as HTMLInputElement).disabled).toBe(true)
    expect((rowCheckbox(wrapper, 2).element as HTMLInputElement).disabled).toBe(true)
    wrapper.unmount()
  })

  it('非待处理行操作列显示「—」，无删除/忽略按钮；待处理行有删除与忽略', async () => {
    const wrapper = await mountTable(records)

    const rows = wrapper.findAll('tbody tr')
    expect(rows.length).toBe(3)
    // The trailing status label is 已删除/已忽略, so assert on the operation cell only.
    const actionCells = rows.map((row) => row.findAll('td')[row.findAll('td').length - 1].text())
    expect(actionCells[0]).toContain('删除')
    expect(actionCells[0]).toContain('忽略')
    // Non-pending rows show only the em-dash: no delete, no ignore.
    expect(actionCells[1].trim()).toBe('—')
    expect(actionCells[2].trim()).toBe('—')
    wrapper.unmount()
  })

  it('当前页没有待处理记录时表头全选 checkbox disabled', async () => {
    const wrapper = await mountTable([resolved, ignored])

    const headerCheckbox = wrapper.find('thead input[type="checkbox"]').element as HTMLInputElement
    expect(headerCheckbox.disabled).toBe(true)
    wrapper.unmount()
  })

  it('待处理行的 checkbox change 会向上 emit toggle（选中状态的唯一事实在父组件）', async () => {
    const wrapper = await mountTable(records)

    // Drive the row checkbox's change event (its disabled state for non-pending rows is
    // already asserted above, which is what blocks user interaction there).
    const rows = wrapper.findAll('tbody tr')
    ;(rows[0].findComponent({ name: 'ElCheckbox' }).vm as never as { $emit: (event: string, value: boolean) => void })
      .$emit('change', true)
    await settle()

    expect(wrapper.emitted('toggle')?.length).toBe(1)
    expect(wrapper.emitted('toggle')?.[0]).toEqual([pending])
    wrapper.unmount()
  })
})
