import { Alert, Button, Divider, Group, PasswordInput, Stack, Switch, Text, Textarea, TextInput, Title } from '@mantine/core'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'

type QqBotSettings = {
  enabled: boolean
  baseUrl: string
  accessTokenConfigured: boolean
  groupId: string
  messageTemplate: string
  notifyLearningSolves: boolean
  notifyGameSolves: boolean
  notifyChallengePublishes: boolean
  notifyAnnouncements: boolean
  notifyHints: boolean
}

export const QqBotSettingsPanel = () => {
  const { t } = useTranslation('skillTrees')
  const { data, mutate, error } = useSWR<QqBotSettings>('/api/admin/qq-bot/settings', fetcher)
  const [settings, setSettings] = useState<QqBotSettings>()
  const [accessToken, setAccessToken] = useState('')
  const [clearAccessToken, setClearAccessToken] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [result, setResult] = useState<'saved' | 'tested' | 'failed'>()

  useEffect(() => { if (data) setSettings(data) }, [data])
  const set = (patch: Partial<QqBotSettings>) =>
    setSettings((current) => current ? { ...current, ...patch } : current)
  const preview = settings?.messageTemplate
    .replaceAll('{member}', t('qqBot.sampleMember'))
    .replaceAll('{challenge}', t('qqBot.sampleChallenge'))
    .replaceAll('{source}', t('qqBot.sampleSource'))
    .replaceAll('{team}', t('qqBot.sampleTeam'))
    .replaceAll('{time}', new Date().toLocaleString())

  const save = async () => {
    if (!settings) return false
    setSaving(true)
    setResult(undefined)
    try {
      const response = await api.request<QqBotSettings>({
        path: '/api/admin/qq-bot/settings', method: 'PUT', type: ContentType.Json,
        body: { ...settings, accessToken: accessToken || null, clearAccessToken }, format: 'json',
      })
      await mutate(response.data, false)
      setAccessToken('')
      setClearAccessToken(false)
      setResult('saved')
      return true
    } catch {
      setResult('failed')
      return false
    } finally {
      setSaving(false)
    }
  }

  const test = async () => {
    setTesting(true)
    setResult(undefined)
    try {
      if (!await save()) return
      await api.request({ path: '/api/admin/qq-bot/test', method: 'POST' })
      setResult('tested')
    } catch {
      setResult('failed')
    } finally {
      setTesting(false)
    }
  }

  return <Stack gap="sm">
    <Title order={2}>{t('qqBot.title')}</Title>
    <Divider />
    <Text size="sm" c="dimmed">{t('qqBot.description')}</Text>
    {(error || result === 'failed') && <Alert color="red">{t('qqBot.failed')}</Alert>}
    {result === 'saved' && <Alert color="teal">{t('qqBot.saved')}</Alert>}
    {result === 'tested' && <Alert color="teal">{t('qqBot.tested')}</Alert>}
    <Switch label={t('qqBot.enabled')} checked={settings?.enabled ?? false}
      onChange={(event) => set({ enabled: event.currentTarget.checked })} />
    <Group grow align="start">
      <TextInput label={t('qqBot.baseUrl')} placeholder="http://napcat:3000"
        value={settings?.baseUrl ?? ''} onChange={(event) => set({ baseUrl: event.currentTarget.value })} />
      <TextInput label={t('qqBot.groupId')} value={settings?.groupId ?? ''}
        onChange={(event) => set({ groupId: event.currentTarget.value })} />
    </Group>
    <PasswordInput label={t('qqBot.accessToken')} value={accessToken}
      placeholder={settings?.accessTokenConfigured ? t('qqBot.tokenSaved') : ''}
      onChange={(event) => setAccessToken(event.currentTarget.value)} />
    {settings?.accessTokenConfigured && <Switch label={t('qqBot.clearToken')}
      checked={clearAccessToken} onChange={(event) => setClearAccessToken(event.currentTarget.checked)} />}
    <Group>
      <Switch label={t('qqBot.learningSolves')} checked={settings?.notifyLearningSolves ?? true}
        onChange={(event) => set({ notifyLearningSolves: event.currentTarget.checked })} />
      <Switch label={t('qqBot.gameSolves')} checked={settings?.notifyGameSolves ?? true}
        onChange={(event) => set({ notifyGameSolves: event.currentTarget.checked })} />
    </Group>
    <Group>
      <Switch label={t('qqBot.challengePublishes')} checked={settings?.notifyChallengePublishes ?? true}
        onChange={(event) => set({ notifyChallengePublishes: event.currentTarget.checked })} />
      <Switch label={t('qqBot.announcements')} checked={settings?.notifyAnnouncements ?? true}
        onChange={(event) => set({ notifyAnnouncements: event.currentTarget.checked })} />
      <Switch label={t('qqBot.hints')} checked={settings?.notifyHints ?? true}
        onChange={(event) => set({ notifyHints: event.currentTarget.checked })} />
    </Group>
    <Textarea label={t('qqBot.template')} description={t('qqBot.placeholders')}
      minRows={3} maxLength={1000} value={settings?.messageTemplate ?? ''}
      onChange={(event) => set({ messageTemplate: event.currentTarget.value })} />
    <Text size="sm" c="dimmed">{t('qqBot.preview')}: {preview}</Text>
    <Group>
      <Button onClick={() => void save()} loading={saving} disabled={!settings}>{t('qqBot.save')}</Button>
      <Button variant="light" onClick={() => void test()} loading={testing}
        disabled={!settings?.baseUrl || !settings?.groupId}>{t('qqBot.test')}</Button>
    </Group>
  </Stack>
}
