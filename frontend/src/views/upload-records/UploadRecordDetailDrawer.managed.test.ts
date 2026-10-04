import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('../../services/studentAdminApi', () => ({ studentAdminClient: {
  assignManagedUploadRecord: vi.fn(), removeImageFromRecord: vi.fn(), rotateUploadImage: vi.fn(), assignUploadRecord: vi.fn(),
}, getErrorMessage: () => '请求失败' }))
vi.mock('../../utils/confirm', () => ({ confirmDanger: vi.fn() }))
import Drawer from './UploadRecordDetailDrawer.vue'
import { studentAdminClient } from '../../services/studentAdminApi'

const api = vi.mocked(studentAdminClient.assignManagedUploadRecord)
const uuid = (n: number) => `10000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const record = (n: number) => ({ id: uuid(n), studentId: uuid(20), studentName: '学生', contentRevision: uuid(30 + n),
  assignmentProtocol: 'managed-v1' as const, imageEntries: [{ path: `uploads/${n}/题目.PNG`, type: 'mistake' },
    { path: `uploads/${n}/answer.PNG`, type: 'answer' }], imagePaths: [`uploads/${n}/题目.PNG`, `uploads/${n}/answer.PNG`],
  status: 1, comments: '', createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z' })
const Gallery = { props: ['selectionDisabled', 'mutationsDisabled', 'assignableIndices'], emits: ['delete', 'rotate'], template: '<div />' }
const Form = { props: ['disabled', 'subject', 'grade', 'comments'], emits: ['update:subject', 'update:grade'], template: '<div />' }
function create() {
  return mount(Drawer, { props: { record: record(1), initialGrade: 7, studentGrade: 7, gradeOptions: [], subjectOptions: [] },
    global: { plugins: [ElementPlus], stubs: { 'el-drawer': { template: '<div><slot name="header"/><slot/><slot name="footer"/></div>' },
      UploadImageGallery: Gallery, AssignForm: Form, VlAnalysisCard: true, VlResultDialog: true, ResetStatusDialog: true } } })
}
function button(wrapper: ReturnType<typeof create>, text: string) { return wrapper.findAll('button').find(b => b.text() === text)! }
async function choose(wrapper: ReturnType<typeof create>) { wrapper.findComponent(Form).vm.$emit('update:subject', 2); await flushPromises() }
function completed(payload: any, id = uuid(60)) { return { success: true, groups: payload.assignments.map((g: any) => ({
  requestKey: g.requestKey, state: 'Completed', errorKind: '', createdItemIds: [id], statusCode: 200 })) } }
beforeEach(() => vi.clearAllMocks())
afterEach(() => vi.restoreAllMocks())

describe('managed drawer fixed requests', () => {
  it('double click sends once, locks mutations and ignores late completion after another row opens', async () => {
    let finish!: (value: any) => void
    api.mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const w = create(); await choose(w)
    await button(w, '确认指派').trigger('click'); await button(w, '指派中...').trigger('click')
    expect(api).toHaveBeenCalledTimes(1)
    expect(w.findComponent(Gallery).props('mutationsDisabled')).toBe(true)
    w.findComponent(Gallery).vm.$emit('delete', 0); await flushPromises()
    expect(studentAdminClient.removeImageFromRecord).not.toHaveBeenCalled()
    const payload = api.mock.calls[0][1]
    expect(payload.assignments[0].sourcePaths).toEqual(['uploads/1/题目.PNG'])
    await w.setProps({ record: record(2) }); finish(completed(payload)); await flushPromises()
    expect(w.text()).toContain(uuid(2)); expect(w.emitted('close')).toBeUndefined(); expect(w.emitted('changed')).toBeUndefined()
    w.unmount()
  })

  it('unknown result survives close, source revision changes and recovery sends the exact original body', async () => {
    api.mockRejectedValueOnce(new Error('connection lost'))
    const w = create(); await choose(w); await button(w, '确认指派').trigger('click'); await flushPromises()
    const original = api.mock.calls[0][1]
    expect(w.text()).toContain('结果未知')
    await button(w, '关闭').trigger('click'); await w.setProps({ record: null }); await flushPromises()
    await button(w, `恢复 ${uuid(1)}`).trigger('click'); await flushPromises()
    await w.setProps({ record: { ...record(1), contentRevision: uuid(99), imageEntries: [], imagePaths: [] } }); await flushPromises()
    api.mockImplementationOnce(async (_, payload) => completed(payload))
    await button(w, '重新发送原请求').trigger('click'); await flushPromises()
    expect(api.mock.calls[1][1]).toEqual(original); expect(w.emitted('changed')).toHaveLength(1)
    w.unmount()
  })

  it('cancel targets the visible operation when two different records are in flight', async () => {
    api.mockImplementation((_, _payload, signal) => new Promise((_resolve, reject) => signal!.addEventListener('abort', () => reject(new Error('cancelled')))))
    const w = create(); await choose(w); await button(w, '确认指派').trigger('click')
    await w.setProps({ record: record(2) }); await choose(w); await button(w, '确认指派').trigger('click')
    const firstSignal = api.mock.calls[0][2]!, secondSignal = api.mock.calls[1][2]!
    await w.setProps({ record: record(1) }); await button(w, '取消等待（结果可能已提交）').trigger('click'); await flushPromises()
    expect(firstSignal.aborted).toBe(true); expect(secondSignal.aborted).toBe(false)
    expect(w.text()).toContain('结果未知'); w.unmount()
  })

  it('missing or unknown protocol cannot assign or silently call the legacy endpoint', async () => {
    const w = create(); await w.setProps({ record: { ...record(1), assignmentProtocol: 'future-v2' } as never }); await choose(w)
    expect(button(w, '确认指派').attributes('disabled')).toBeDefined()
    await button(w, '确认指派').trigger('click'); await flushPromises()
    expect(api).not.toHaveBeenCalled(); expect(studentAdminClient.assignUploadRecord).not.toHaveBeenCalled(); w.unmount()
  })
})
