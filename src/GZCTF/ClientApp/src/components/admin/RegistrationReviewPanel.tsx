import { Alert, Badge, Button, Group, Paper, Stack, Text, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import api, { fetcher } from '@Api'

type ReviewItem = {
  id: string
  userName: string
  email: string
  realName: string
  stdNumber: string
  emailConfirmed: boolean
  status: 'Pending' | 'Rejected'
  registerTimeUtc: number
}

type ReviewPage = { total: number; items: ReviewItem[] }

const pageSize = 50

export const RegistrationReviewPanel = ({ onChanged }: { onChanged: () => void }) => {
  const { t } = useTranslation('admin')
  const [page, setPage] = useState(0)
  const [busyId, setBusyId] = useState<string>()
  const [actionError, setActionError] = useState(false)
  const { data, error, mutate } = useSWR<ReviewPage>(
    `/api/admin/registration-reviews?offset=${page * pageSize}&limit=${pageSize}`, fetcher
  )

  const change = async (item: ReviewItem, action: 'approve' | 'reject') => {
    setBusyId(item.id)
    setActionError(false)
    try {
      await api.request({ path: `/api/admin/registration-reviews/${item.id}/${action}`,
        method: 'POST' })
      const fresh = await mutate()
      if (page > 0 && fresh?.items.length === 0) setPage(page - 1)
      onChanged()
    } catch {
      setActionError(true)
    } finally {
      setBusyId(undefined)
    }
  }

  const reject = (item: ReviewItem) => modals.openConfirmModal({
    title: t('registrationReview.reject'),
    children: <Text size="sm">{t('registrationReview.rejectConfirm', { name: item.userName })}</Text>,
    labels: { confirm: t('registrationReview.reject'), cancel: t('registrationReview.cancel') },
    confirmProps: { color: 'red' },
    onConfirm: () => { void change(item, 'reject') },
  })

  return <Paper withBorder p="md" w="100%">
    <Stack gap="sm">
      <Title order={3}>{t('registrationReview.title')}</Title>
      <Text size="sm" c="dimmed">{t('registrationReview.hint')}</Text>
      {(error || actionError) && <Alert color="red">{t('registrationReview.failed')}</Alert>}
      {data?.total === 0 && <Text c="dimmed">{t('registrationReview.empty')}</Text>}
      {data?.items.map((item) => <Paper key={item.id} withBorder p="sm">
        <Group justify="space-between" align="flex-start" wrap="wrap">
          <Stack gap={2}>
            <Group gap="xs"><Text fw={600}>{item.userName}</Text>
              <Badge color={item.emailConfirmed ? 'teal' : 'orange'}>
                {t(item.emailConfirmed ? 'registrationReview.verified' : 'registrationReview.unverified')}
              </Badge>
              {item.status === 'Rejected' && <Badge color="red">{t('registrationReview.rejected')}</Badge>}
            </Group>
            <Text size="sm">{item.realName} · {item.stdNumber}</Text>
            <Text size="sm" c="dimmed">{item.email}</Text>
            <Text size="xs" c="dimmed">{new Date(item.registerTimeUtc).toLocaleString()}</Text>
          </Stack>
          <Group gap="xs">
            <Button size="xs" disabled={!item.emailConfirmed} loading={busyId === item.id}
              onClick={() => void change(item, 'approve')}>{t('registrationReview.approve')}</Button>
            {item.status === 'Pending' && <Button size="xs" color="red" variant="light"
              disabled={!item.emailConfirmed} loading={busyId === item.id} onClick={() => reject(item)}>
              {t('registrationReview.reject')}
            </Button>}
          </Group>
        </Group>
      </Paper>)}
      {data && data.total > pageSize && <Group justify="flex-end">
        <Button variant="default" size="xs" disabled={page === 0}
          onClick={() => setPage(page - 1)}>{t('registrationReview.previous')}</Button>
        <Text size="sm">{page + 1}</Text>
        <Button variant="default" size="xs" disabled={(page + 1) * pageSize >= data.total}
          onClick={() => setPage(page + 1)}>{t('registrationReview.next')}</Button>
      </Group>}
    </Stack>
  </Paper>
}
