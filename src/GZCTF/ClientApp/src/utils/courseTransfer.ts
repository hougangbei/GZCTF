import type { CourseContent, CourseWorkspace } from './courseAuthoring'

export type ContentKey = { kind: 'challenge' | 'lesson'; contentId: string }
export type TransferTarget = { categoryId: string; skillId: string }
export type TransferCommand = {
  operationId: string
  mode: 'move' | 'reference'
  sourceTreeId: string
  sourceVersion: string
  sourceCategoryId: string
  sourceSkillId: string
  targetTreeId: string
  targetVersion: string
  targetCategoryId: string
  targetSkillId: string
  contents: ContentKey[]
}
export type TransferResult = {
  source: CourseWorkspace
  target: CourseWorkspace
  addedCount: number
  removedCount: number
  duplicateCount: number
  noOp: boolean
}
export type ResourceLocation = {
  treeId: string
  treeName: string
  categoryId: string
  categoryName: string
  skillId?: string
  skillName?: string
  state: 'draft' | 'published'
}
export type ResourceRow = {
  id: string
  kind: string
  name: string
  sourceLabels: string[]
  locations: ResourceLocation[]
  unassociated: boolean
  contentCount: number
}
export const contentKey = (item: ContentKey) => `${item.kind}:${item.contentId}`
export function uniqueContents(items: CourseContent[]): CourseContent[] {
  const unique = new Map<string, CourseContent>()
  for (const item of items) if (!unique.has(contentKey(item))) unique.set(contentKey(item), item)
  return [...unique.values()]
}
export const locationLabel = (p: ResourceLocation) =>
  [p.treeName, p.categoryName, p.skillName].filter(Boolean).join(' / ')
export function resourceLabel(item: ResourceRow | undefined): string {
  return item
    ? `${item.name} · ${item.sourceLabels.length ? item.sourceLabels.join(' · ') : '自建'}${item.locations.length ? ' · ' + [...new Set(item.locations.map((p) => p.treeName))].join('、') : ''}`
    : ''
}
export const transferCommand = (
  source: CourseWorkspace,
  target: CourseWorkspace,
  from: TransferTarget,
  to: TransferTarget,
  contents: ContentKey[],
  mode: 'move' | 'reference',
  operationId: string
): TransferCommand => ({
  operationId,
  mode,
  sourceTreeId: source.skillTreeId,
  sourceVersion: source.version,
  sourceCategoryId: from.categoryId,
  sourceSkillId: from.skillId,
  targetTreeId: target.skillTreeId,
  targetVersion: target.version,
  targetCategoryId: to.categoryId,
  targetSkillId: to.skillId,
  contents: contents.map(({ kind, contentId }) => ({ kind, contentId })),
})
