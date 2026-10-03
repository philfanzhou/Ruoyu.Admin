import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import AuditStatusPanel from './AuditStatusPanel.vue'
import type { AuditStatusResponse } from '../../services/ossAuditApi'

function status(proofs: unknown, version: string | null = 'storage-references-v1'): AuditStatusResponse {
  return { isRunning: false, pendingCount: 2, observationCount: 3, deletionAuthorized: false, lastFailed: null,
    lastCompleted: { id: 1, startedAt: 1, completedAt: 2, durationSeconds: 1, newZombieCount: 3,
      referenceContractVersion: version, referenceSnapshots: JSON.stringify(proofs), triggerType: 'manual' } }
}
const proofs = ['student', 'mistake', 'homework'].map((provider) => ({ provider, entryCount: 5, createdAt: '2026-10-03T06:00:00Z' }))

describe('AuditStatusPanel 保存的引用采集证据', () => {
  it('显示三方完整采集与只读观察，不将其描述为僵尸删除许可', () => {
    const wrapper = mount(AuditStatusPanel, { props: { status: status(proofs), ignoredCount: 0 }, global: { plugins: [ElementPlus] } })
    expect(wrapper.text()).toContain('未观察到引用的对象')
    for (const proof of proofs) expect(wrapper.text()).toContain(`${proof.provider}：5 条引用，采集于 ${proof.createdAt}`)
    expect(wrapper.text()).not.toContain('僵尸')
    wrapper.unmount()
  })
  it.each([null, proofs.slice(0, 2), [proofs[0], proofs[0], proofs[2]], [...proofs.slice(0, 2), { provider: 'other', entryCount: 0, createdAt: '' }]])(
    '不把损坏或不完整证据显示为三方成功', (evidence) => {
      const wrapper = mount(AuditStatusPanel, { props: { status: status(evidence), ignoredCount: 0 }, global: { plugins: [ElementPlus] } })
      expect(wrapper.find('.reference-proofs').exists()).toBe(false)
      wrapper.unmount()
    })
  it('历史扫描保留历史发现语义，不冒充v1采集', () => {
    const wrapper = mount(AuditStatusPanel, { props: { status: status(proofs, null), ignoredCount: 0 }, global: { plugins: [ElementPlus] } })
    expect(wrapper.text()).toContain('历史发现')
    expect(wrapper.find('.reference-proofs').exists()).toBe(false)
    wrapper.unmount()
  })
})
