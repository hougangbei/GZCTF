import { Navigate } from 'react-router'
import type { HierarchyTree } from '@Utils/skillHierarchy'
import { SkillHierarchyBrowser } from './SkillHierarchyBrowser'

export const SkillTreeOutline = ({ tree, selectedCategoryId }: { tree: HierarchyTree; selectedCategoryId?: string }) =>
  selectedCategoryId ? (
    <Navigate replace to={`/skill-trees/${tree.skillTreeId}/${selectedCategoryId}/skills`} />
  ) : (
    <SkillHierarchyBrowser tree={tree} />
  )
