import { describe, it, expect } from 'vitest'
import type { CourseWorkspace } from './courseAuthoring'
import { contentKey, uniqueContents, transferCommand, resourceLabel, moveOutlineContent } from './courseTransfer'

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

describe('inline outline moves', () => {
  const content = { contentId: 'q', kind: 'challenge' as const, title: 'Q', summary: '', state: 'Draft' }
  const skill = (skillId: string, contents = [content]) => ({
    skillId,
    name: skillId,
    summary: '',
    iconKey: 'web',
    isEnabled: true,
    contents,
  })
  const doc = () => ({
    name: 'Tree',
    summary: '',
    iconKey: 'web',
    categories: [
      { categoryId: 'a', name: 'A', summary: '', iconKey: 'web', skills: [skill('s')] },
      { categoryId: 'b', name: 'B', summary: '', iconKey: 'web', skills: [skill('t', []), skill('s')] },
    ],
  })
  const from = { kind: 'challenge' as const, contentId: 'q', categoryId: 'a', skillId: 's' }
  it('moves across category/skill and updates every shared occurrence without mutating input', () => {
    const original = doc(),
      next = moveOutlineContent(original, from, { categoryId: 'b', skillId: 't' })
    expect(next.categories[0].skills[0].contents).toEqual([])
    expect(next.categories[1].skills[1].contents).toEqual([])
    expect(next.categories[1].skills[0].contents).toEqual([content])
    expect(original.categories[0].skills[0].contents).toEqual([content])
  })
  it('adds a reference without removing original, and deduplicates target', () => {
    const original = doc()
    original.categories[1].skills[0].contents = [content]
    const next = moveOutlineContent(original, from, { categoryId: 'b', skillId: 't' }, 'reference')
    expect(next.categories[0].skills[0].contents).toEqual([content])
    expect(next.categories[1].skills[0].contents).toEqual([content])
  })
  it('does not remove anything when source, target or content is stale, or target is same shared skill', () => {
    const original = doc()
    for (const target of [
      { categoryId: 'b', skillId: 's' },
      { categoryId: 'missing', skillId: 't' },
    ])
      expect(moveOutlineContent(original, from, target)).toBe(original)
    expect(moveOutlineContent(original, { ...from, contentId: 'missing' }, { categoryId: 'b', skillId: 't' })).toBe(
      original
    )
  })
})
