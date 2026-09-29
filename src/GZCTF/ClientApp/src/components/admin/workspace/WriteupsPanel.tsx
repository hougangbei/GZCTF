import { Alert, Anchor, Badge, Button, Group, Modal, Paper, SegmentedControl, Stack, Text } from '@mantine/core'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { ContentType, fetcher } from '@Api'
import { PDFViewer } from '@Components/admin/PDFViewer'

type ReviewStatus = 'pending' | 'approved' | 'rejected'
type ReviewItem = {
  id: string
  challengeId: string
  challengeTitle: string
  title: string
  authorName: string
  fileSize: number
  status: ReviewStatus | number
  createdAtUtc: string
}

export const WriteupsPanel = () => {
  const { t } = useTranslation('skillTrees')
  const [status, setStatus] = useState<ReviewStatus>('pending')
  const [selected, setSelected] = useState<ReviewItem>()
  const [busyId, setBusyId] = useState<string>()
  const [error, setError] = useState(false)
  const { data, error: loadError, mutate } = useSWR<ReviewItem[]>(
    `/api/admin/community-writeups?status=${status}`, fetcher
  )

  const review = async (item: ReviewItem, approved: boolean) => {
    setBusyId(item.id)
    setError(false)
    try {
      await api.request({
        path: `/api/admin/community-writeups/${item.id}/review`,
        method: 'POST', type: ContentType.Json, body: { approved },
      })
      setSelected(undefined)
      await mutate()
    } catch {
      setError(true)
    } finally {
      setBusyId(undefined)
    }
  }

  return (
    <Stack gap="md">
      <SegmentedControl value={status} onChange={(value) => setStatus(value as ReviewStatus)}
        data={(['pending', 'approved', 'rejected'] as const).map((value) => ({ value, label: t(`writeups.${value}`) }))} />
      {(loadError || error) && <Alert color="red">{t('writeups.reviewFailed')}</Alert>}
      {data?.length === 0 && <Text c="dimmed">{t('writeups.empty')}</Text>}
      {data?.map((item) => (
        <Paper key={item.id} withBorder p="md" radius="md">
          <Group justify="space-between" align="flex-start" wrap="wrap">
            <Stack gap={4}>
              <Text fw={600}>{item.title}</Text>
              <Text size="sm" c="dimmed">{t('writeups.challenge')}: {item.challengeTitle}</Text>
              <Text size="sm" c="dimmed">{t('writeups.by', { author: item.authorName })}</Text>
              <Text size="xs" c="dimmed">{t('writeups.submittedAt')}: {new Date(item.createdAtUtc).toLocaleString()}</Text>
            </Stack>
            <Group gap="xs">
              <Badge variant="light">{t(`writeups.${status}`)}</Badge>
              <Button variant="light" onClick={() => setSelected(item)}>{t('writeups.preview')}</Button>
              {status !== 'approved' && <Button loading={busyId === item.id}
                onClick={() => void review(item, true)}>{t('writeups.approve')}</Button>}
              {status !== 'rejected' && <Button color="red" variant="light" loading={busyId === item.id}
                onClick={() => void review(item, false)}>{t('writeups.reject')}</Button>}
            </Group>
          </Group>
        </Paper>
      ))}
      <Modal opened={!!selected} onClose={() => setSelected(undefined)} title={selected?.title}
        size="xl" centered>
        {selected && <Stack>
          <PDFViewer url={`/api/challenges/${selected.challengeId}/community-writeups/${selected.id}/pdf`} height={600} />
          <Group justify="flex-end">
            <Anchor href={`/api/challenges/${selected.challengeId}/community-writeups/${selected.id}/pdf`}
              target="_blank" rel="noopener noreferrer">{t('writeups.preview')}</Anchor>
            <Button onClick={() => void review(selected, true)} loading={busyId === selected.id}>{t('writeups.approve')}</Button>
            <Button color="red" variant="light" onClick={() => void review(selected, false)}
              loading={busyId === selected.id}>{t('writeups.reject')}</Button>
          </Group>
        </Stack>}
      </Modal>
    </Stack>
  )
}
