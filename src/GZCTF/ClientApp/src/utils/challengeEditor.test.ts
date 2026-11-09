import { expect, it } from 'vitest'
import { ChallengeFlagKind, ChallengeType, type ChallengeEditResponse } from '@Api'
import { challengeCommandFromDraft, challengeDraftFromResponse } from './challengeEditor'

const edit = (configuration: Record<string, unknown>, type = ChallengeType.DynamicContainer): ChallengeEditResponse =>
  ({
    challenge: { id: 'challenge', type, title: 'Imported challenge', isEnabled: true },
    localizations: [{ locale: 'en', title: 'Imported challenge', body: 'Description' }],
    runtimeConfigurationJson: JSON.stringify(configuration),
    flags: [],
    hints: [],
    writeups: [],
  }) as unknown as ChallengeEditResponse

it.each(['gzctf', 'ctfd-whale', 'gzctf-random'])(
  'editing an imported %s challenge preserves origin and attachment settings',
  (mode) => {
    const attachment = { FileName: 'download.zip', StorageKey: 'import/private', Sha256: 'abc', Flag: 'supplementary' }
    const original = edit({
      Image: 'fixture/image:old',
      FlagMode: mode,
      ImportSourceInstance: 'source',
      FlagTemplate: 'nynuctf{[GUID]}',
      Attachments: [attachment],
      NetworkMode: 'open',
    })
    const draft = challengeDraftFromResponse(original)
    draft.title = 'Edited description'
    draft.container.ContainerImage = 'fixture/image:new'
    const command = challengeCommandFromDraft(draft, original, 1)
    const config = JSON.parse(command.runtimeConfigurationJson!)
    expect(config).toMatchObject({
      ContainerImage: 'fixture/image:new',
      FlagMode: mode,
      ImportSourceInstance: 'source',
      FlagTemplate: 'nynuctf{[GUID]}',
      Attachments: [attachment],
    })
    expect(config.Image).toBeUndefined()
    expect(config.NetworkMode).toBe('Open')
  }
)

it('CTFd random challenges show the same editable GUID template as GZCTF', () => {
  const draft = challengeDraftFromResponse(
    edit({ Image: 'fixture/whale:latest', FlagMode: 'ctfd-whale', FlagTemplate: null })
  )
  expect(draft.container.FlagTemplate).toBe('flag{[GUID]}')
})

it('editing a static CTFd challenge keeps its original case-insensitive Flag rule', () => {
  const original = edit({ Image: 'fixture/whale:latest', FlagMode: 'ctfd-whale' }, ChallengeType.StaticContainer)
  original.flags = [{ kind: ChallengeFlagKind.Static, value: 'FiXtUrE', metadataJson: '{"IgnoreCase":true}' }]
  const draft = challengeDraftFromResponse(original)
  draft.body = 'Edited body'
  const command = challengeCommandFromDraft(draft, original, 1)
  expect(command.flags).toEqual([
    { kind: ChallengeFlagKind.Static, value: 'FiXtUrE', metadataJson: '{"IgnoreCase":true}' },
  ])
})

it('runtime field casing is accepted without retaining stale editable aliases', () => {
  const original = edit({
    image: 'fixture/old',
    networkmode: 'isolated',
    flagmode: 'gzctf',
    flagtemplate: 'flag{[GUID]}',
    exposedport: 8080,
  })
  const draft = challengeDraftFromResponse(original)
  expect(draft.container).toMatchObject({
    ContainerImage: 'fixture/old',
    NetworkMode: 'Isolated',
    ExposedPort: 8080,
    FlagTemplate: 'flag{[GUID]}',
  })
  draft.container.ExposedPort = 9000
  const config = JSON.parse(challengeCommandFromDraft(draft, original, 1).runtimeConfigurationJson!)
  expect(config).toMatchObject({ ExposedPort: 9000, flagmode: 'gzctf' })
  expect(config.exposedport).toBeUndefined()
})

it('static attachment edits preserve remote URLs, MIME type and body resource references', () => {
  const resource = {
    FileName: 'image.png',
    Sha256: 'abc',
    StorageKey: 'private/image',
    Flag: 'resource',
    ResourceOnly: true,
    ContentType: 'image/png',
  }
  const remote = {
    FileName: 'remote.zip',
    Sha256: '',
    StorageKey: '',
    Flag: 'flag{literal}',
    RemoteUrl: 'https://fixture.invalid/file.zip',
  }
  const original = edit({ Attachments: [resource, remote], SourceOption: 'keep' }, ChallengeType.StaticAttachment)
  original.flags = [{ kind: ChallengeFlagKind.Static, value: 'flag{literal}' }]
  const draft = challengeDraftFromResponse(original)
  draft.body = 'Updated body'
  const config = JSON.parse(challengeCommandFromDraft(draft, original, 1).runtimeConfigurationJson!)
  expect(config.SourceOption).toBe('keep')
  expect(config.Attachments).toEqual(expect.arrayContaining([resource, remote]))
})

it('dynamic pool edits preserve supplemental files and body resources outside the flag pool', () => {
  const pool = {
    FileName: 'challenge.zip',
    Sha256: 'pool',
    StorageKey: 'pool/file',
    Flag: 'flag{one}',
    DynamicPool: true,
    ContentType: 'application/zip',
  }
  const resource = {
    FileName: 'image.png',
    Sha256: 'body',
    StorageKey: 'body/image',
    Flag: 'supplementary',
    ResourceOnly: true,
    ContentType: 'image/png',
  }
  const supplement = {
    FileName: 'guide.txt',
    Sha256: 'guide',
    StorageKey: 'guide/file',
    Flag: 'supplementary',
    DynamicPool: false,
  }
  const original = edit({ Attachments: [pool, resource, supplement] }, ChallengeType.DynamicAttachment)
  original.flags = [{ kind: ChallengeFlagKind.DynamicAttachment, metadataJson: JSON.stringify([pool]) }]
  const draft = challengeDraftFromResponse(original)
  draft.body = 'Edited description'
  const command = challengeCommandFromDraft(draft, original, 1)
  const config = JSON.parse(command.runtimeConfigurationJson!)
  expect(config.Attachments).toEqual(expect.arrayContaining([pool, resource, supplement]))
  expect(JSON.parse(command.flags![0].metadataJson!)).toEqual([pool])
  draft.attachments = []
  const removed = JSON.parse(challengeCommandFromDraft(draft, original, 1).runtimeConfigurationJson!)
  expect(removed.Attachments).toEqual([resource, supplement])
})

it('an empty runtime template falls back to its canonical Flag template', () => {
  const original = edit({ FlagTemplate: '' })
  original.flags = [{ kind: ChallengeFlagKind.Template, template: 'nynuctf{[GUID]}' }]
  expect(challengeDraftFromResponse(original).container.FlagTemplate).toBe('nynuctf{[GUID]}')
})

it('explicit repair fills only blank localization fields without overwriting existing translations', () => {
  const original = edit({}, ChallengeType.StaticAttachment)
  original.localizations = [
    { locale: 'en', title: 'Original', body: '' },
    { locale: 'zh', title: '中文标题', body: '   ' },
    { locale: 'ja', title: 'Existing title', body: 'Existing translation' },
    { locale: 'fr', title: '', body: 'French body' },
  ]
  const draft = challengeDraftFromResponse(original)
  draft.title = 'Repaired title'
  draft.body = 'Repaired description'
  const ordinary = challengeCommandFromDraft(draft, original, 1)
  expect(ordinary.localizations?.find((l) => l.locale === 'zh')?.body).toBe('   ')
  const repaired = challengeCommandFromDraft(draft, original, 1, true)
  expect(repaired.localizations).toEqual([
    { locale: 'en', title: 'Repaired title', summary: '', body: 'Repaired description' },
    { locale: 'zh', title: '中文标题', body: 'Repaired description' },
    { locale: 'ja', title: 'Existing title', body: 'Existing translation' },
    { locale: 'fr', title: 'Repaired title', body: 'French body' },
  ])
  expect(original.localizations[1].body).toBe('   ')
})
