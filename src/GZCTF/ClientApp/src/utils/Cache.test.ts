import { describe, expect, it } from 'vitest'
import { shouldPersistCacheKey } from './Cache'

describe('persistent SWR cache boundary', () => {
  it('keeps only explicitly public API data on disk', () => {
    expect(shouldPersistCacheKey('/api/config')).toBe(true)
    expect(shouldPersistCacheKey('/api/posts/latest')).toBe(true)
    expect(shouldPersistCacheKey('/api/posts')).toBe(true)
    expect(shouldPersistCacheKey('/api/skill-trees')).toBe(true)
    expect(shouldPersistCacheKey('/api/about')).toBe(true)
    expect(shouldPersistCacheKey('/api/account/profile')).toBe(false)
    expect(shouldPersistCacheKey('/api/admin/skill-trees')).toBe(false)
    expect(shouldPersistCacheKey('/api/tokens')).toBe(false)
    expect(shouldPersistCacheKey(['/api/admin/cohorts', { page: 1 }])).toBe(false)
  })
})
