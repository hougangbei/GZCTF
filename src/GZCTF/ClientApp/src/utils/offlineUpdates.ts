export type OfflinePackage = {
  id: string
  name: string
  size: number
  modifiedAtUtc: string
  source: 'directory' | 'upload'
  sha256?: string
  checksumProvided: boolean
}
export type OfflineSettings = { mode: 'offline'; directory: string; maxUploadBytes: number; allowedRoots: string[] }
export type OfflineValidation = {
  id: string
  packageId: string
  sourceDigest: string
  artifactHash: string
  version: string
  targetImage?: string
  expiresAtUtc: string
  canApply: boolean
  checks: { key: string; label: string; status: 'passed' | 'failed' | 'running'; message: string }[]
}
export type OfflineStatus = {
  mode: 'offline'
  configured: boolean
  phase: string
  message?: string
  currentSha?: string
  currentImage?: string
  backupDirectory?: string
  validation?: OfflineValidation
  backupPath?: string
  operationId?: string
  packageId?: string
}
export const offlineBusy = (phase?: string) =>
  ['validating', 'accepted', 'pulling', 'importing', 'backingUp', 'stopping', 'starting', 'recovering'].includes(
    phase ?? ''
  )
export const offlineBlocked = (phase?: string) => ['interrupted', 'recoveryRequired'].includes(phase ?? '')
export const validPackageName = (name: string) =>
  name.length <= 180 && !/[\x00-\x1f\x7f/\\:]/.test(name) && /\.(tar\.gz|tgz|tar|zip)$/i.test(name)
export function checksumValue(content: string): string | undefined {
  const match = content.trim().match(/^([a-f\d]{64})(?:[ \t]+\*?[^\r\n]+)?$/i)
  return match?.[1].toLowerCase()
}
export function applicablePackage(
  status: OfflineStatus | undefined,
  packageId: string | undefined,
  now: number
): boolean {
  const report = status?.validation
  return (
    status?.phase === 'ready' &&
    !!report?.canApply &&
    report.packageId === packageId &&
    /^[a-f\d]{32}$/.test(report.id) &&
    Number.isFinite(Date.parse(report.expiresAtUtc)) &&
    Date.parse(report.expiresAtUtc) > now
  )
}
