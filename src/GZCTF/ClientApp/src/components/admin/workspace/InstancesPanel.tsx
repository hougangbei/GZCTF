import { Alert, Button, Group, NumberInput, Paper, ScrollArea, Stack, Table, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'

type LearningInstance = {
  id: string
  userName: string
  challengeTitle: string
  startedAtUtc?: string
  expiresAtUtc?: string
  publicIp?: string
  publicPort?: number
}

type InstanceSettings = {
  maxConcurrentPerUser: number
  lifetimeMinutes: number
}

export const InstancesPanel = () => {
  const { t } = useTranslation('skillTrees')
  const { data: instances, error: loadError, mutate: mutateInstances } =
    useSWR<LearningInstance[]>('/api/admin/challenge-instances', fetcher, { refreshInterval: 30000 })
  const { data: settings, error: settingsError, mutate: mutateSettings } =
    useSWR<InstanceSettings>('/api/admin/challenge-instances/settings', fetcher)
  const [maxConcurrentPerUser, setMaxConcurrentPerUser] = useState<number | string>(2)
  const [lifetimeMinutes, setLifetimeMinutes] = useState<number | string>(120)
  const [busyId, setBusyId] = useState<string>()
  const [saving, setSaving] = useState(false)
  const [message, setMessage] = useState<'saved' | 'failed'>()

  useEffect(() => {
    if (!settings) return
    setMaxConcurrentPerUser(settings.maxConcurrentPerUser)
    setLifetimeMinutes(settings.lifetimeMinutes)
  }, [settings])

  const save = async () => {
    if (typeof maxConcurrentPerUser !== 'number' || typeof lifetimeMinutes !== 'number') return
    setSaving(true)
    setMessage(undefined)
    try {
      const result = await api.request<InstanceSettings>({
        path: '/api/admin/challenge-instances/settings', method: 'PUT',
        type: ContentType.Json, body: { maxConcurrentPerUser, lifetimeMinutes }, format: 'json',
      })
      await mutateSettings(result.data, false)
      setMessage('saved')
    } catch {
      setMessage('failed')
    } finally {
      setSaving(false)
    }
  }

  const stop = (id: string) => modals.openConfirmModal({
    title: t('instances.stop'),
    children: <Text size="sm">{t('instances.stop')}?</Text>,
    labels: { confirm: t('instances.stop'), cancel: t('delete.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => {
      setBusyId(id)
      setMessage(undefined)
      void (async () => {
        try {
          await api.request({ path: `/api/admin/challenge-instances/${id}`, method: 'DELETE' })
          await mutateInstances()
        } catch {
          setMessage('failed')
        } finally {
          setBusyId(undefined)
        }
      })()
    },
  })

  const formatDate = (value?: string) => value ? new Date(value).toLocaleString() : '—'
  const valid = typeof maxConcurrentPerUser === 'number' && maxConcurrentPerUser >= 1 &&
    maxConcurrentPerUser <= 20 && Number.isInteger(maxConcurrentPerUser) &&
    typeof lifetimeMinutes === 'number' && lifetimeMinutes >= 10 &&
    lifetimeMinutes <= 1440 && Number.isInteger(lifetimeMinutes)

  return (
    <Stack gap="lg">
      {(loadError || settingsError || message === 'failed') &&
        <Alert color="red">{t('instances.saveFailed')}</Alert>}
      {message === 'saved' && <Alert color="teal">{t('instances.saved')}</Alert>}
      <Paper withBorder p="md">
        <Stack gap="md">
          <Title order={3}>{t('instances.settings')}</Title>
          <Group align="end" wrap="wrap">
            <NumberInput label={t('instances.maxConcurrentPerUser')} min={1} max={20}
              value={maxConcurrentPerUser} onChange={setMaxConcurrentPerUser} allowDecimal={false} />
            <NumberInput label={t('instances.lifetimeMinutes')} min={10} max={1440}
              value={lifetimeMinutes} onChange={setLifetimeMinutes} allowDecimal={false} />
            <Button onClick={() => void save()} loading={saving} disabled={!valid || !settings}>
              {t('instances.save')}
            </Button>
          </Group>
          <Text size="sm" c="dimmed">{t('instances.settingsHint')}</Text>
        </Stack>
      </Paper>
      <Paper withBorder p="md">
        <Stack>
          <Title order={3}>{t('instances.learning')}</Title>
          {instances?.length === 0 && <Text c="dimmed">{t('instances.empty')}</Text>}
          {!!instances?.length && <ScrollArea>
            <Table miw={720}>
              <Table.Thead><Table.Tr>
                <Table.Th>{t('instances.user')}</Table.Th>
                <Table.Th>{t('instances.challenge')}</Table.Th>
                <Table.Th>{t('instances.started')}</Table.Th>
                <Table.Th>{t('instances.expires')}</Table.Th>
                <Table.Th>{t('instances.address')}</Table.Th>
                <Table.Th />
              </Table.Tr></Table.Thead>
              <Table.Tbody>{instances.map((item) => <Table.Tr key={item.id}>
                <Table.Td>{item.userName}</Table.Td>
                <Table.Td>{item.challengeTitle}</Table.Td>
                <Table.Td>{formatDate(item.startedAtUtc)}</Table.Td>
                <Table.Td>{formatDate(item.expiresAtUtc)}</Table.Td>
                <Table.Td>{item.publicIp ? `${item.publicIp}${item.publicPort ? `:${item.publicPort}` : ''}` : '—'}</Table.Td>
                <Table.Td><Button color="red" variant="light" size="xs" loading={busyId === item.id}
                  onClick={() => stop(item.id)}>{t('instances.stop')}</Button></Table.Td>
              </Table.Tr>)}</Table.Tbody>
            </Table>
          </ScrollArea>}
        </Stack>
      </Paper>
    </Stack>
  )
}
