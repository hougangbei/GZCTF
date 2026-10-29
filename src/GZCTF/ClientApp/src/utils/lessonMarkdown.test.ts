import { describe, expect, it } from 'vitest'
import { getLessonHeadings, insertMarkdown } from './lessonMarkdown'

describe('lesson markdown', () => {
  it('collects real headings with unique anchors and ignores fenced code', () => {
    expect(getLessonHeadings('## 标题\n\n```md\n## false\n```\n\n## 标题')).toEqual([
      { id: 'lesson-heading-0', title: '标题', level: 2 },
      { id: 'lesson-heading-1', title: '标题', level: 2 },
    ])
  })
  it('keeps duplicate anchors stable throughout a long lesson and ignores all code fence headings', () => {
    const section =
      '## Repeat\n\n' + 'A long teaching paragraph. '.repeat(100) + '\n\n~~~markdown\n## Not a heading\n~~~\n\n'
    const headings = getLessonHeadings(section.repeat(80) + 'Final section\n=============')
    expect(headings).toHaveLength(81)
    expect(new Set(headings.map((heading) => heading.id)).size).toBe(81)
    expect(headings.slice(0, 80).every((heading) => heading.title === 'Repeat' && heading.level === 2)).toBe(true)
    expect(headings[80]).toEqual({ id: 'lesson-heading-80', title: 'Final section', level: 1 })
  })
  it('retains literal punctuation and readable inline text in outline titles', () => {
    expect(
      getLessonHeadings('## HTTP_header &amp; **cookies** [reference][guide]\n\n[guide]: /reference')[0].title
    ).toBe('HTTP_header & cookies reference')
  })
  it('wraps selected text and leaves an editable selection', () => {
    expect(insertMarkdown('hello world', 6, 11, '**', '**', 'bold')).toEqual({
      value: 'hello **world**',
      start: 8,
      end: 13,
    })
    expect(insertMarkdown('', 0, 0, '## ', '', 'Heading').value).toBe('## Heading')
  })
})
