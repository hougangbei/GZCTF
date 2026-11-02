import { describe, expect, it } from 'vitest'
import { contentCompleted, orderedSkills, progressPercent, skillHref, type TreeProgress } from './skillHierarchy'

describe('skill hierarchy navigation and progress', () => {
  it('keeps stable skill ordering for legacy URL resolution', () => {
    const category = {
      skills: [
        { skillId: 'b', sortOrder: 0 },
        { skillId: 'a', sortOrder: 0 },
        { skillId: 'c', sortOrder: 1 },
      ],
    }
    expect(orderedSkills(category as Parameters<typeof orderedSkills>[0]).map((item) => item.skillId)).toEqual([
      'a',
      'b',
      'c',
    ])
    expect(category.skills[0].skillId).toBe('b')
  })
  it('empty skills are never shown as complete', () => {
    expect(progressPercent({ totalCount: 0, completedCount: 0 })).toBe(0)
    expect(progressPercent({ totalCount: 3, completedCount: 1 })).toBeCloseTo(33.333)
    expect(progressPercent({ totalCount: 1, completedCount: 4 })).toBe(100)
  })
  it('uses shared completion identity without mixing lesson and challenge ids', () => {
    const progress = { completedLessonIds: ['same'], solvedChallengeIds: [] } as unknown as TreeProgress
    expect(contentCompleted(progress, 'lesson', 'same')).toBe(true)
    expect(contentCompleted(progress, 'challenge', 'same')).toBe(false)
    expect(contentCompleted(undefined, 'lesson', 'same')).toBe(false)
    expect(skillHref('tree', 'category', 'skill')).toBe('/skill-trees/tree/category/skills/skill')
  })
})
