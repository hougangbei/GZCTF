import { Alert, Anchor, Badge, Button, Group, Modal, Paper, SegmentedControl, Stack, Text } from '@mantine/core'
import { modals } from '@mantine/modals'
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
  reviewedAtUtc?: string
}

const pageSize = 50

export const WriteupsPanel = ({ mode = 'review' }: { mode?: 'review' | 'management' }) => {
  const { t } = useTranslation('skillTrees')
  const [reviewStatus, setReviewStatus] = useState<'pending' | 'rejected'>('pending')
  const [page, setPage] = useState(0)
  const [selected, setSelected] = useState<ReviewItem>()
  const [busyId, setBusyId] = useState<string>()
  const [error, setError] = useState(false)
  const status: ReviewStatus = mode === 'management' ? 'approved' : reviewStatus
  const { data, error: loadError, mutate } = useSWR<ReviewItem[]>(
    `/api/admin/community-writeups?status=${status}&offset=${page * pageSize}&limit=${pageSize + 1}`,
    fetcher
  )
  const items = data?.slice(0, pageSize)

  const review = async (item: ReviewItem, approved: boolean) => {
    setBusyId(item.id)
    setError(false)
    try {
      await api.request({
        path: `/api/admin/community-writeups/${item.id}/review`,
        method: 'POST', type: ContentType.Json, body: { approved },
      })
      setSelected(undefined)
      const updated = await mutate()
      if (page > 0 && updated?.length === 0) setPage(page - 1)
    } catch {
      setError(true)
    } finally {
      setBusyId(undefined)
    }
  }

  const takeDown = (item: ReviewItem) => modals.openConfirmModal({
    title: t('writeups.takeDown'),
    children: <Text size="sm">{t('writeups.takeDownConfirm', { title: item.title })}</Text>,
    labels: { confirm: t('writeups.takeDown'), cancel: t('writeups.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => { void review(item, false) },
  })

  return (
    <Stack gap="md">
      {mode === 'review' ? <SegmentedControl value={reviewStatus} onChange={(value) => {
        setReviewStatus(value as 'pending' | 'rejected')
        setPage(0)
      }} data={(['pending', 'rejected'] as const).map((value) => ({
        value, label: t(`writeups.${value}`),
      }))} /> : <Text c="dimmed" size="sm">{t('writeups.managementHint')}</Text>}
      {(loadError || error) && <Alert color="red">{t('writeups.reviewFailed')}</Alert>}
      {data?.length === 0 && <Text c="dimmed">{t(mode === 'management' ? 'writeups.noApproved' : 'writeups.noReviews')}</Text>}
      {items?.map((item) => (
        <Paper key={item.id} withBorder p="md" radius="md">
          <Group justify="space-between" align="flex-start" wrap="wrap">
            <Stack gap={4}>
              <Text fw={600}>{item.title}</Text>
              <Text size="sm" c="dimmed">{t('writeups.challenge')}: {item.challengeTitle}</Text>
              <Text size="sm" c="dimmed">{t('writeups.by', { author: item.authorName })}</Text>
              <Text size="xs" c="dimmed">{t('writeups.submittedAt')}: {new Date(item.createdAtUtc).toLocaleString()}</Text>
              {mode === 'management' && item.reviewedAtUtc &&
                <Text size="xs" c="dimmed">{t('writeups.approvedAt')}: {new Date(item.reviewedAtUtc).toLocaleString()}</Text>}
            </Stack>
            <Group gap="xs">
              <Badge variant="light">{t(`writeups.${status}`)}</Badge>
              <Button variant="light" onClick={() => setSelected(item)}>{t('writeups.preview')}</Button>
              {mode === 'review' && <Button loading={busyId === item.id}
                onClick={() => void review(item, true)}>{t('writeups.approve')}</Button>}
              {status === 'pending' && <Button color="red" variant="light" loading={busyId === item.id}
                onClick={() => void review(item, false)}>{t('writeups.reject')}</Button>}
              {mode === 'management' && <Button color="red" variant="light" loading={busyId === item.id}
                onClick={() => takeDown(item)}>{t('writeups.takeDown')}</Button>}
            </Group>
          </Group>
        </Paper>
      ))}
      {(page > 0 || (data?.length ?? 0) > pageSize) &&
        <Group justify="flex-end">
          <Button variant="default" disabled={page === 0} onClick={() => setPage(page - 1)}>
            {t('writeups.previousPage')}
          </Button>
          <Text size="sm">{t('writeups.page', { page: page + 1 })}</Text>
          <Button variant="default" disabled={(data?.length ?? 0) <= pageSize}
            onClick={() => setPage(page + 1)}>{t('writeups.nextPage')}</Button>
        </Group>}
      <Modal opened={!!selected} onClose={() => setSelected(undefined)} title={selected?.title}
        size="xl" centered>
        {selected && <Stack>
          <PDFViewer url={`/api/challenges/${selected.challengeId}/community-writeups/${selected.id}/pdf`} height={600} />
          <Group justify="flex-end">
            <Anchor href={`/api/challenges/${selected.challengeId}/community-writeups/${selected.id}/pdf`}
              target="_blank" rel="noopener noreferrer">{t('writeups.preview')}</Anchor>
            {mode === 'review' && <Button onClick={() => void review(selected, true)}
              loading={busyId === selected.id}>{t('writeups.approve')}</Button>}
            {status === 'pending' && <Button color="red" variant="light"
              onClick={() => void review(selected, false)} loading={busyId === selected.id}>
              {t('writeups.reject')}
            </Button>}
            {mode === 'management' && <Button color="red" variant="light"
              onClick={() => takeDown(selected)} loading={busyId === selected.id}>
              {t('writeups.takeDown')}
            </Button>}
          </Group>
        </Stack>}
      </Modal>
    </Stack>
  )
}
