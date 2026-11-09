import { mergeChallengeLocalization } from '@Utils/LearningAdmin'
import {
  ChallengeCategory,
  ChallengeFlagKind,
  ChallengeType,
  Difficulty,
  type ChallengeCommand,
  type ChallengeEditResponse,
} from '@Api'

export type AttachmentEntry = {
  [key: string]: unknown
  FileName: string
  Sha256: string
  StorageKey: string
  Flag: string
}

export type ContainerFields = {
  ContainerImage: string
  ExposedPort: number
  Cpu: number
  MemoryMb: number
  StorageMb: number
  NetworkMode: string
  FlagTemplate: string
}

export type ChallengeDraft = {
  title: string
  summary: string
  body: string
  type: ChallengeType
  ctfCategory: ChallengeCategory
  difficulty: Difficulty
  score: number
  expectedMinutes: number
  submissionLimit: number
  isEnabled: boolean
  container: ContainerFields
  attachments: AttachmentEntry[]
  staticFlags: string[]
  hints: string[]
  writeup: string
}

export const isContainerType = (type: ChallengeType) =>
  type === ChallengeType.StaticContainer || type === ChallengeType.DynamicContainer

export const isAttachmentType = (type: ChallengeType) =>
  type === ChallengeType.StaticAttachment || type === ChallengeType.DynamicAttachment

const defaultContainer: ContainerFields = {
  ContainerImage: '',
  ExposedPort: 80,
  Cpu: 1,
  MemoryMb: 256,
  StorageMb: 512,
  NetworkMode: 'Open',
  FlagTemplate: 'flag{[GUID]}',
}

const parseConfig = (value?: string | null): Record<string, unknown> => {
  if (!value) return {}
  try {
    const parsed: unknown = JSON.parse(value)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

const runtimeValue = (config: Record<string, unknown>, field: string): unknown =>
  Object.entries(config).find(([key]) => key.toLowerCase() === field.toLowerCase())?.[1]

const editableRuntimeFields = new Set(['image', ...Object.keys(defaultContainer).map((key) => key.toLowerCase())])

const parseAttachments = (value?: string | null): AttachmentEntry[] => {
  if (!value) return []
  try {
    const parsed: unknown = JSON.parse(value)
    const entries = Array.isArray(parsed)
      ? parsed
      : parsed && typeof parsed === 'object'
        ? runtimeValue(parsed as Record<string, unknown>, 'Attachments')
        : []
    if (!Array.isArray(entries)) return []
    return entries.map((entry) => {
      const item = entry as Record<string, unknown>
      return {
        ...Object.fromEntries(
          Object.entries(item).filter(
            ([key]) => !['filename', 'sha256', 'storagekey', 'flag'].includes(key.toLowerCase())
          )
        ),
        FileName: String(item.FileName ?? item.fileName ?? ''),
        Sha256: String(item.Sha256 ?? item.sha256 ?? ''),
        StorageKey: String(item.StorageKey ?? item.storageKey ?? item.FileName ?? ''),
        Flag: String(item.Flag ?? item.flag ?? ''),
      }
    })
  } catch {
    return []
  }
}

export const challengeDraftFromResponse = (data: ChallengeEditResponse): ChallengeDraft => {
  const text = data.localizations?.find((item) => item.locale?.toLowerCase() === 'en')
  const type = data.challenge?.type ?? ChallengeType.StaticAttachment
  const config = parseConfig(data.runtimeConfigurationJson)
  const dynamicPool = data.flags?.find((flag) => flag.kind === ChallengeFlagKind.DynamicAttachment)
  const template = data.flags?.find((flag) => flag.kind === ChallengeFlagKind.Template)?.template
  return {
    title: text?.title ?? data.challenge?.title ?? '',
    summary: text?.summary ?? data.challenge?.summary ?? '',
    body: text?.body ?? '',
    type,
    ctfCategory: data.challenge?.ctfCategory ?? ChallengeCategory.Misc,
    difficulty: data.challenge?.difficulty ?? Difficulty.Normal,
    score: data.challenge?.score ?? 100,
    expectedMinutes: data.challenge?.expectedMinutes ?? 60,
    submissionLimit: data.challenge?.submissionLimit ?? 0,
    isEnabled: data.challenge?.isEnabled ?? true,
    container: {
      ContainerImage: String(runtimeValue(config, 'ContainerImage') ?? runtimeValue(config, 'Image') ?? ''),
      ExposedPort: Number(runtimeValue(config, 'ExposedPort') ?? 80),
      Cpu: Number(runtimeValue(config, 'Cpu') ?? 1),
      MemoryMb: Number(runtimeValue(config, 'MemoryMb') ?? 256),
      StorageMb: Number(runtimeValue(config, 'StorageMb') ?? 512),
      NetworkMode:
        ['Open', 'Isolated', 'Custom'].find(
          (mode) => mode.toLowerCase() === String(runtimeValue(config, 'NetworkMode') ?? '').toLowerCase()
        ) ?? 'Open',
      FlagTemplate: String(runtimeValue(config, 'FlagTemplate') || template || defaultContainer.FlagTemplate),
    },
    attachments: parseAttachments(dynamicPool?.metadataJson ?? data.runtimeConfigurationJson),
    staticFlags:
      data.flags?.filter((flag) => flag.kind === ChallengeFlagKind.Static).map((flag) => flag.value ?? '') ?? [],
    hints:
      data.hints
        ?.filter((hint) => hint.locale?.toLowerCase() === 'en')
        .sort((left, right) => (left.sortOrder ?? 0) - (right.sortOrder ?? 0))
        .map((hint) => hint.content ?? '') ?? [],
    writeup: data.writeups?.find((item) => item.locale?.toLowerCase() === 'en')?.content ?? '',
  }
}

export const challengeCommandFromDraft = (
  draft: ChallengeDraft,
  current: ChallengeEditResponse,
  rowVersion: number | undefined,
  fillEmptyLocalizations = false
): ChallengeCommand => {
  const attachments = draft.attachments.map((entry) => ({
    ...entry,
    Flag:
      draft.type === ChallengeType.StaticAttachment && !entry.ResourceOnly ? (draft.staticFlags[0] ?? '') : entry.Flag,
  }))
  const originalConfig = parseConfig(current.runtimeConfigurationJson)
  const currentPool = current.flags?.find((flag) => flag.kind === ChallengeFlagKind.DynamicAttachment)
  const originalPool = parseAttachments(currentPool?.metadataJson ?? current.runtimeConfigurationJson)
  const attachmentKey = (entry: AttachmentEntry) =>
    JSON.stringify([entry.StorageKey, entry.Sha256, entry.FileName, entry.Flag])
  const poolKeys = new Set(originalPool.map(attachmentKey))
  const supplemental =
    draft.type === ChallengeType.DynamicAttachment
      ? parseAttachments(current.runtimeConfigurationJson).filter((entry) => !poolKeys.has(attachmentKey(entry)))
      : []
  const runtimeAttachments = [...attachments, ...supplemental]
  const rootWithoutAttachments = Object.fromEntries(
    Object.entries(originalConfig).filter(([key]) => key.toLowerCase() !== 'attachments')
  )
  const flags =
    draft.type === ChallengeType.DynamicContainer
      ? [{ kind: ChallengeFlagKind.Template, template: draft.container.FlagTemplate }]
      : draft.type === ChallengeType.DynamicAttachment
        ? [{ kind: ChallengeFlagKind.DynamicAttachment, metadataJson: JSON.stringify(attachments) }]
        : draft.staticFlags
            .filter((value) => value.trim())
            .map((value) => {
              const original = current.flags?.find(
                (flag) => flag.kind === ChallengeFlagKind.Static && flag.value === value
              )
              return {
                kind: ChallengeFlagKind.Static,
                value,
                ...(original?.metadataJson ? { metadataJson: original.metadataJson } : {}),
              }
            })
  const localizations = mergeChallengeLocalization(current.localizations ?? [], {
    locale: 'en',
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    body: draft.body,
  })
  return {
    rowVersion,
    type: draft.type,
    ctfCategory: draft.ctfCategory,
    difficulty: draft.difficulty,
    score: draft.score,
    isEnabled: draft.isEnabled,
    expectedMinutes: draft.expectedMinutes,
    submissionLimit: draft.submissionLimit,
    runtimeConfigurationJson: isContainerType(draft.type)
      ? JSON.stringify({
          ...Object.fromEntries(
            Object.entries(parseConfig(current.runtimeConfigurationJson)).filter(
              ([key]) => !editableRuntimeFields.has(key.toLowerCase())
            )
          ),
          ...draft.container,
        })
      : JSON.stringify({ ...rootWithoutAttachments, Attachments: runtimeAttachments }),
    localizations: fillEmptyLocalizations
      ? localizations.map((text) => ({
          ...text,
          title: text.title?.trim() ? text.title : draft.title.trim(),
          body: text.body?.trim() ? text.body : draft.body,
        }))
      : localizations,
    flags,
    hints: draft.hints
      .filter((content) => content.trim())
      .map((content, sortOrder) => ({
        locale: 'en',
        sortOrder,
        content,
      })),
    writeups: draft.writeup.trim() ? [{ locale: 'en', content: draft.writeup }] : [],
  }
}

export const emptyOtherChallengeLocales = (data: ChallengeEditResponse) => [
  ...new Set(
    (data.localizations ?? [])
      .filter((text) => text.locale?.toLowerCase() !== 'en' && (!text.title?.trim() || !text.body?.trim()))
      .map((text) => text.locale ?? 'unknown')
  ),
]
