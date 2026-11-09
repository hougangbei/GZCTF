import { describe, it, expect } from 'vitest'
import type { CourseWorkspace } from './courseAuthoring'
import { contentKey, uniqueContents, transferCommand, resourceLabel } from './courseTransfer'

describe('course transfer identity', () => {
  it('keeps kind in identity and preserves first source metadata', () => {
    const a = { contentId: 'same', kind: 'challenge' as const, title: 'a', summary: '', state: 'Draft' }
    const lesson = { ...a, kind: 'lesson' as const }
    expect(contentKey(a)).not.toBe(contentKey(lesson))
    const result = uniqueContents([a, lesson, { ...a, title: 'other metadata' }])
    expect(result).toHaveLength(2)
    expect(result[0].contentId).toBe('same')
    expect(result[0].title).toBe('a')
  })
  it('sends reference IDs and both versions without content text', () => {
    const w = {
      skillTreeId: 'tree',
      version: 'version',
      hasPublishedVersion: true,
      document: { name: 'Name', summary: '', iconKey: 'flag', categories: [] },
    } satisfies CourseWorkspace
    const result = transferCommand(
      w,
      { ...w, skillTreeId: 'target', version: 'targetVersion' },
      { categoryId: 'c', skillId: 's' },
      { categoryId: 'd', skillId: 't' },
      [{ kind: 'challenge', contentId: 'question' }],
      'move',
      'operation'
    )
    expect(result).toMatchObject({
      sourceVersion: 'version',
      targetVersion: 'targetVersion',
      contents: [{ kind: 'challenge', contentId: 'question' }],
    })
    expect(JSON.stringify(result)).not.toContain('Name')
  })
  it('same global name has distinguishable source and course context', () => {
    const row = {
      id: 'id',
      kind: 'category',
      name: 'MISC',
      sourceLabels: ['ExSolo [gzctf]'],
      locations: [
        {
          treeId: 'tree',
          treeName: 'ExSolo个人赛',
          categoryId: 'category',
          categoryName: 'misc (26-ExSolo)',
          state: 'draft' as const,
        },
      ],
      unassociated: false,
      contentCount: 3,
    }
    expect(resourceLabel(row)).toContain('ExSolo [gzctf]')
    expect(resourceLabel(row)).toContain('ExSolo个人赛')
    expect(resourceLabel({ ...row, sourceLabels: [] })).toContain('自建')
  })
})
