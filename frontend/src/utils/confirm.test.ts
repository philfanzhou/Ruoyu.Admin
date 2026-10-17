import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ElMessageBox } from 'element-plus'
import { confirmDanger } from './confirm'

/**
 * The danger-confirmation gate for destructive operations such as single or
 * batch delete (issue #24 semantic model: clicking the dialog mask sends no
 * request, Enter does not trigger deletion, Esc/cancel sends no request).
 * This file checks the options that confirmDanger passes through to
 * ElMessageBox.confirm as-is; component-level cases drive only the
 * confirm/cancel paths.
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
