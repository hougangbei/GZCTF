import type { SWRConfiguration } from 'swr'
import Api from '@Api'

export const useSkillTrees = (options?: SWRConfiguration, doFetch: boolean = true) =>
  Api.skillTrees.useSkillTreesList(options, doFetch)

export const useSkillTree = (id?: string, options?: SWRConfiguration) =>
  Api.skillTrees.useSkillTreesDetail(id ?? '', options, Boolean(id))

export const useMyLearning = (enabled: boolean, locale?: string) =>
  Api.myLearning.useMyLearningGet(locale ? { locale } : undefined, undefined, enabled)
