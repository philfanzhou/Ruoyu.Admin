import { describe, expect, it, vi } from 'vitest'
import { freezeOperation, hasManagedMetadata, sendFixedOperation } from './managedAssignment'
import type { AssignmentChoice, FixedOperation } from './managedAssignment'
import type { UploadRecordDto } from '../../services/studentAdminApi'

const source = '11111111-1111-4111-8111-111111111111'
const revision = '22222222-2222-4222-8222-222222222222'
const student = '33333333-3333-4333-8333-333333333333'
const first = '44444444-4444-4444-8444-444444444444'
const second = '55555555-5555-4555-8555-555555555555'
const item1 = '66666666-6666-4666-8666-666666666666'
const item2 = '77777777-7777-4777-8777-777777777777'
const record = (): UploadRecordDto => ({ id: source, studentId: student, contentRevision: revision,
  assignmentProtocol: 'managed-v1', imageEntries: [
    { path: 'uploads/题目/A.PNG', type: 'mistake' }, { path: 'uploads/题目/a.PNG', type: 'mistake' },
    { path: 'uploads/题目/A.PNG', type: 'mistake' }, { path: 'uploads/homework.PNG', type: 'homework' },
  ], imagePaths: ['uploads/题目/A.PNG', 'uploads/题目/a.PNG', 'uploads/题目/A.PNG', 'uploads/homework.PNG'],
  studentName: 'owned student', status: 1, imageRotations: [0, 0, 0, 0], comments: '', createdAt: 1, updatedAt: 2 })
const choice = (indices = [0, 1, 2]): AssignmentChoice => ({ imageIndices: indices, subject: 2, grade: 7, comments: 'fixed cause' })
function operation(choices = [choice()]): FixedOperation {
  let index = 0
  return freezeOperation(record(), choices, () => [first, second][index++])
}
function complete(op: FixedOperation, ids = [item1, item2]) {
  return { success: true, groups: op.results.map((result, index) => ({ requestKey: result.requestKey,
    state: 'Completed', errorKind: '', createdItemIds: [ids[index]], statusCode: 200 })) }
}

describe('fixed managed assignment', () => {
  it('retains Unicode/case and de-duplicates only identical paths without reinterpreting edited current entries', async () => {
    const current = record()
    const op = freezeOperation(current, [choice()], () => first)
    current.imageEntries!.reverse(); current.contentRevision = second; current.studentId = item2
    const send = vi.fn().mockResolvedValue(complete(op))
    expect(await sendFixedOperation(op, send)).toBe(true)
    expect(send).toHaveBeenCalledWith(source, expect.objectContaining({ studentId: student,
      expectedContentRevision: revision, assignments: [{ requestKey: first, sourcePaths: ['uploads/题目/A.PNG', 'uploads/题目/a.PNG'],
        subject: 2, grade: 7, comments: 'fixed cause' }] }), undefined)
    expect(Object.isFrozen(op.payload.assignments[0].sourcePaths)).toBe(true)
  })

  it('allows two distinct groups sharing the same original image and retains both actual result IDs', async () => {
    const op = operation([choice([0]), choice([0, 1])])
    expect(await sendFixedOperation(op, vi.fn().mockResolvedValue(complete(op)))).toBe(true)
    expect(op.results.map(group => group.createdItemIds)).toEqual([[item1], [item2]])
    expect(op.payload.assignments.map(group => group.sourcePaths)).toEqual([
      ['uploads/题目/A.PNG'], ['uploads/题目/A.PNG', 'uploads/题目/a.PNG'],
    ])
  })

  it.each([undefined, 'unknown', 'legacy'])('never guesses managed protocol from metadata (%s)', protocol => {
    const value = record(); value.assignmentProtocol = protocol
    expect(hasManagedMetadata(value)).toBe(false)
    expect(() => freezeOperation(value, [choice()], () => first)).toThrow()
  })

  it.each([[3], [-1], [4], [], [0.5]].map(indices => [indices]))('rejects nonmistake or invalid positions %j before creating a request', indices => {
    expect(() => freezeOperation(record(), [choice(indices)], () => first)).toThrow()
  })

  it('does not trust missing revision, malformed entries, invalid subject/grade or duplicate request keys', () => {
    const missing = record(); delete missing.contentRevision
    expect(() => freezeOperation(missing, [choice()], () => first)).toThrow()
    const noType = record(); noType.imageEntries![0].type = ''
    expect(hasManagedMetadata(noType)).toBe(false)
    expect(() => operation([{ ...choice(), subject: 10 }])).toThrow()
    expect(() => operation([{ ...choice(), grade: 13 }])).toThrow()
    expect(() => freezeOperation(record(), [choice([0]), choice([1])], () => first)).toThrow()
  })

  it('double confirmation sends once while the actual first request is in progress', async () => {
    const op = operation(); let resolve!: (value: unknown) => void
    const send = vi.fn(() => new Promise(done => { resolve = done }))
    const pending = sendFixedOperation(op, send)
    expect(await sendFixedOperation(op, send)).toBe(false); expect(send).toHaveBeenCalledTimes(1)
    resolve(complete(op)); expect(await pending).toBe(true)
  })

  it('retains Completed/Failed/NotAttempted and exact 409 without reporting batch success', async () => {
    const op = operation([choice([0]), choice([1])])
    const response = { success: false, groups: [
      { requestKey: first, state: 'Completed', errorKind: '', createdItemIds: [item1], statusCode: 200 },
      { requestKey: second, state: 'Failed', errorKind: 'request_payload_conflict', createdItemIds: [], statusCode: 409 },
    ] }
    expect(await sendFixedOperation(op, vi.fn().mockResolvedValue(response))).toBe(false)
    expect(op.results.map(result => result.state)).toEqual(['Completed', 'Failed'])
    expect(op.results[1].statusCode).toBe(409)
  })

  it('retries an unknown committed group with the identical payload/key and retains known IDs', async () => {
    const op = operation([choice([0]), choice([1])]); const body = JSON.stringify(op.payload)
    const send = vi.fn().mockResolvedValueOnce({ success: false, groups: [
      { requestKey: first, state: 'Completed', errorKind: '', createdItemIds: [item1] },
      { requestKey: second, state: 'Unknown', errorKind: 'transport_unknown', createdItemIds: [] },
    ] }).mockResolvedValueOnce(complete(op))
    expect(await sendFixedOperation(op, send)).toBe(false)
    op.record.contentRevision = item2; op.record.imageEntries = []
    expect(await sendFixedOperation(op, send)).toBe(true)
    expect(JSON.stringify(send.mock.calls[0][1])).toBe(body)
    expect(JSON.stringify(send.mock.calls[1][1])).toBe(body)
    expect(op.results[0].createdItemIds).toEqual([item1])
  })

  it.each([{}, { success: true }, { success: true, groups: [] }, { success: true, groups: [{ requestKey: first,
    state: 'Completed', createdItemIds: [], errorKind: '' }] }, { success: true, groups: [{ requestKey: first,
    state: 'Completed', createdItemIds: [item1, item1], errorKind: '' }] }])('malformed or empty completion is unknown with the original key', async response => {
    const op = operation()
    expect(await sendFixedOperation(op, vi.fn().mockResolvedValue(response))).toBe(false)
    expect(op.results[0].state).toBe('Unknown'); expect(op.payload.assignments[0].requestKey).toBe(first)
  })

  it('rejects a changed replay ID instead of erasing the prior Completed fact', async () => {
    const op = operation([choice([0]), choice([1])])
    op.results[0] = { requestKey: first, state: 'Completed', errorKind: '', createdItemIds: [item1] }
    expect(await sendFixedOperation(op, vi.fn().mockResolvedValue(complete(op, [student, item2])))).toBe(false)
    expect(op.results[0].createdItemIds).toEqual([item1]); expect(op.results[1].state).toBe('Unknown')
  })

  it('cancellation forwards the signal and retains a recoverable unknown request', async () => {
    const op = operation(); const controller = new AbortController()
    const send = vi.fn(async (_source, _payload, signal) => { expect(signal).toBe(controller.signal); controller.abort(); throw new Error('cancelled') })
    expect(await sendFixedOperation(op, send, controller.signal)).toBe(false)
    expect(op.results[0].state).toBe('Unknown'); expect(op.results[0].errorKind).toBe('cancelled_unknown')
    expect(send).toHaveBeenCalledTimes(1)
  })
})
