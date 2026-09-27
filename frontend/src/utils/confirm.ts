import type { VNode } from 'vue'
import { ElMessageBox } from 'element-plus'

export interface ConfirmDangerOptions {
  title: string
  /**
   * Plain strings are rendered as text. Use `h()` for emphasis; never enable
   * `dangerouslyUseHTMLString` — names and paths come from backend data.
   */
  message: string | VNode
  confirmText: string
}

/**
 * Second confirmation for destructive actions (delete, revoke, unlink).
 * Resolves true only when the user clicks the danger button; cancel, close
 * and Esc resolve false. Never throws, so callers write
 * `if (!(await confirmDanger(...))) return` and keep API errors in their own try/catch.
 *
 * The confirm button is not focused on open, so pressing Enter cannot delete,
 * and clicking the mask does not close the dialog.
 */
export async function confirmDanger(opts: ConfirmDangerOptions): Promise<boolean> {
  try {
    await ElMessageBox.confirm(opts.message, opts.title, {
      type: 'error',
      confirmButtonType: 'danger',
      confirmButtonText: opts.confirmText,
      cancelButtonText: '取消',
      autofocus: false,
      closeOnClickModal: false,
      closeOnPressEscape: true,
    })
    return true
  } catch {
    return false
  }
}
