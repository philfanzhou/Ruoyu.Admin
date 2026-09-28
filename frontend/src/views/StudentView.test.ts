import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('../services/studentAdminApi', () => ({
  default: {
    getStudents: vi.fn(),
    getGrades: vi.fn(),
    getSubjectOptions: vi.fn(),
    getStudentOpenSubjects: vi.fn(),
    getIdentityAccountsByStudentId: vi.fn(),
    getIdentityAccountsBatch: vi.fn(),
    linkIdentityAccount: vi.fn(),
    unlinkIdentityAccount: vi.fn(),
  },
}))

vi.mock('../services/identityApi', () => ({
  default: {
    getUsers: vi.fn(),
  },
}))

vi.mock('../utils/confirm', () => ({
  confirmDanger: vi.fn(),
}))

import StudentView from './StudentView.vue'
import studentAdminApi from '../services/studentAdminApi'
import identityApi from '../services/identityApi'
import { confirmDanger } from '../utils/confirm'

/**
 * 学生关联/解除账号的确认流程（#25 范围表末行）：解除关联必须过危险确认；
 * 添加关联无确认（既有行为）；accountId 缺失时早退，不得静默提交。
 */

const student = {
  id: 'stu-1',
  name: '张三',
  grade: 7,
  identityAccountIds: ['u1'],
  createdAt: '2026-09-01',
}

async function mountView(): Promise<VueWrapper> {
  const wrapper = mount(StudentView, {
    global: {
      plugins: [ElementPlus],
      stubs: {
        PageHeader: { template: '<div><slot name="actions" /></div>' },
        FilterBar: { template: '<div><slot /></div>', emits: ['search', 'reset'] },
        DataTableCard: { template: '<div />' },
        ListPagination: { template: '<div />' },
        SubjectTag: { template: '<span />' },
        // Pass-through so the linked-accounts dialog content renders in jsdom.
        'el-dialog': {
          template: '<div class="dialog-stub"><slot /><slot name="footer" /></div>',
        },
      },
    },
  })
  await flushPromises()
  return wrapper
}

const getStudents = vi.mocked(studentAdminApi.getStudents)
const getGrades = vi.mocked(studentAdminApi.getGrades)
const getSubjectOptions = vi.mocked(studentAdminApi.getSubjectOptions)
const getStudentOpenSubjects = vi.mocked(studentAdminApi.getStudentOpenSubjects)
const getIdentityAccountsByStudentId = vi.mocked(studentAdminApi.getIdentityAccountsByStudentId)
const getIdentityAccountsBatch = vi.mocked(studentAdminApi.getIdentityAccountsBatch)
const linkIdentityAccount = vi.mocked(studentAdminApi.linkIdentityAccount)
const unlinkIdentityAccount = vi.mocked(studentAdminApi.unlinkIdentityAccount)
const getUsers = vi.mocked(identityApi.getUsers)
const confirmDangerMock = vi.mocked(confirmDanger)

beforeEach(() => {
  vi.clearAllMocks()
  confirmDangerMock.mockResolvedValue(true)
  linkIdentityAccount.mockResolvedValue(undefined as never)
  unlinkIdentityAccount.mockResolvedValue(undefined as never)
  getStudents.mockResolvedValue({ items: [student], total: 1 } as never)
  getGrades.mockResolvedValue([] as never)
  getSubjectOptions.mockResolvedValue([] as never)
  getStudentOpenSubjects.mockResolvedValue([] as never)
  getIdentityAccountsByStudentId.mockResolvedValue(['u1'] as never)
  getIdentityAccountsBatch.mockResolvedValue({
    accounts: [{ userId: 'u1', username: 'alice', displayName: 'Alice' }],
  } as never)
  getUsers.mockResolvedValue({ items: [] } as never)
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('StudentView 关联/解除账号', () => {
  it('解除关联必须经过危险确认；取消时不发解除请求', async () => {
    confirmDangerMock.mockResolvedValue(false)
    const wrapper = await mountView()

    await (wrapper.vm as never as { openLinkedAccountsDialog: (s: typeof student) => Promise<void> })
      .openLinkedAccountsDialog(student)
    await flushPromises()

    const unlinkButton = wrapper.find('[title="移除关联"]')
    expect(unlinkButton.exists()).toBe(true)
    await unlinkButton.trigger('click')
    await flushPromises()

    expect(confirmDangerMock).toHaveBeenCalledTimes(1)
    expect(confirmDangerMock).toHaveBeenCalledWith(
      expect.objectContaining({ title: '解除账户关联', confirmText: '解除关联' }),
    )
    expect(unlinkIdentityAccount).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('确认解除后调用解除接口并刷新关联账户', async () => {
    const wrapper = await mountView()

    await (wrapper.vm as never as { openLinkedAccountsDialog: (s: typeof student) => Promise<void> })
      .openLinkedAccountsDialog(student)
    await flushPromises()

    await wrapper.find('[title="移除关联"]').trigger('click')
    await flushPromises()

    expect(unlinkIdentityAccount).toHaveBeenCalledWith('stu-1', 'u1')
    // refreshLinkedAccounts + loadStudents run after a successful unlink.
    expect(getIdentityAccountsByStudentId.mock.calls.length).toBeGreaterThanOrEqual(2)
    wrapper.unmount()
  })

  it('添加关联无危险确认（既有行为，锁定现状），直接调用关联接口', async () => {
    const wrapper = await mountView()

    await (wrapper.vm as never as { openLinkedAccountsDialog: (s: typeof student) => Promise<void> })
      .openLinkedAccountsDialog(student)
    // Simulate a completed account search (the search input flow itself is not the invariant).
    ;(wrapper.vm as never as { accountSearchResults: { userId: string; username: string }[] }).accountSearchResults = [
      { userId: 'u2', username: 'bob' },
    ]
    await flushPromises()

    const addButton = wrapper.find('[title="添加关联"]')
    expect(addButton.exists()).toBe(true)
    await addButton.trigger('click')
    await flushPromises()

    expect(confirmDangerMock).not.toHaveBeenCalled()
    expect(linkIdentityAccount).toHaveBeenCalledWith('stu-1', 'u2')
    wrapper.unmount()
  })

  it('accountId 缺失时早退：不弹确认也不发任何请求（不得静默提交）', async () => {
    const wrapper = await mountView()

    await (wrapper.vm as never as { runLinkAccount: (id: string, action: string) => Promise<void> })
      .runLinkAccount('', 'link')
    await flushPromises()
    await (wrapper.vm as never as { runLinkAccount: (id: string, action: string) => Promise<void> })
      .runLinkAccount('', 'unlink')
    await flushPromises()

    expect(confirmDangerMock).not.toHaveBeenCalled()
    expect(linkIdentityAccount).not.toHaveBeenCalled()
    expect(unlinkIdentityAccount).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
