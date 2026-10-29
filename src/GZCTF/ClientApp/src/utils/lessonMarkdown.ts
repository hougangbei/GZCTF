import { Marked, type Token } from 'marked'

const headingText = (tokens: Token[]): string =>
  tokens
    .map((token) => {
      if ('tokens' in token && token.tokens) return headingText(token.tokens)
      if (token.type === 'html') return token.text.replace(/<[^>]*>/g, '')
      return 'text' in token ? String(token.text) : ''
    })
    .join('')

const decodeEntities = (text: string) =>
  text.replace(/&(#x[\da-f]+|#\d+|amp|lt|gt|quot|apos|nbsp);/gi, (entity, name: string) => {
    if (name[0] === '#') {
      const point = name[1].toLowerCase() === 'x' ? parseInt(name.slice(2), 16) : parseInt(name.slice(1), 10)
      return point > 0 && point <= 0x10ffff ? String.fromCodePoint(point) : entity
    }
    return (
      ({ amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: '\u00a0' } as Record<string, string>)[
        name.toLowerCase()
      ] ?? entity
    )
  })

export type LessonHeading = { id: string; title: string; level: number }
export const getLessonHeadings = (source: string): LessonHeading[] => {
  const marked = new Marked()
  const result: LessonHeading[] = []
  marked.walkTokens(marked.lexer(source), (token) => {
    if (token.type === 'heading')
      result.push({
        id: `lesson-heading-${result.length}`,
        title: decodeEntities(headingText(token.tokens ?? [])),
        level: token.depth,
      })
  })
  return result
}

export const insertMarkdown = (
  value: string,
  start: number,
  end: number,
  before: string,
  after: string,
  placeholder: string
) => {
  const selected = value.slice(start, end) || placeholder
  return {
    value: value.slice(0, start) + before + selected + after + value.slice(end),
    start: start + before.length,
    end: start + before.length + selected.length,
  }
}
