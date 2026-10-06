import api from '@Api'

export type LogPage<T> = { items: T[]; total: number; page: number; pageSize: number }

export type AuditEvent = {
  id: string
  occurredAtUtc: string
  actorId: string | null
  actorName: string
  actorKind: string
  category: string
  action: string
  targetType: string
  targetId: string | null
  targetName: string | null
  succeeded: boolean
  httpStatus: number
  errorCode: string | null
  errorReason: string | null
  requestId: string
  affectedCount: number | null
}

export type FlagAttempt = {
  id: string
  occurredAtUtc: string
  userId: string
  userName: string
  challengeId: string
  challengeName: string | null
  submissionId: string | null
  outcome: string
  rejectionCode: string | null
}

export type FlagAttemptDetail = FlagAttempt & {
  originalAvailable: boolean
  submittedFlag: string | null
}

export type SystemLog = {
  id: number
  time: string
  name: string | null
  level: string | null
  ip: string | null
  msg: string | null
  source: string | null
  exception: string | null
  status: string | null
}

type PageQuery = { page: number; pageSize: number }

export const adminLogsApi = {
  audit: (query: PageQuery & { category?: string; succeeded?: boolean; search?: string }) =>
    api.request<LogPage<AuditEvent>>({
      path: '/api/admin/audit-events',
      query,
      format: 'json',
    }),
  auditDetail: (id: string) => api.request<AuditEvent>({ path: `/api/admin/audit-events/${id}`, format: 'json' }),
  flagAttempts: (query: PageQuery & { outcome?: string; search?: string }) =>
    api.request<LogPage<FlagAttempt>>({ path: '/api/admin/flag-attempts', query, format: 'json' }),
  flagAttemptDetail: (id: string) => api.request<FlagAttemptDetail>({ path: `/api/admin/flag-attempts/${id}`, format: 'json' }),
  system: (query: PageQuery & { level: string }) =>
    api.request<LogPage<SystemLog>>({ path: '/api/admin/logs', query, format: 'json' }),
}
