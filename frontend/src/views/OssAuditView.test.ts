import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ElementPlus, { ElMessage } from 'element-plus'

vi.mock('../services/ossAuditApi', () => ({
  ossAuditApi: {
    getRecords: vi.fn(),
    resolveRecord: vi.fn(),
    batchResolve: vi.fn(),
    ignoreRecord: vi.fn(),
    triggerAudit: vi.fn(),
    getStatus: vi.fn(),
  },
  // Mirror of the real extractor: axios errors surface the backend message verbatim.
  getOssAuditErrorMessage: (error: any) =>
    error?.response?.data?.message ?? error?.message ?? 'Unknown error occurred.',
  formatFileSize: (bytes: number) => `${bytes} B`,
}))

vi.mock('../utils/confirm', () => ({
  confirmDanger: vi.fn(),
}))

import OssAuditView from './OssAuditView.vue'
import { ossAuditApi, type OssAuditRecordDto } from '../services/ossAuditApi'
import { confirmDanger } from '../utils/confirm'

const AuditRecordTableStub = {
  props: ['records', 'loading', 'error', 'selectedIds', 'resolvingIds'],
  emits: ['toggle', 'toggleAll', 'resolve', 'ignore', 'copy', 'retry'],
  template: '<div class="audit-record-table-stub" />',
}
const AuditBulkBarStub = {
  props: ['count', 'loading'],
  emits: ['resolve', 'ignore', 'cancel'],
  template: '<div class="audit-bulk-bar-stub" />',
}

function makeRecord(id: number, overrides: Partial<OssAuditRecordDto> = {}): OssAuditRecordDto {
  return {
    id,
    objectPath: `uploads/regression/record-${id}.jpg`,
    bucket: 'uploads',
    size: 1024,
    lastModified: 1759000000,
    status: 0,
    statusText: 'Pending',
    createdAt: 1759000000,
    resolvedAt: null,
    note: null,
    ...overrides,
  }
}

async function mountView(): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: OssAuditView }],
  })
  const wrapper = mount(OssAuditView, {
    global: {
      plugins: [router, ElementPlus],
      stubs: {
        PageHeader: { template: '<div><slot name="actions" /></div>' },
        FilterBar: { template: '<div><slot /></div>', emits: ['search', 'reset'] },
        ListPagination: { template: '<div />' },
        AuditStatusPanel: { template: '<div />' },
        IgnoreRecordDialog: { template: '<div />' },
        AuditRecordTable: AuditRecordTableStub,
        AuditBulkBar: AuditBulkBarStub,
      },
    },
  })
  await router.isReady()
  await flushPromises()
  return wrapper
}

const getRecords = vi.mocked(ossAuditApi.getRecords)
const resolveRecord = vi.mocked(ossAuditApi.resolveRecord)
const batchResolve = vi.mocked(ossAuditApi.batchResolve)
const getStatus = vi.mocked(ossAuditApi.getStatus)
const confirmDangerMock = vi.mocked(confirmDanger)

let messageSuccess: ReturnType<typeof vi.spyOn>
let messageError: ReturnType<typeof vi.spyOn>
let messageWarning: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  vi.clearAllMocks()
  messageSuccess = vi.spyOn(ElMessage, 'success').mockImplementation((() => undefined) as never)
  messageError = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
  messageWarning = vi.spyOn(ElMessage, 'warning').mockImplementation((() => undefined) as never)
  confirmDangerMock.mockResolvedValue(true)
  getStatus.mockResolvedValue({
    isRunning: false,
    lastCompleted: null,
    lastFailed: null,
    pendingCount: 0,
  })
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('OssAuditView v1 不可处置观察', () => {
  it.each([0, 1, 2, 3])('状态 %s 的记录没有选择或删除路径，包括陈旧子组件事件', async (status) => {
    const record = makeRecord(7, { status })
    getRecords.mockResolvedValue({ items: [record], totalCount: 1, page: 1, pageSize: 20,
      statusCounts: { [status]: 1 }, bucketCounts: { uploads: 1 } })
    const wrapper = await mountView()
    const table = wrapper.findComponent(AuditRecordTableStub)
    table.vm.$emit('toggle', record)
    table.vm.$emit('toggleAll')
    table.vm.$emit('resolve', record)
    await flushPromises()
    expect(table.props('selectedIds')).toEqual([])
    expect(table.props('records')).toEqual([record])
    expect(wrapper.findComponent(AuditBulkBarStub).exists()).toBe(false)
    expect(resolveRecord).not.toHaveBeenCalled()
    expect(batchResolve).not.toHaveBeenCalled()
    expect(confirmDangerMock).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('不能据此删除文件')
    wrapper.unmount()
  })

  it('失败加载保留可重试错误，重试恢复观察结果', async () => {
    getRecords.mockRejectedValueOnce({ response: { data: { message: 'references_unavailable' } } })
    const record = makeRecord(11, { status: 3 })
    getRecords.mockResolvedValueOnce({ items: [record], totalCount: 1, page: 1, pageSize: 20,
      statusCounts: { 3: 1 }, bucketCounts: { uploads: 1 } })
    const wrapper = await mountView()
    const table = wrapper.findComponent(AuditRecordTableStub)
    expect(table.props('records')).toEqual([])
    expect(table.props('error')).toBe('references_unavailable')
    table.vm.$emit('retry')
    await flushPromises()
    expect(table.props('records')).toEqual([record])
    expect(table.props('error')).toBeNull()
    expect(resolveRecord).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
