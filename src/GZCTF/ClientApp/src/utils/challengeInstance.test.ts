import { describe, expect, it } from 'vitest'
import { instanceSecondsRemaining, formatRemaining, instanceConnection } from './challengeInstance'

describe('instance expiry', () => {
  it('reads the API epoch milliseconds and ISO dates, including after extension', () => {
    const now = Date.parse('2026-10-27T01:00:00Z')
    expect(instanceSecondsRemaining(now + 61000, now)).toBe(61)
    expect(instanceSecondsRemaining('2026-10-27T01:01:01Z', now)).toBe(61)
    expect(instanceSecondsRemaining(now + 121000, now)).toBe(121)
    expect(formatRemaining(3661)).toBe('01:01:01')
  })
  it('clamps expired dates and does not fabricate a lifetime for invalid data', () => {
    expect(instanceSecondsRemaining(1000, 2000)).toBe(0)
    expect(instanceSecondsRemaining(undefined, 2000)).toBeNull()
    expect(instanceSecondsRemaining('invalid', 2000)).toBeNull()
  })
})
describe('instance connection', () => {
  it('copies host and port and only offers Web links for Web challenges', () => {
    expect(instanceConnection('192.168.1.160', 32769, 'Web')).toEqual({
      address: '192.168.1.160:32769',
      href: 'http://192.168.1.160:32769/',
    })
    expect(instanceConnection('192.168.1.160', 32769, 'Pwn').href).toBeUndefined()
    expect(instanceConnection('https://lab.example', 443, 'Web').href).toBe('https://lab.example/')
    expect(instanceConnection('::1', 8080, 'Web').href).toBe('http://[::1]:8080/')
  })
  it('does not treat an opaque proxy container ID as a Web hostname', () => {
    const id = '01990000-0000-7000-8000-000000000003'
    expect(instanceConnection(id, null, 'Web')).toEqual({ address: id, href: undefined })
  })
  it('never turns unsafe or malformed entries into browser links', () => {
    for (const host of [
      'javascript:alert(1)',
      'https://user:pass@lab.example',
      '//evil.test',
      'bad host',
      'https://lab.example/path',
    ]) {
      expect(instanceConnection(host, 8080, 'Web').href).toBeUndefined()
    }
    expect(instanceConnection(undefined, 8080, 'Web').address).toBe('')
  })
})
