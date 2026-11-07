import { expect, it } from 'vitest'
import {
  contentEditorHref,
  courseContext,
  reuseCourseSkill,
  moveCourseNode,
  patchCourseSkill,
  removeCourseNode,
  type CourseDocument,
} from './courseAuthoring'

const doc: CourseDocument = {
  name: 'Web',
  summary: '',
  iconKey: 'web',
  categories: ['a', 'b'].map((categoryId) => ({
    categoryId,
    name: categoryId,
    summary: '',
    iconKey: 'web',
    skills: [
      {
        skillId: 'shared',
        name: 'SQL',
        summary: '',
        iconKey: 'web',
        isEnabled: true,
        contents: [
          { contentId: 'one', kind: 'lesson', title: 'One', summary: '', state: 'Draft' },
          { contentId: 'two', kind: 'challenge', title: 'Two', summary: '', state: 'Published' },
        ],
      },
    ],
  })),
}
it('edits shared skill references consistently within the draft without mutating the original', () => {
  const next = patchCourseSkill(doc, 'shared', (skill) => ({ ...skill, name: 'SQL basics' }))
  expect(next.categories.map((c) => c.skills[0].name)).toEqual(['SQL basics', 'SQL basics'])
  expect(doc.categories[0].skills[0].name).toBe('SQL')
})
it('removes only the selected placement and keeps other references', () => {
  const next = removeCourseNode(doc, { kind: 'skill', categoryId: 'a', skillId: 'shared' })
  expect(next.categories[0].skills).toHaveLength(0)
  expect(next.categories[1].skills[0].contents).toHaveLength(2)
})
it('moves content consistently for repeated skill references in the same course', () => {
  const next = moveCourseNode(doc, { kind: 'challenge', categoryId: 'a', skillId: 'shared', contentId: 'two' }, -1)
  expect(next.categories.map((c) => c.skills[0].contents[0].contentId)).toEqual(['two', 'two'])
  expect(moveCourseNode(next, { kind: 'category', categoryId: 'a' }, -1)).toEqual(next)
})
it('preserves parent context in the editor link and rejects incomplete or malicious contexts', () => {
  const id = '01990000-0000-7000-8000-000000000001'
  const href = contentEditorHref(id, { kind: 'lesson', categoryId: id, skillId: id, contentId: id })
  const context = courseContext(new URL(href, 'http://localhost').searchParams)
  expect(context).toMatchObject({ courseId: id, categoryId: id, skillId: id })
  expect(courseContext(new URLSearchParams('course=https://evil.example'))).toBe('invalid')
  expect(courseContext(new URLSearchParams())).toBeUndefined()
})

it('never downgrades a partial parent context to a global editor', () => {
  expect(courseContext(new URLSearchParams('category=one&skill=two'))).toBe('invalid')
})
it('reusing an already edited skill preserves its draft definition', () => {
  const edited = patchCourseSkill(doc, 'shared', (skill) => ({ ...skill, name: 'Local draft' }))
  expect(reuseCourseSkill(edited, { ...doc.categories[0].skills[0], name: 'Library old' }).name).toBe('Local draft')
})

it('drag moves a node across multiple positions without swapping the intervening order', () => {
  const document = {
    ...doc,
    categories: ['a', 'b', 'c', 'd'].map((categoryId) => ({ ...doc.categories[0], categoryId })),
  }
  expect(
    moveCourseNode(document, { kind: 'category', categoryId: 'a' }, 2).categories.map((c) => c.categoryId)
  ).toEqual(['b', 'c', 'a', 'd'])
})
