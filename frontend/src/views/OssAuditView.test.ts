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

/**
 * 破坏性操作不变量（#24 准备评论「破坏性路径语义模型」两表为唯一权威）中归属
 * OssAuditView 的行：单条 resolve 成功/失败、批量快照 ids、防重复提交、未勾选警告、
 * 部分失败只报计数。confirmDanger / ossAuditApi 均 mock：组件级用例只驱动确认与
 * 取消两条路径（confirmDanger 自身的行为由 src/utils/confirm.test.ts 锁定）。
 */

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

describe('OssAuditView handleResolve（#24 单条删除表）', () => {
  it('确认删除且无引用（200）：该行从列表消失（后端 Remove，非置灰），成功 toast', async () => {
    const first = makeRecord(1)
    const second = makeRecord(2)
    getRecords.mockResolvedValueOnce({
      items: [first, second],
      totalCount: 2,
      page: 1,
      pageSize: 20,
      statusCounts: { 0: 2 },
      bucketCounts: { uploads: 2 },
    })
    // Refresh after the successful delete no longer returns the resolved record.
    getRecords.mockResolvedValueOnce({
      items: [second],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      statusCounts: { 0: 1 },
      bucketCounts: { uploads: 1 },
    })
    resolveRecord.mockResolvedValue({ success: true, message: 'Record, OSS object and fingerprint deleted.' })

    const wrapper = await mountView()
    const table = wrapper.findComponent(AuditRecordTableStub)
    table.vm.$emit('resolve', first)
    await flushPromises()

    expect(confirmDangerMock).toHaveBeenCalledWith(
      expect.objectContaining({ title: '删除存储文件', confirmText: '删除' }),
    )
    expect(resolveRecord).toHaveBeenCalledWith(1)
    expect(messageSuccess).toHaveBeenCalledWith('删除成功')
    const refreshedRecords = wrapper.findComponent(AuditRecordTableStub).props('records') as OssAuditRecordDto[]
    expect(refreshedRecords.map((r) => r.id)).toEqual([2])
    wrapper.unmount()
  })

  it.each([400, 502, 500])(
    '删除失败（%s）：toast 原样透传后端 message，记录保留且仍为待处理，不触发刷新',
    async (status) => {
      const backendMessage = `后端原文-${status}`
      const record = makeRecord(7)
      getRecords.mockResolvedValueOnce({
        items: [record],
        totalCount: 1,
        page: 1,
        pageSize: 20,
        statusCounts: { 0: 1 },
        bucketCounts: { uploads: 1 },
      })
      resolveRecord.mockRejectedValue({
        response: { status, data: { message: backendMessage } },
      })

      const wrapper = await mountView()
      wrapper.findComponent(AuditRecordTableStub).vm.$emit('resolve', record)
      await flushPromises()

      expect(messageError).toHaveBeenCalledWith(backendMessage)
      const records = wrapper.findComponent(AuditRecordTableStub).props('records') as OssAuditRecordDto[]
      expect(records.map((r) => r.id)).toEqual([7])
      expect(records[0].status).toBe(0)
      // Only the initial onMounted load: the failure path must not reload the list.
      expect(getRecords).toHaveBeenCalledTimes(1)
      wrapper.unmount()
    },
  )

  it('确认框取消：不发请求、无 toast', async () => {
    const record = makeRecord(3)
    getRecords.mockResolvedValueOnce({
      items: [record],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      statusCounts: { 0: 1 },
      bucketCounts: { uploads: 1 },
    })
    confirmDangerMock.mockResolvedValue(false)

    const wrapper = await mountView()
    wrapper.findComponent(AuditRecordTableStub).vm.$emit('resolve', record)
    await flushPromises()

    expect(confirmDangerMock).toHaveBeenCalledTimes(1)
    expect(resolveRecord).not.toHaveBeenCalled()
    expect(messageError).not.toHaveBeenCalled()
    expect(messageSuccess).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})

describe('OssAuditView handleBatchResolve（#24 批量删除表）', () => {
  function primeSelection(wrapper: VueWrapper, records: OssAuditRecordDto[]) {
    const table = wrapper.findComponent(AuditRecordTableStub)
    records.forEach((record) => table.vm.$emit('toggle', record))
    return table
  }

  function primeList(records: OssAuditRecordDto[]) {
    getRecords.mockResolvedValue({
      items: records,
      totalCount: records.length,
      page: 1,
      pageSize: 20,
      statusCounts: { 0: records.length },
      bucketCounts: { uploads: records.length },
    })
  }

  it('勾选 N 条后确认：确认框文案为「删除 N 个文件」，请求体为确认前快照；刷新/翻页/筛选不改变 ids', async () => {
    const selected = [makeRecord(1), makeRecord(2), makeRecord(3)]
    primeList([...selected, makeRecord(4)])

    let releaseConfirm!: (value: boolean) => void
    confirmDangerMock.mockReturnValue(new Promise<boolean>((resolve) => (releaseConfirm = resolve)))

    const wrapper = await mountView()
    primeSelection(wrapper, selected)
    wrapper.findComponent(AuditBulkBarStub).vm.$emit('resolve')

    await flushPromises()
    expect(confirmDangerMock).toHaveBeenCalledWith(
      expect.objectContaining({ confirmText: '删除 3 个文件', title: '批量删除存储文件' }),
    )

    // While the confirm dialog is open, the user pages/filters/refreshes: the live
    // selection may grow or be cleared entirely (loadRecords always clears it).
    // Neither may change the snapshot that was confirmed for deletion.
    wrapper.findComponent(AuditRecordTableStub).vm.$emit('toggle', makeRecord(4))
    await (wrapper.vm as any).loadRecords()
    await flushPromises()

    releaseConfirm(true)
    await flushPromises()

    expect(batchResolve).toHaveBeenCalledTimes(1)
    expect(batchResolve).toHaveBeenCalledWith([1, 2, 3])
    wrapper.unmount()
  })

  it('batchLoading 期间防重复提交：快速重复点击只发一次请求', async () => {
    primeList([makeRecord(1), makeRecord(2)])
    let releaseBatch!: (value: { resolvedCount: number; errors: string[]; totalRequested: number }) => void
    batchResolve.mockReturnValue(
      new Promise((resolve) => {
        releaseBatch = resolve
      }),
    )

    const wrapper = await mountView()
    primeSelection(wrapper, [makeRecord(1), makeRecord(2)])
    const bulkBar = wrapper.findComponent(AuditBulkBarStub)
    bulkBar.vm.$emit('resolve')
    await flushPromises()
    // Second click while the first request is in flight.
    bulkBar.vm.$emit('resolve')
    await flushPromises()

    expect(batchResolve).toHaveBeenCalledTimes(1)
    expect(bulkBar.props('loading')).toBe(true)

    releaseBatch({ resolvedCount: 2, errors: [], totalRequested: 2 })
    await flushPromises()
    expect(bulkBar.props('loading')).toBe(false)
    wrapper.unmount()
  })

  it('未勾选即点批量删除：前端提前 warning，不发请求也不弹确认框', async () => {
    primeList([makeRecord(1)])
    const wrapper = await mountView()

    wrapper.findComponent(AuditBulkBarStub).vm.$emit('resolve')
    await flushPromises()

    expect(messageWarning).toHaveBeenCalledWith('请选择要删除的记录')
    expect(confirmDangerMock).not.toHaveBeenCalled()
    expect(batchResolve).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('批量部分失败：成功 toast + 只提示失败计数，不渲染逐条 errors 原因', async () => {
    primeList([makeRecord(1), makeRecord(2), makeRecord(3)])
    batchResolve.mockResolvedValue({
      resolvedCount: 1,
      errors: [
        'uploads/regression/record-2.jpg: 被上传记录引用，跳过',
        'uploads/regression/record-3.jpg: 被错题记录引用，跳过',
      ],
      totalRequested: 3,
    })

    const wrapper = await mountView()
    primeSelection(wrapper, [makeRecord(1), makeRecord(2), makeRecord(3)])
    wrapper.findComponent(AuditBulkBarStub).vm.$emit('resolve')
    await flushPromises()

    expect(messageSuccess).toHaveBeenCalledWith('成功删除 1 条记录')
    expect(messageWarning).toHaveBeenCalledWith('有 2 条记录删除失败')
    const allMessages = [...messageSuccess.mock.calls, ...messageWarning.mock.calls, ...messageError.mock.calls]
      .map((call) => String(call[0]))
    expect(allMessages.some((text) => text.includes('被上传记录引用'))).toBe(false)
    expect(allMessages.some((text) => text.includes('被错题记录引用'))).toBe(false)
    wrapper.unmount()
  })

  it('批量全部失败：只有 warning「有 N 条记录删除失败」，无成功 toast', async () => {
    primeList([makeRecord(1), makeRecord(2)])
    batchResolve.mockResolvedValue({
      resolvedCount: 0,
      errors: ['a: 被上传记录引用，跳过', 'b: 被上传记录引用，跳过'],
      totalRequested: 2,
    })

    const wrapper = await mountView()
    primeSelection(wrapper, [makeRecord(1), makeRecord(2)])
    wrapper.findComponent(AuditBulkBarStub).vm.$emit('resolve')
    await flushPromises()

    expect(messageSuccess).not.toHaveBeenCalled()
    expect(messageWarning).toHaveBeenCalledWith('有 2 条记录删除失败')
    wrapper.unmount()
  })
})
