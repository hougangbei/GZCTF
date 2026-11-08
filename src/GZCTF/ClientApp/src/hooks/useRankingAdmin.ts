import useSWR, { useSWRConfig } from 'swr'
import { fetcher } from '@Api'

const base = '/api/admin/learning-rankings'
const errorMessages: Record<string, string> = {
  'ranking.version_conflict': '成绩或等级规则已变化，请刷新后核对再操作。',
  'ranking.operation_payload_mismatch': '该操作编号已用于其他内容，请刷新后重新操作。',
  'ranking.invalid_adjustment': '请输入有效的整数积分或目标总分。',
  'ranking.already_reversed_or_not_adjustment': '该记录已撤销，或需要在通关管理中处理。',
  'ranking.invalid_thresholds': '等级门槛必须从 0 开始、严格递增，且不超过 2147483647。',
  'ranking.reason_required': '请填写 1–1000 字的调整原因。',
  'ranking.score_out_of_range': '调整后的总分超出允许范围，请检查数值。',
  'ranking.challenge_merged': '题目已经合并，请刷新并选择保留的题目。',
  'ranking.import_frozen_score_unavailable': '该题的迁入分值无法核实，暂不能撤销，请检查原始备份及密钥。',
}
export type RankingUser = {
  userId: string; userName: string; bio: string; avatar?: string; role: string
  hidden: boolean; sourceHidden: boolean; visibilityOverride?: boolean | null
  score: number; importedScore: number; earnedScore: number; adjustmentScore: number
  solvedCount: number; importedSolvedCount: number; earnedSolvedCount: number; level: number; version: string
}
export type RankingOperation = {
  id: string; operationId: string; userId: string; targetUserName?: string; actorId: string; actorName: string; kind: string
  reason: string; delta: number; beforeScore: number; afterScore: number; solvedDelta: number
  challengeId?: string | null; createdAtUtc: number | string; reversesId?: string | null; reversed: boolean; canReverse?: boolean
}
export type RankingChallenge = { challengeId: string; title: string; score: number; solved: boolean; imported: boolean; frozenScoreAvailable?: boolean; solvedAtUtc?: number | string | null }
export type RankingDetail = { user: RankingUser; challenges: RankingChallenge[]; operations: RankingOperation[] }
export type RankingPage<T> = { items: T[]; total: number; skip: number; take: number }
export type RankingLevels = { version: string; thresholds: number[] }
export const useRankingUsers = (search: string, skip: number, visibility = 'all') => useSWR<RankingPage<RankingUser>>(`${base}?search=${encodeURIComponent(search)}&skip=${skip}&take=20&visibility=${visibility}`, fetcher, { keepPreviousData: false })
export const useRankingDetail = (id?: string) => useSWR<RankingDetail>(id ? `${base}/${id}` : null, fetcher, { keepPreviousData: false })
export const useRankingOperations = (skip: number, userId?: string) => useSWR<RankingPage<RankingOperation>>(`${base}/operations?skip=${skip}&take=20${userId ? `&userId=${encodeURIComponent(userId)}` : ''}`, fetcher, { keepPreviousData: false })
export const useRankingLevels = () => useSWR<RankingLevels>(`${base}/levels`, fetcher)
export class RankingRequestError extends Error {
  constructor(message: string, public conflict: boolean) { super(message) }
}
export const rankingOperationId = () => {
  if (typeof globalThis.crypto?.randomUUID === 'function') return crypto.randomUUID()
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128
  const hex = [...bytes].map(value => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`
}
export const useRankingActions = () => {
  const { mutate } = useSWRConfig()
  const request = async <T>(path: string, method: string, body?: unknown): Promise<T> => {
    const response = await fetch(base + path, { method, credentials: 'include', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) })
    const data = await response.json().catch(() => ({}))
    if (!response.ok) throw new RankingRequestError(data.message ?? errorMessages[data.code] ?? (response.status === 409 ? '成绩已变化，请刷新后核对再操作。' : '操作失败，请检查输入后重试。'), response.status === 409)
    return data as T
  }
  return {
    request,
    refresh: () => mutate(key => typeof key === 'string' && (key.startsWith(base) || key.startsWith('/api/learning-leaderboard') || key.startsWith('/api/my-learning') || key.startsWith('/api/account/profile'))),
    exportCsv: async () => {
      const response = await fetch(`${base}/export`, { credentials: 'include' })
      if (!response.ok) throw new Error('导出失败，请刷新后重试。')
      const url = URL.createObjectURL(await response.blob())
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = 'training-rankings.csv'; anchor.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    },
  }
}
