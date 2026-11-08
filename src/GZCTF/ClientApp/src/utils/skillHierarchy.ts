import type { SkillTreeContentSummaryResponse, SkillTreeDetailResponse, SkillTreeSummaryResponse } from '@Api'

export type SkillSummary = {
  skillId: string
  name: string
  summary: string
  iconKey: string
  isEnabled?: boolean
  sortOrder: number
  challengeCount: number
  lessonCount: number
  contents: SkillTreeContentSummaryResponse[]
}
export type HierarchyCategory = NonNullable<SkillTreeDetailResponse['categories']>[number] & { skills?: SkillSummary[] }
export type HierarchyTree = Omit<SkillTreeDetailResponse, 'categories'> & {
  categories?: HierarchyCategory[]
  skillCount?: number
  coverUrl?: string | null
}
export type HierarchyTreeSummary = SkillTreeSummaryResponse & { skillCount?: number; coverUrl?: string | null }
export type Counts = { totalCount: number; completedCount: number }
export type TreeProgress = Counts & {
  completedLessonIds: string[]
  solvedChallengeIds: string[]
  categories: (Counts & { categoryId: string })[]
  skills: (Counts & { skillId: string })[]
}
export type AdminSkill = Omit<SkillSummary, 'challengeCount' | 'lessonCount' | 'sortOrder'> & {
  isEnabled: boolean
  rowVersion: number
  categories: { categoryId: string; name: string }[]
}
export const skillHref = (treeId: string, categoryId: string, skillId: string) =>
  `/skill-trees/${treeId}/${categoryId}/skills/${skillId}`
export const orderedSkills = (category?: HierarchyCategory) =>
  [...(category?.skills ?? [])].sort((a, b) => a.sortOrder - b.sortOrder || a.skillId.localeCompare(b.skillId))
export const progressPercent = (counts?: Counts) =>
  counts && counts.totalCount > 0 ? Math.min(100, Math.max(0, (counts.completedCount / counts.totalCount) * 100)) : 0
export const contentCompleted = (progress: TreeProgress | undefined, kind?: string, id?: string) =>
  Boolean(id && (kind === 'lesson' ? progress?.completedLessonIds : progress?.solvedChallengeIds)?.includes(id))
