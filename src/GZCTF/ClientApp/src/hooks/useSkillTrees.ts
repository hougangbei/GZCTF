import type { SWRConfiguration } from 'swr'
import type { SWRResponse } from 'swr'
import type { HierarchyTree, HierarchyTreeSummary } from '@Utils/skillHierarchy'
import Api from '@Api'

export const useSkillTrees = (options?: SWRConfiguration, doFetch: boolean = true) =>
  Api.skillTrees.useSkillTreesList(options, doFetch) as SWRResponse<HierarchyTreeSummary[]>

export const useSkillTree = (id?: string, options?: SWRConfiguration) =>
  Api.skillTrees.useSkillTreesDetail(id ?? '', options, Boolean(id)) as SWRResponse<HierarchyTree>

export const useMyLearning = (enabled: boolean, locale?: string) =>
  Api.myLearning.useMyLearningGet(locale ? { locale } : undefined, undefined, enabled)
