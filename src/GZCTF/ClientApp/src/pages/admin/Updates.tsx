import { Alert, Anchor, Badge, Button, Group, Paper, Stack, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'
import { AdminPage } from '@Components/admin/AdminPage'

type UpdateStatus = {
  configured: boolean
  currentSha?: string | null
  latestSha?: string | null
  latestUrl?: string | null
  publishedAtUtc?: string | null
  updateAvailable?: boolean
  checkError?: string | null
  phase: string
  message?: string | null
  targetSha?: string | null
  backupPath?: string | null
  changedAtUtc?: string | null
}

const activePhases = ['accepted', 'pulling', 'backingUp', 'starting']

const Updates = () => {
  const { t } = useTranslation('admin')
  const { data, error, mutate, isLoading } = useSWR<UpdateStatus>(
    '/api/admin/updates', fetcher, { refreshInterval: 5000 }
  )
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string>()
  const [refreshing, setRefreshing] = useState(false)
  const active = !!data && activePhases.includes(data.phase)
  const canUpdate = !!data?.configured && !!data.updateAvailable && !!data.currentSha &&
    !!data.latestSha && !active && data.phase !== 'interrupted' &&
    !(data.phase === 'failed' && data.backupPath)

  const apply = async () => {
    if (!data?.latestSha) return
    setSubmitting(true)
    setSubmitError(undefined)
    try {
      await api.request({ path: '/api/admin/updates', method: 'POST', type: ContentType.Json,
        body: { targetSha: data.latestSha } })
      await mutate()
    } catch {
      setSubmitError(t('updates.applyFailed'))
    } finally {
      setSubmitting(false)
    }
  }

  const confirmUpdate = () => modals.openConfirmModal({
    title: t('updates.confirmTitle'),
    children: <Text size="sm">{t('updates.confirmBody', { sha: data?.latestSha?.slice(0, 12) })}</Text>,
    labels: { confirm: t('updates.apply'), cancel: t('updates.cancel') },
    confirmProps: { color: 'orange' },
    onConfirm: () => { void apply() },
  })

  const refresh = async () => {
    setRefreshing(true)
    try {
      const response = await api.request<UpdateStatus>({ path: '/api/admin/updates',
        method: 'GET', query: { refresh: true }, format: 'json' })
      await mutate(response.data, false)
    } catch {
      setSubmitError(t('updates.loadFailed'))
    } finally {
      setRefreshing(false)
    }
  }

  return <AdminPage minWidth={390} isLoading={isLoading}>
    <Stack w="100%" maw={900} gap="md">
      <Title order={2}>{t('updates.title')}</Title>
      <Text c="dimmed">{t('updates.description')}</Text>
      {error && <Alert color="red">{t('updates.loadFailed')}</Alert>}
      {submitError && <Alert color="red">{submitError}</Alert>}
      {data && !data.configured && <Alert color="yellow">{t('updates.notConfigured')}</Alert>}
      {data?.configured && <>
        {data.checkError && <Alert color="yellow">{data.checkError}</Alert>}
        <Paper withBorder p="lg" radius="md">
          <Stack gap="sm">
            <Group justify="space-between"><Text fw={600}>{t('updates.current')}</Text>
              <Text ff="monospace">{data.currentSha?.slice(0, 12) ?? t('updates.unknown')}</Text></Group>
            <Group justify="space-between"><Text fw={600}>{t('updates.latest')}</Text>
              <Text ff="monospace">{data.latestSha?.slice(0, 12) ?? t('updates.unknown')}</Text></Group>
            {data.publishedAtUtc && <Text size="sm" c="dimmed">{t('updates.publishedAt')}: {new Date(data.publishedAtUtc).toLocaleString()}</Text>}
            {data.latestUrl && <Anchor href={data.latestUrl} target="_blank" rel="noopener noreferrer">{t('updates.workflow')}</Anchor>}
            <Group><Badge color={data.updateAvailable ? 'blue' : 'gray'}>
              {data.updateAvailable ? t('updates.available') : t('updates.noUpdate')}
            </Badge><Button variant="default" loading={refreshing} onClick={() => void refresh()}>
              {t('updates.refresh')}
            </Button></Group>
          </Stack>
        </Paper>
        <Paper withBorder p="lg" radius="md">
          <Stack gap="sm">
            <Text fw={600}>{t('updates.task')}</Text>
            <Text>{t(`updates.phases.${data.phase}`, { defaultValue: data.phase })}</Text>
            {data.message && <Alert color={data.phase === 'failed' || data.phase === 'interrupted' ? 'red' : 'blue'}>{data.message}</Alert>}
            {data.targetSha && <Text size="sm">{t('updates.target')}: {data.targetSha.slice(0, 12)}</Text>}
            {data.backupPath && <Text size="sm">{t('updates.backup')}: {data.backupPath}</Text>}
            {data.changedAtUtc && <Text size="sm" c="dimmed">{t('updates.changedAt')}: {new Date(data.changedAtUtc).toLocaleString()}</Text>}
          </Stack>
        </Paper>
        <Alert color="orange">{t('updates.warning')}</Alert>
        <Group justify="flex-end"><Button disabled={!canUpdate} loading={submitting}
          onClick={confirmUpdate}>{t('updates.apply')}</Button></Group>
      </>}
    </Stack>
  </AdminPage>
}

export default Updates
