import { ref, onMounted, onUnmounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ossAuditApi, getOssAuditErrorMessage, type AuditStatusResponse } from '../../services/ossAuditApi'

/**
 * Scan status for the OSS audit page: loads it with the first records request,
 * polls it every 10 seconds (reloading records when a running scan completes),
 * and triggers a full scan after confirmation.
 */
export function useAuditStatus(loadRecords: () => Promise<void>) {
  const statusLoaded = ref(false)
  const auditLoading = ref(false)
  const auditStatus = ref<AuditStatusResponse>({
    isRunning: false,
    lastCompleted: null,
    lastFailed: null,
    pendingCount: 0
  })

  let pollTimer: ReturnType<typeof setInterval> | null = null
  let wasRunning = false

  async function loadAuditStatus(): Promise<void> {
    try {
      auditStatus.value = await ossAuditApi.getStatus()
      statusLoaded.value = true
    } catch {
      // silent
    }
  }

  async function handleTriggerAudit(): Promise<void> {
    if (auditStatus.value.isRunning) {
      ElMessage.warning('扫描正在进行中')
      return
    }
    try {
      await ElMessageBox.confirm(
        '确定要触发全量 OSS 扫描吗？这可能需要较长时间。',
        '确认',
        { type: 'warning' }
      )
    } catch {
      return
    }
    auditLoading.value = true
    try {
      const result = await ossAuditApi.triggerAudit()
      if (result.success) {
        ElMessage.success(result.message || '审计任务已触发')
        await Promise.all([loadAuditStatus(), loadRecords()])
        wasRunning = auditStatus.value.isRunning
      } else {
        ElMessage.error(result.message || '触发失败')
      }
    } catch (error) {
      ElMessage.error(getOssAuditErrorMessage(error))
    } finally {
      auditLoading.value = false
    }
  }

  onMounted(async () => {
    await Promise.all([loadRecords(), loadAuditStatus()])
    wasRunning = auditStatus.value.isRunning
    pollTimer = setInterval(async () => {
      await loadAuditStatus()
      if (wasRunning && !auditStatus.value.isRunning) {
        // Audit just completed, refresh records
        await loadRecords()
      }
      wasRunning = auditStatus.value.isRunning
    }, 10000)
  })

  onUnmounted(() => {
    if (pollTimer) {
      clearInterval(pollTimer)
      pollTimer = null
    }
  })

  return { auditStatus, statusLoaded, auditLoading, loadAuditStatus, handleTriggerAudit }
}
