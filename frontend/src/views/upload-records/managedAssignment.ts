import type { ManagedAssignRequest, ManagedSourceRegion, UploadRecordDto } from '../../services/studentAdminApi'

export type GroupState = 'NotAttempted' | 'Completed' | 'Failed' | 'Unknown'
export interface GroupResult { requestKey: string; state: GroupState; createdItemIds: string[]; errorKind: string; statusCode?: number | null }
/** Crop rectangle draft; box null means the full source image. */
export interface RegionChoice { sourceImagePath: string; box: { x1: number; y1: number; x2: number; y2: number } | null }
export interface AssignmentChoice { imageIndices: number[]; regions?: RegionChoice[]; subject: number; grade: number; comments: string }
export interface FixedOperation {
  record: UploadRecordDto
  payload: ManagedAssignRequest
  results: GroupResult[]
  running: boolean
  acknowledged: boolean
}

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export const isId = (value: unknown): value is string => typeof value === 'string' && uuid.test(value)
  && value.toLowerCase() !== '00000000-0000-0000-0000-000000000000'
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)

export function hasManagedMetadata(record: UploadRecordDto): boolean {
  return record.assignmentProtocol === 'managed-v1' && isId(record.id) && isId(record.studentId)
    && isId(record.contentRevision) && Array.isArray(record.imageEntries) && record.imageEntries.every(e =>
      object(e) && typeof e.path === 'string' && e.path.length > 0 && typeof e.type === 'string' && e.type.length > 0)
}

const regionCanonical = (region: RegionChoice) => region.sourceImagePath + '\u0000'
  + (region.box ? [region.box.x1, region.box.y1, region.box.x2, region.box.y2].join(',') : 'Full')
const isValidRegion = (region: RegionChoice) => region.sourceImagePath.length > 0 && region.sourceImagePath.length <= 4096
  && (region.box === null || Number.isInteger(region.box.x1) && Number.isInteger(region.box.y1)
    && Number.isInteger(region.box.x2) && Number.isInteger(region.box.y2)
    && region.box.x1 >= 0 && region.box.y1 >= 0 && region.box.x1 < region.box.x2 && region.box.y1 < region.box.y2)

export function freezeOperation(record: UploadRecordDto, choices: AssignmentChoice[], newKey = () => crypto.randomUUID()): FixedOperation {
  if (!hasManagedMetadata(record) || choices.length < 1 || choices.length > 100) throw new Error('invalid_assignment')
  const entries = record.imageEntries!
  const assignments = choices.map(choice => {
    if (!Number.isInteger(choice.subject) || choice.subject < 1 || choice.subject > 9
      || !Number.isInteger(choice.grade) || choice.grade < 1 || choice.grade > 12 || choice.comments.length > 4096
      || choice.imageIndices.some(i => !Number.isInteger(i) || i < 0 || i >= entries.length || entries[i].type !== 'mistake'))
      throw new Error('invalid_assignment')
    // The upstream contract makes imagePaths and sourceRegions mutually exclusive:
    // a group is either whole images or explicit crop rectangles over shared originals.
    if (choice.regions !== undefined) {
      if (choice.imageIndices.length !== 0
        || choice.regions.length < 1 || choice.regions.length > 100 || !choice.regions.every(isValidRegion))
        throw new Error('invalid_assignment')
      const regions: ManagedSourceRegion[] = [...new Map(choice.regions.map(region =>
        [regionCanonical(region), { sourceImagePath: region.sourceImagePath, boundingBox: region.box }] as const))]
        .map(([, region]) => Object.freeze(region))
      const key = newKey()
      if (!isId(key)) throw new Error('invalid_request_key')
      return Object.freeze({ requestKey: key, sourceRegions: Object.freeze(regions) as unknown as ManagedSourceRegion[],
        subject: choice.subject, grade: choice.grade, comments: choice.comments })
    }
    if (choice.imageIndices.length < 1 || choice.imageIndices.length > 100) throw new Error('invalid_assignment')
    const paths = [...new Set(choice.imageIndices.map(i => entries[i].path))]
    if (paths.some(path => path.length > 4096)) throw new Error('invalid_assignment')
    const key = newKey()
    if (!isId(key)) throw new Error('invalid_request_key')
    return Object.freeze({ requestKey: key, sourcePaths: Object.freeze(paths) as unknown as string[],
      subject: choice.subject, grade: choice.grade, comments: choice.comments })
  })
  if (new Set(assignments.map(a => a.requestKey.toLowerCase())).size !== assignments.length) throw new Error('duplicate_request_key')
  const snapshot: UploadRecordDto = { ...record, imagePaths: [...record.imagePaths],
    imageEntries: entries.map(entry => ({ ...entry })), imageRotations: [...(record.imageRotations ?? [])] }
  const payload = Object.freeze({ mode: 'managed-v1' as const, studentId: record.studentId,
    expectedContentRevision: record.contentRevision!, assignments: Object.freeze(assignments) as unknown as ManagedAssignRequest['assignments'] })
  return { record: snapshot, payload, results: assignments.map(a => ({ requestKey: a.requestKey,
    state: 'NotAttempted', createdItemIds: [], errorKind: '' })), running: false, acknowledged: false }
}

function unknown(op: FixedOperation, kind: string, failed = false) {
  const index = op.results.findIndex(result => result.state !== 'Completed')
  if (index < 0) return
  op.results[index] = { requestKey: op.results[index].requestKey, state: failed ? 'Failed' : 'Unknown', createdItemIds: [], errorKind: kind }
  for (let i = index + 1; i < op.results.length; i++)
    if (op.results[i].state !== 'Completed') op.results[i] = { requestKey: op.results[i].requestKey,
      state: 'NotAttempted', createdItemIds: [], errorKind: '' }
}

function parseResult(value: unknown, op: FixedOperation): GroupResult[] | null {
  if (!object(value) || typeof value.success !== 'boolean' || !Array.isArray(value.groups)
    || value.groups.length !== op.results.length) return null
  const results: GroupResult[] = []
  let stopped = false
  for (let i = 0; i < value.groups.length; i++) {
    const group: unknown = value.groups[i]
    if (!object(group) || group.requestKey !== op.payload.assignments[i].requestKey
      || !['Completed', 'Failed', 'Unknown', 'NotAttempted'].includes(String(group.state))
      || typeof group.errorKind !== 'string' || !Array.isArray(group.createdItemIds)) return null
    const state = group.state as GroupState
    const ids = group.createdItemIds as unknown[]
    if (state === 'Completed' && (stopped || ids.length < 1 || ids.length > 100 || !ids.every(isId)
      || new Set(ids.map(id => String(id).toLowerCase())).size !== ids.length)) return null
    if (state !== 'Completed' && ids.length !== 0 || stopped && state !== 'NotAttempted'
      || !stopped && state === 'NotAttempted') return null
    if (state !== 'Completed') stopped = true
    results.push({ requestKey: String(group.requestKey), state, createdItemIds: ids as string[],
      // Never present an arbitrary upstream body or message in the UI.
      errorKind: /^[a-z_]{1,64}$/.test(group.errorKind) ? group.errorKind : 'provider_rejected',
      statusCode: typeof group.statusCode === 'number' && Number.isInteger(group.statusCode) ? group.statusCode : null })
  }
  return value.success === results.every(group => group.state === 'Completed') ? results : null
}

export async function sendFixedOperation(op: FixedOperation,
  send: (source: string, payload: ManagedAssignRequest, signal?: AbortSignal) => Promise<unknown>, signal?: AbortSignal): Promise<boolean> {
  if (op.running) return false
  op.running = true
  try {
    const value = await send(op.record.id, op.payload, signal)
    const results = parseResult(value, op)
    if (!results) { unknown(op, 'invalid_response'); return false }
    // A replay cannot replace already known completed IDs with unrelated IDs or erase that fact.
    for (let i = 0; i < results.length; i++) {
      const previous = op.results[i]
      if (previous.state === 'Completed' && (results[i].state !== 'Completed'
        || JSON.stringify(previous.createdItemIds.map(id => id.toLowerCase()).sort())
          !== JSON.stringify(results[i].createdItemIds.map(id => id.toLowerCase()).sort()))) {
        unknown(op, 'replay_result_conflict'); return false
      }
    }
    op.results = results
    return results.every(group => group.state === 'Completed')
  } catch (error) {
    const status = (error as { response?: { status?: number } }).response?.status
    unknown(op, signal?.aborted ? 'cancelled_unknown' : 'request_unknown', [400, 401, 403, 404, 409, 422].includes(status ?? 0))
    return false
  } finally { op.running = false }
}
