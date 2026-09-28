import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ElMessageBox } from 'element-plus'
import { confirmDanger } from './confirm'

/**
 * 单条/批量删除等破坏性操作的二次确认门（#24 语义模型：确认框点遮罩不发请求、
 * Enter 不触发删除、Esc/取消不发请求）。此处直接锁定 confirmDanger 透传给
 * ElMessageBox.confirm 的选项；组件级用例只驱动确认/取消两条路径。
 */
describe('confirmDanger', () => {
  let confirmSpy: ReturnType<typeof vi.fn>

  beforeEach(() => {
    confirmSpy = vi.fn()
    vi.spyOn(ElMessageBox, 'confirm').mockImplementation(confirmSpy as never)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  const opts = { title: '删除存储文件', message: 'x', confirmText: '删除' }

  it('passes the non-dismissable dialog options: no mask close, no autofocus, Esc closes', async () => {
    confirmSpy.mockResolvedValue(undefined as never)
    await confirmDanger(opts)

    expect(confirmSpy).toHaveBeenCalledTimes(1)
    const [, , options] = confirmSpy.mock.calls[0]
    expect(options.closeOnClickModal).toBe(false)
    expect(options.autofocus).toBe(false)
    expect(options.closeOnPressEscape).toBe(true)
    expect(options.type).toBe('error')
    expect(options.confirmButtonType).toBe('danger')
    expect(options.confirmButtonText).toBe('删除')
  })

  it('resolves true only when the danger button is clicked', async () => {
    confirmSpy.mockResolvedValue(undefined as never)
    await expect(confirmDanger(opts)).resolves.toBe(true)
  })

  it.each([
    ['cancel button', (spy: ReturnType<typeof vi.fn>) => spy.mockRejectedValue('cancel' as never)],
    ['Esc / close', (spy: ReturnType<typeof vi.fn>) => spy.mockRejectedValue('close' as never)],
    ['unexpected rejection', (spy: ReturnType<typeof vi.fn>) => spy.mockRejectedValue(new Error('boom') as never)],
  ])('resolves false (and never throws) on %s without any caller-visible error', async (_name, rejectWith) => {
    rejectWith(confirmSpy)
    await expect(confirmDanger(opts)).resolves.toBe(false)
  })
})
