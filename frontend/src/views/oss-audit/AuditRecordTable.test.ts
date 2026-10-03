import { describe, expect, it } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { nextTick } from 'vue'
import ElementPlus from 'element-plus'
import AuditRecordTable from './AuditRecordTable.vue'
import type { OssAuditRecordDto } from '../../services/ossAuditApi'

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

describe('AuditRecordTable v1 只读引用观察', () => {
  it('全部新旧状态均无选择框和删除按钮，保留历史待处理忽略操作', async () => {
    const wrapper = await mountTable([0, 1, 2, 3].map((status) => makeRecord(status + 1, status)))
    expect(wrapper.findAll('input[type="checkbox"]')).toHaveLength(0)
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(4)
    const actions = rows.map((row) => row.findAll('td').at(-1)!.text())
    expect(actions[0]).toBe('忽略')
    expect(actions.slice(1)).toEqual(['—', '—', '—'])
    expect(rows[3].text()).toContain('未观察到引用')
    expect(wrapper.emitted('resolve')).toBeUndefined()
    wrapper.unmount()
  })

  it('历史待处理忽略操作仍发出原记录，观察记录没有忽略入口', async () => {
    const record = makeRecord(7, 0)
    const wrapper = await mountTable([record, makeRecord(8, 3)])
    const button = wrapper.findAll('tbody tr')[0].findAll('button').find((item) => item.text() === '忽略')!
    await button.trigger('click')
    expect(wrapper.emitted('ignore')).toEqual([[record]])
    expect(wrapper.findAll('tbody tr')[1].findAll('button').some((item) => item.text() === '忽略')).toBe(false)
    wrapper.unmount()
  })
})
