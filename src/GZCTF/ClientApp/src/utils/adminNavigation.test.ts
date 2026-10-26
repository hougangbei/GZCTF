import { describe, expect, it } from 'vitest'
import { getAdminSection } from './adminNavigation'

describe('admin deep link selection', () => {
  it('keeps query tabs, unknown tabs, and editor routes in their owning section', () => {
    expect(getAdminSection('/admin/skill-trees', '?tab=writeupManagement')).toBe('writeupManagement')
    expect(getAdminSection('/admin/skill-trees', '?tab=invalid')).toBe('trees')
    expect(getAdminSection('/admin/skill-trees/abc', '')).toBe('trees')
    expect(getAdminSection('/admin/library/challenges/abc', '')).toBe('challenges')
    expect(getAdminSection('/admin/library/lessons/abc', '')).toBe('lessons')
    expect(getAdminSection('/admin/instances', '')).toBe('gameInstances')
    expect(getAdminSection('/admin/users', '')).toBe('users')
    expect(getAdminSection('/admin/settings', '')).toBe('settings')
  })
})
