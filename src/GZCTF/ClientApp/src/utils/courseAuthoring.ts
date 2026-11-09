export type CourseContent = {
  contentId: string
  kind: 'lesson' | 'challenge'
  title: string
  summary: string
  state: string
}
export type CourseSkill = {
  skillId: string
  name: string
  summary: string
  iconKey: string
  isEnabled: boolean
  contents: CourseContent[]
}
export type CourseCategory = {
  categoryId: string
  name: string
  summary: string
  iconKey: string
  skills: CourseSkill[]
}
export type CourseDocument = {
  name: string
  summary: string
  iconKey: string
  coverUrl?: string | null
  categories: CourseCategory[]
}
export type CourseWorkspace = {
  skillTreeId: string
  version: string
  document: CourseDocument
  hasPublishedVersion: boolean
}
export type CourseSelection = {
  kind: 'tree' | 'category' | 'skill' | 'lesson' | 'challenge'
  categoryId?: string
  skillId?: string
  contentId?: string
}
export type PublicationIssue = {
  severity: 'error' | 'warning'
  code: string
  message: string
  categoryId?: string
  skillId?: string
  contentId?: string
  locales?: string[]
}
export type CourseCheck = {
  changes?: { added: string[]; removed: string[] }
  impacts?: string[]
  runtimeChecks?: { contentId: string; title: string; image?: string | null; port?: number | null; score: number }[]
  publishedVersion?: string

  version: string
  issues: PublicationIssue[]
  categoryCount: number
  skillCount: number
  challengeCount: number
  lessonCount: number
}
export const patchCourseSkill = (
  document: CourseDocument,
  skillId: string,
  update: (skill: CourseSkill) => CourseSkill
): CourseDocument => ({
  ...document,
  categories: document.categories.map((category) => ({
    ...category,
    skills: category.skills.map((skill) => (skill.skillId === skillId ? update(skill) : skill)),
  })),
})
export const removeCourseNode = (document: CourseDocument, node: CourseSelection): CourseDocument => {
  if (node.kind === 'category')
    return { ...document, categories: document.categories.filter((c) => c.categoryId !== node.categoryId) }
  if (node.kind === 'skill')
    return {
      ...document,
      categories: document.categories.map((c) =>
        c.categoryId === node.categoryId ? { ...c, skills: c.skills.filter((s) => s.skillId !== node.skillId) } : c
      ),
    }
  return patchCourseSkill(document, node.skillId!, (skill) => ({
    ...skill,
    contents: skill.contents.filter((c) => !(c.contentId === node.contentId && c.kind === node.kind)),
  }))
}
const moved = <T>(list: T[], index: number, delta: number): T[] => {
  const next = index + delta
  if (index < 0 || next < 0 || next >= list.length) return list
  const result = [...list]
  const [item] = result.splice(index, 1)
  result.splice(next, 0, item)
  return result
}
export const moveCourseNode = (document: CourseDocument, node: CourseSelection, delta: number): CourseDocument => {
  if (node.kind === 'category')
    return {
      ...document,
      categories: moved(
        document.categories,
        document.categories.findIndex((c) => c.categoryId === node.categoryId),
        delta
      ),
    }
  if (node.kind === 'skill')
    return {
      ...document,
      categories: document.categories.map((c) =>
        c.categoryId === node.categoryId
          ? {
              ...c,
              skills: moved(
                c.skills,
                c.skills.findIndex((s) => s.skillId === node.skillId),
                delta
              ),
            }
          : c
      ),
    }
  return patchCourseSkill(document, node.skillId!, (skill) => ({
    ...skill,
    contents: moved(
      skill.contents,
      skill.contents.findIndex((c) => c.contentId === node.contentId && c.kind === node.kind),
      delta
    ),
  }))
}
export const workspaceHref = (courseId: string, node: CourseSelection) =>
  `/admin/skill-trees/${encodeURIComponent(courseId)}?${new URLSearchParams({ ...(node.categoryId ? { category: node.categoryId } : {}), ...(node.skillId ? { skill: node.skillId } : {}) })}`
export const contentEditorHref = (courseId: string, node: CourseSelection) =>
  `/admin/library/${node.kind === 'lesson' ? 'lessons' : 'challenges'}/${encodeURIComponent(node.contentId!)}?${new URLSearchParams({ course: courseId, category: node.categoryId!, skill: node.skillId! })}`
export type CourseContext = { courseId: string; categoryId: string; skillId: string }
export const courseContext = (params: URLSearchParams): CourseContext | 'invalid' | undefined => {
  if (!params.has('course')) return params.has('category') || params.has('skill') ? 'invalid' : undefined
  const ids = ['course', 'category', 'skill'].map((key) => params.get(key) ?? '')
  if (ids.some((id) => !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id))) return 'invalid'
  return { courseId: ids[0], categoryId: ids[1], skillId: ids[2] }
}
export const contextualPath = (path: string, context: CourseContext | undefined) =>
  context ? `${path}${path.includes('?') ? '&' : '?'}course=${encodeURIComponent(context.courseId)}` : path

export const reuseCourseSkill = (document: CourseDocument, candidate: CourseSkill): CourseSkill =>
  document.categories.flatMap((category) => category.skills).find((skill) => skill.skillId === candidate.skillId) ??
  candidate
