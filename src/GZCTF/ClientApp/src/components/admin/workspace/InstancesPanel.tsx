import { Alert, Button, Paper, ScrollArea, Stack, Table, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { fetcher } from '@Api'

type LearningInstance = {
  id: string
  userName: string
  challengeTitle: string
  status: string | number
  startedAtUtc?: string
  expiresAtUtc?: string
  publicIp?: string
  publicPort?: number
}

type GameInstance = {
  containerGuid: string
  team?: { name: string }
  challenge?: { title: string }
  startedAt?: string
  expectStopAt?: string
  ip?: string
  port?: number
}

export const InstancesPanel = () => {
  const { t } = useTranslation('skillTrees')
  const { data: learning, error: learningError, mutate: mutateLearning } =
    useSWR<LearningInstance[]>('/api/admin/challenge-instances', fetcher, { refreshInterval: 30000 })
  const { data: games, error: gamesError, mutate: mutateGames } =
    useSWR<{ data: GameInstance[] }>('/api/admin/instances', fetcher, { refreshInterval: 30000 })
  const [error, setError] = useState(false)
  const [busyId, setBusyId] = useState<string>()

  const stop = (id: string, kind: 'learning' | 'game') => modals.openConfirmModal({
    title: t('instances.stop'),
    children: <Text size="sm">{t('instances.stop')}?</Text>,
    labels: { confirm: t('instances.stop'), cancel: t('delete.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => {
      setBusyId(id)
      setError(false)
      void (async () => {
        try {
          if (kind === 'learning') {
            await api.request({ path: `/api/admin/challenge-instances/${id}`, method: 'DELETE' })
            await mutateLearning()
          } else {
            await api.admin.adminDestroyInstance(id)
            await mutateGames()
          }
        } catch {
          setError(true)
        } finally {
          setBusyId(undefined)
        }
      })()
    },
  })

  const formatDate = (value?: string) => value ? new Date(value).toLocaleString() : '—'

  return (
    <Stack gap="lg">
      {(learningError || gamesError || error) && <Alert color="red">{t('instances.stopFailed')}</Alert>}
      <Paper withBorder p="md">
        <Stack>
          <Title order={3}>{t('instances.learning')}</Title>
          {learning?.length === 0 && <Text c="dimmed">{t('instances.empty')}</Text>}
          {!!learning?.length && <ScrollArea>
            <Table miw={720}>
              <Table.Thead><Table.Tr>
                <Table.Th>{t('instances.user')}</Table.Th>
                <Table.Th>{t('instances.challenge')}</Table.Th>
                <Table.Th>{t('instances.started')}</Table.Th>
                <Table.Th>{t('instances.expires')}</Table.Th>
                <Table.Th>{t('instances.address')}</Table.Th>
                <Table.Th />
              </Table.Tr></Table.Thead>
              <Table.Tbody>{learning?.map((item) => <Table.Tr key={item.id}>
                <Table.Td>{item.userName}</Table.Td>
                <Table.Td>{item.challengeTitle}</Table.Td>
                <Table.Td>{formatDate(item.startedAtUtc)}</Table.Td>
                <Table.Td>{formatDate(item.expiresAtUtc)}</Table.Td>
                <Table.Td>{item.publicIp ? `${item.publicIp}${item.publicPort ? `:${item.publicPort}` : ''}` : '—'}</Table.Td>
                <Table.Td><Button color="red" variant="light" size="xs" loading={busyId === item.id}
                  onClick={() => stop(item.id, 'learning')}>{t('instances.stop')}</Button></Table.Td>
              </Table.Tr>)}</Table.Tbody>
            </Table>
          </ScrollArea>}
        </Stack>
      </Paper>
      <Paper withBorder p="md">
        <Stack>
          <Title order={3}>{t('instances.competition')}</Title>
          {games?.data?.length === 0 && <Text c="dimmed">{t('instances.empty')}</Text>}
          {!!games?.data?.length && <ScrollArea>
            <Table miw={720}>
              <Table.Thead><Table.Tr>
                <Table.Th>{t('instances.user')}</Table.Th>
                <Table.Th>{t('instances.challenge')}</Table.Th>
                <Table.Th>{t('instances.started')}</Table.Th>
                <Table.Th>{t('instances.expires')}</Table.Th>
                <Table.Th>{t('instances.address')}</Table.Th>
                <Table.Th />
              </Table.Tr></Table.Thead>
              <Table.Tbody>{games.data.map((item) => <Table.Tr key={item.containerGuid}>
                <Table.Td>{item.team?.name ?? '—'}</Table.Td>
                <Table.Td>{item.challenge?.title ?? '—'}</Table.Td>
                <Table.Td>{formatDate(item.startedAt)}</Table.Td>
                <Table.Td>{formatDate(item.expectStopAt)}</Table.Td>
                <Table.Td>{item.ip ? `${item.ip}${item.port ? `:${item.port}` : ''}` : '—'}</Table.Td>
                <Table.Td><Button color="red" variant="light" size="xs" loading={busyId === item.containerGuid}
                  onClick={() => stop(item.containerGuid, 'game')}>{t('instances.stop')}</Button></Table.Td>
              </Table.Tr>)}</Table.Tbody>
            </Table>
          </ScrollArea>}
        </Stack>
      </Paper>
    </Stack>
  )
}
