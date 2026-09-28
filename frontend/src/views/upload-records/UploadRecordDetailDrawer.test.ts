import { beforeEach, describe, expect, it, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ElementPlus from 'element-plus'

vi.mock('../../services/studentAdminApi', () => ({
  studentAdminClient: {
    removeImageFromRecord: vi.fn(),
    rotateUploadImage: vi.fn(),
    assignUploadRecord: vi.fn(),
  },
  getErrorMessage: (error: any) => error?.response?.data?.message ?? '请求失败',
}))

vi.mock('../../utils/confirm', () => ({
  confirmDanger: vi.fn(),
}))

import UploadRecordDetailDrawer from './UploadRecordDetailDrawer.vue'
import { studentAdminClient } from '../../services/studentAdminApi'
import { confirmDanger } from '../../utils/confirm'

/**
 * 上传记录删图的危险确认（#24 手工清单「上传记录：删图（危险确认）」）：
 * 删除必须先过 confirmDanger；取消路径不发任何请求。
 */

const record = {
  id: 'rec-1',
  studentId: 'stu-1',
  studentName: '学生一',
  status: 1,
  imagePaths: ['uploads/a.jpg', 'uploads/b.jpg'],
  imageRotations: [0, 0],
  comments: '',
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
} as never

const UploadImageGalleryStub = {
  props: ['imagePaths', 'rotations', 'assignIndices', 'selectedIndex'],
  emits: ['delete', 'toggle-assign', 'rotate', 'update:selectedIndex'],
  template: '<div class="gallery-stub" />',
}

function mountDrawer() {
  return mount(UploadRecordDetailDrawer, {
    props: {
      record: record as never,
      initialGrade: 7,
      studentGrade: 7,
      gradeOptions: [],
      subjectOptions: [],
    },
    global: {
      plugins: [ElementPlus],
      stubs: {
        'el-drawer': {
          template: '<div class="drawer-stub"><slot name="header" /><slot /><slot name="footer" /></div>',
        },
        UploadImageGallery: UploadImageGalleryStub,
        VlAnalysisCard: { template: '<div />' },
        VlResultDialog: { template: '<div />' },
        AssignForm: { template: '<div />' },
        ResetStatusDialog: { template: '<div />' },
      },
    },
  })
}

const removeImageFromRecord = vi.mocked(studentAdminClient.removeImageFromRecord)
const confirmDangerMock = vi.mocked(confirmDanger)

beforeEach(() => {
  vi.clearAllMocks()
  confirmDangerMock.mockResolvedValue(true)
  removeImageFromRecord.mockResolvedValue({ success: true } as never)
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('UploadRecordDetailDrawer 删图危险确认', () => {
  it('删除图片必须先经过危险确认（标题与文案），确认后才调用删除接口', async () => {
    const wrapper = mountDrawer()
    await flushPromises()

    wrapper.findComponent(UploadImageGalleryStub).vm.$emit('delete', 0)
    await flushPromises()

    expect(confirmDangerMock).toHaveBeenCalledTimes(1)
    expect(confirmDangerMock).toHaveBeenCalledWith(
      expect.objectContaining({ title: '删除图片', confirmText: '删除' }),
    )
    expect(removeImageFromRecord).toHaveBeenCalledWith('rec-1', 'stu-1', 0)
    wrapper.unmount()
  })

  it('取消确认：不发删除请求', async () => {
    confirmDangerMock.mockResolvedValue(false)
    const wrapper = mountDrawer()
    await flushPromises()

    wrapper.findComponent(UploadImageGalleryStub).vm.$emit('delete', 0)
    await flushPromises()

    expect(confirmDangerMock).toHaveBeenCalledTimes(1)
    expect(removeImageFromRecord).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
