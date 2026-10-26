import { describe, expect, it } from 'vitest'
import { runtimeErrorKey } from './runtimeErrors'

describe('challenge runtime error messages', () => {
  it('recognizes structured server failures rather than the generic HTTP message', () => {
    expect(runtimeErrorKey({ response: { data: { code: 'challenge.runtime_unavailable' } }, message: 'Request failed' }))
      .toBe('translation:common.error.challenge_runtime_unavailable')
    expect(runtimeErrorKey({ code: 'challenge.container_start_failed' })).toBe('translation:common.error.challenge_container_start_failed')
    expect(runtimeErrorKey({ code: 'challenge.container_stop_failed' })).toBe('translation:common.error.challenge_container_stop_failed')
  })
  it('leaves unrelated and empty errors to the existing message handler', () => {
    for (const error of [null, undefined, 'failure', {}, { code: 'unknown' }]) expect(runtimeErrorKey(error)).toBeUndefined()
  })
})
