import useSWR, { useSWRConfig } from 'swr'
import { fetcher } from '@Api'

export type ImportIssue = { severity: string; code: string; sourceId: string; message: string }
export type ImportOptions = {
  sourceInstance: string
  treeName?: string
  targetSkillId?: string
  targetSkillRowVersion?: number
  includeAccounts: boolean
  includeHistory: boolean
  userNameOverrides?: Record<string, string>
  skipUserIds?: string[]
}
export type ImportPreview = {
  format: string
  fingerprint: string
  planFingerprint: string
  title: string
  challengeCount: number
  fileCount: number
  userCount: number
  teamCount: number
  submissionCount: number
  awardCount: number
  issues: ImportIssue[]
  canImport: boolean
}
export type ImportBatch = {
  id: string
  title: string
  sourceInstance: string
  format: string
  createdAtUtc: number | string
  treeId?: string
  challengeCount: number
  userCount: number
  teamCount: number
  submissionCount: number
  awardCount: number
  reportJson: string
}
export const usePlatformImportBatches = () => useSWR<ImportBatch[]>('/api/admin/platform-imports', fetcher)
export const usePlatformImportActions = () => {
  const { mutate } = useSWRConfig()
  return {
    upload: async <T>(file: File, options: ImportOptions, plan?: string): Promise<T> => {
      const form = new FormData()
      form.set('package', file)
      form.set('options', JSON.stringify(options))
      if (plan) form.set('planFingerprint', plan)
      const response = await fetch(`/api/admin/platform-imports${plan ? '' : '/preview'}`, {
        method: 'POST',
        credentials: 'include',
        body: form,
      })
      const body = await response.json().catch(() => ({}))
      if (!response.ok)
        throw new Error(
          body.message ??
            body.issues?.find((issue: ImportIssue) => issue.severity === 'error')?.message ??
            '导入失败，请重新预检后重试。'
        )
      if (plan)
        await mutate((key) => {
          const path = Array.isArray(key) ? key[0] : key
          return (
            typeof path === 'string' &&
            /^\/api\/(admin\/(platform-imports|skills|skill-trees|challenges)|account\/import-history)/.test(path)
          )
        })
      return body as T
    },
  }
}
export type ImportHistory = {
  members?: {
    id: string
    batchId: string
    sourceUserId: string
    sourceUserName: string
    sourceTeamId?: string
    sourceTeamName?: string
  }[]
  memberCount?: number
  batches: { id: string; sourceInstance: string; title: string; format: string; createdAtUtc: number | string }[]
  submissions: {
    id: string
    batchId: string
    sourceUserName?: string
    sourceTeamName?: string
    challengeTitle: string
    result: string
    score?: number
    submittedAtUtc?: number | string
    attribution: string
  }[]
  awards: {
    id: string
    batchId: string
    name: string
    value: number
    sourceUserName?: string
    sourceTeamName?: string
    attribution: string
  }[]
  submissionCount: number
  awardCount: number
  skip: number
  take: number
}
export const useImportHistory = (admin = false, skip = 0) =>
  useSWR<ImportHistory>(
    `${admin ? '/api/admin/platform-imports/history' : '/api/account/import-history'}?skip=${skip}&take=50`,
    fetcher
  )
