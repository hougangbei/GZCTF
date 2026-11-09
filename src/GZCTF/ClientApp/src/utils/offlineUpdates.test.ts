import { describe, expect, it } from 'vitest'
import { applicablePackage, checksumValue, validPackageName, type OfflineStatus } from './offlineUpdates'

describe('offline package boundaries', () => {
  it('accepts exactly one checksum and never treats a multi-file digest list as one package', () => {
    expect(checksumValue('A'.repeat(64) + '  release.tar.gz\n')).toBe('a'.repeat(64))
    expect(checksumValue('b'.repeat(64))).toBe('b'.repeat(64))
    expect(checksumValue('a'.repeat(64) + '\n' + 'b'.repeat(64))).toBeUndefined()
    expect(checksumValue('md5:1234')).toBeUndefined()
  })
  it('rejects path names, control characters and non-package extensions', () => {
    for (const name of ['../release.zip', 'C:\\release.tar', 'bad\nname.zip', 'release.tar.gz.exe'])
      expect(validPackageName(name)).toBe(false)
    expect(validPackageName('更新包.tar.gz')).toBe(true)
  })
  it('requires the exact selected package and a finite future report expiry', () => {
    const status: OfflineStatus = {
      mode: 'offline',
      configured: true,
      phase: 'ready',
      validation: {
        id: 'a'.repeat(32),
        packageId: 'selected',
        sourceDigest: 'digest',
        artifactHash: 'hash',
        version: 'version',
        canApply: true,
        expiresAtUtc: new Date(2000).toISOString(),
        checks: [],
      },
    }
    expect(applicablePackage(status, 'selected', 1000)).toBe(true)
    expect(applicablePackage(status, 'other', 1000)).toBe(false)
    expect(applicablePackage(status, 'selected', 2000)).toBe(false)
    status.validation!.expiresAtUtc = 'invalid'
    expect(applicablePackage(status, 'selected', 1000)).toBe(false)
  })
})
