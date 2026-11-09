import {
  contentEditorHref,
  workspaceHref,
  type CourseCheck,
  type CourseDocument,
  type CourseSelection,
  type PublicationIssue,
} from './courseAuthoring'

export type CourseReview = {
  schema: 1
  courseId: string
  document: CourseDocument
  check: CourseCheck
  stale: boolean
  changedKeys: string[]
}
export type ReviewGroup = { key: string; title: string; node?: CourseSelection; issues: PublicationIssue[] }
export const reviewKey = (userId: string, courseId: string) =>
  `course-review:v1:${encodeURIComponent(userId)}:${encodeURIComponent(courseId)}`
export const courseReviewHref = (id: string) => `/admin/skill-trees/${encodeURIComponent(id)}?review=1`
export function buildCourseReview(courseId: string, document: CourseDocument, check: CourseCheck): CourseReview {
  return {
    schema: 1,
    courseId,
    check,
    stale: false,
    changedKeys: [],
    document: {
      ...document,
      summary: '',
      categories: document.categories.map((c) => ({
        ...c,
        summary: '',
        skills: c.skills.map((s) => ({ ...s, summary: '', contents: s.contents.map((i) => ({ ...i, summary: '' })) })),
      })),
    },
  }
}
export function markReviewChanged(review: CourseReview, node?: CourseSelection): CourseReview {
  const key = node?.contentId ? `${node.kind}:${node.contentId}` : undefined
  return { ...review, stale: true, changedKeys: [...new Set([...review.changedKeys, ...(key ? [key] : [])])] }
}
export function groupReviewIssues(review: CourseReview): ReviewGroup[] {
  const groups = new Map<string, ReviewGroup>()
  for (const issue of review.check.issues) {
    let node: CourseSelection | undefined,
      title = '课程设置'
    for (const c of review.document.categories) {
      if (issue.categoryId === c.categoryId) {
        node = { kind: 'category', categoryId: c.categoryId }
        title = c.name
      }
      for (const s of c.skills) {
        if (issue.skillId === s.skillId) {
          node = { kind: 'skill', categoryId: c.categoryId, skillId: s.skillId }
          title = s.name
        }
        const item = s.contents.find((i) => i.contentId === issue.contentId)
        if (item && !node?.contentId) {
          node = { kind: item.kind, contentId: item.contentId, categoryId: c.categoryId, skillId: s.skillId }
          title = item.title
        }
      }
    }
    const key = issue.contentId
      ? `${node?.kind ?? 'content'}:${issue.contentId}`
      : issue.skillId
        ? `skill:${issue.skillId}`
        : issue.categoryId
          ? `category:${issue.categoryId}`
          : 'tree'
    const group = groups.get(key) ?? { key, title, node, issues: [] }
    group.issues.push(issue)
    groups.set(key, group)
  }
  return [...groups.values()].sort(
    (a, b) =>
      Number(b.issues.some((i) => i.severity === 'error')) - Number(a.issues.some((i) => i.severity === 'error'))
  )
}
export function reviewIssueHref(courseId: string, group: ReviewGroup): string {
  const node = group.node
  return node?.contentId &&
    !group.issues.some((i) => i.code.endsWith('_identity_unavailable')) &&
    (node.kind === 'challenge' || node.kind === 'lesson')
    ? contentEditorHref(courseId, node) + '&review=1'
    : workspaceHref(courseId, node ?? { kind: 'tree' })
}
export function parseCourseReview(raw: string | null, courseId: string): CourseReview | undefined {
  if (!raw) return
  try {
    const r = JSON.parse(raw)
    if (
      r?.schema !== 1 ||
      r.courseId !== courseId ||
      typeof r.stale !== 'boolean' ||
      !Array.isArray(r.changedKeys) ||
      !r.changedKeys.every((k: unknown) => typeof k === 'string') ||
      typeof r.check?.version !== 'string' ||
      !Array.isArray(r.check?.issues) ||
      !r.check.issues.every(
        (i: any) =>
          i &&
          typeof i.code === 'string' &&
          typeof i.message === 'string' &&
          ['error', 'warning'].includes(i.severity) &&
          (i.locales == null || (Array.isArray(i.locales) && i.locales.every((l: unknown) => typeof l === 'string')))
      ) ||
      typeof r.document?.name !== 'string' ||
      !Array.isArray(r.document.categories)
    )
      return
    const strings = (v: unknown) => Array.isArray(v) && v.every((i) => typeof i === 'string')
    if (
      !['categoryCount', 'skillCount', 'lessonCount', 'challengeCount'].every(
        (k) => typeof r.check[k] === 'number' && Number.isFinite(r.check[k])
      )
    )
      return
    if (r.check.changes != null && (!strings(r.check.changes.added) || !strings(r.check.changes.removed))) return
    if (r.check.impacts != null && !strings(r.check.impacts)) return
    if (
      r.check.runtimeChecks != null &&
      (!Array.isArray(r.check.runtimeChecks) ||
        !r.check.runtimeChecks.every(
          (i: any) =>
            i &&
            typeof i.contentId === 'string' &&
            typeof i.title === 'string' &&
            typeof i.score === 'number' &&
            Number.isFinite(i.score) &&
            (i.image == null || typeof i.image === 'string') &&
            (i.port == null || typeof i.port === 'number')
        ))
    )
      return
    for (const c of r.document.categories) {
      if (!c || typeof c.categoryId !== 'string' || typeof c.name !== 'string' || !Array.isArray(c.skills)) return
      for (const s of c.skills) {
        if (!s || typeof s.skillId !== 'string' || typeof s.name !== 'string' || !Array.isArray(s.contents)) return
        if (
          !s.contents.every(
            (i: any) =>
              i &&
              typeof i.contentId === 'string' &&
              typeof i.title === 'string' &&
              ['lesson', 'challenge'].includes(i.kind)
          )
        )
          return
      }
    }
    return r as CourseReview
  } catch {
    return
  }
}
