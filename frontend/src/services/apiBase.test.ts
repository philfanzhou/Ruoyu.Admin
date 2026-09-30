import { AxiosHeaders, AxiosError } from 'axios'
import { describe, expect, it } from 'vitest'
import { extractApiErrorMessage } from './apiBase'

/**
 * #56: the backend answers unhandled exceptions with application/problem+json while business
 * errors keep their existing {message} bodies. extractApiErrorMessage is the single point that
 * makes both shapes surface as a readable message for every toast in the app; these tests lock
 * the priority and the guard rails (detail never read, problem title only for problem+json).
 */
function axiosError(status: number, contentType: string | undefined, data: unknown): AxiosError {
  const headers = new AxiosHeaders(
    contentType === undefined ? undefined : { 'content-type': contentType })
  return new AxiosError('transport failure', 'ERR_BAD_RESPONSE', undefined, undefined, {
    status,
    statusText: 'status',
    headers,
    config: { headers },
    data,
  } as never)
}

describe('extractApiErrorMessage', () => {
  it('prefers the business message of a legacy JSON error body', () => {
    const error = axiosError(400, 'application/json', { message: 'Name is required.' })
    expect(extractApiErrorMessage(error)).toBe('Name is required.')
  })

  it('reads the fixed title of a problem+json error body', () => {
    const error = axiosError(502, 'application/problem+json', {
      type: 'urn:servicemantle:error:downstream.unavailable',
      title: 'A downstream service request failed.',
      status: 502,
      correlationId: '0123456789abcdef0123456789abcdef',
      errorCode: 'downstream.unavailable',
    })
    expect(extractApiErrorMessage(error)).toBe('A downstream service request failed.')
  })

  it('never renders the problem detail or raw JSON', () => {
    const error = axiosError(500, 'application/problem+json', {
      title: 'An unexpected error occurred.',
      detail: 'secret-internal-detail',
    })
    const message = extractApiErrorMessage(error)
    expect(message).toBe('An unexpected error occurred.')
    expect(message).not.toContain('secret-internal-detail')
  })

  it('ignores a title when the content type is not problem+json', () => {
    const error = axiosError(400, 'application/json', { title: 'not a problem title', message: 'business text' })
    expect(extractApiErrorMessage(error)).toBe('business text')
  })

  it('falls back to the axios transport message without a response body', () => {
    const error = new AxiosError('Network Error')
    expect(extractApiErrorMessage(error)).toBe('Network Error')
  })

  it('keeps plain Error and unknown inputs readable', () => {
    expect(extractApiErrorMessage(new Error('boom'))).toBe('boom')
    expect(extractApiErrorMessage('not an error')).toBe('Unknown error occurred.')
  })
})
