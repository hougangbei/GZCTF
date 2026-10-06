import { Accordion, Alert, Badge, Button, Group, Loader, Paper, Select, Stack, Text, TextInput } from '@mantine/core'
import { mdiAlertCircleOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AuditEvent, LogPage, adminLogsApi } from '@Utils/AdminLogsApi'
import { useLanguage } from '@Utils/I18n'
import { LogPager } from './LogPager'

const PAGE_SIZE = 20

type Props = { refreshKey: number }

export const AuditLogPanel: FC<Props> = ({ refreshKey }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const [category, setCategory] = useState<string | null>(null)
  const [result, setResult] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [data, setData] = useState<LogPage<AuditEvent>>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => setPage(1), [category, result, search, refreshKey])

  useEffect(() => {
    let active = true
    setLoading(true)
    setError(false)
    adminLogsApi.audit({ page, pageSize: PAGE_SIZE, category: category ?? undefined,
      succeeded: result ? result === 'success' : undefined, search: search.trim() || undefined })
      .then((response) => { if (active) setData(response.data) })
      .catch(() => { if (active) setError(true) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, category, result, search, reload, refreshKey])

  const categoryData = [
    { value: '', label: t('admin.logsConsole.filters.allCategories') },
    ...['users', 'challenges', 'skill_trees', 'containers', 'settings', 'content', 'cohorts', 'dashboards']
      .map((value) => ({ value, label: t(`admin.logsConsole.categories.${value}`, { defaultValue: value }) })),
  ]

  return (
    <Stack gap="sm">
      <Group grow align="end" wrap="wrap">
        <Select label={t('admin.logsConsole.filters.category')} data={categoryData} value={category ?? ''}
          onChange={(value) => setCategory(value || null)} allowDeselect={false} />
        <Select label={t('admin.logsConsole.filters.result')} value={result ?? ''}
          onChange={(value) => setResult(value || null)} allowDeselect={false}
          data={[{ value: '', label: t('admin.logsConsole.filters.allResults') },
            { value: 'success', label: t('admin.logsConsole.result.success') },
            { value: 'failure', label: t('admin.logsConsole.result.failure') }]} />
        <TextInput label={t('admin.logsConsole.filters.search')} value={search} onChange={(event) => setSearch(event.currentTarget.value)} />
      </Group>

      {error ? <Alert color="red" icon={<Icon path={mdiAlertCircleOutline} size={0.9} />}
        title={t('admin.logsConsole.loadFailed')}>
        <Button mt="xs" size="xs" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => setReload((value) => value + 1)}>
          {t('admin.logsConsole.retry')}
        </Button>
      </Alert> : loading && !data ? <Group justify="center" p="xl"><Loader size="sm" /></Group> : (
        <>
          <Accordion variant="separated" radius="md" chevronPosition="left">
            {(data?.items ?? []).map((item) => (
              <Accordion.Item key={item.id} value={item.id}>
                <Accordion.Control>
                  <Group gap="xs" wrap="wrap">
                    <Text size="sm" c="dimmed">{dayjs(item.occurredAtUtc).locale(locale).format('YYYY-MM-DD HH:mm:ss')}</Text>
                    <Text size="sm" fw={600}>{item.actorName}</Text>
                    <Badge variant="light">{t(`admin.logsConsole.categories.${item.category}`, { defaultValue: item.category })}</Badge>
                    <Text size="sm">{t(`admin.logsConsole.actions.${item.action}`, { defaultValue: item.action })}</Text>
                    <Text size="sm">{item.targetName || item.targetId || item.targetType}</Text>
                    <Badge color={item.succeeded ? 'teal' : 'red'}>{t(item.succeeded ? 'admin.logsConsole.result.success' : 'admin.logsConsole.result.failure')}</Badge>
                  </Group>
                </Accordion.Control>
                <Accordion.Panel>
                  <Paper p="sm" withBorder>
                    <Stack gap={4}>
                      <Text size="sm">{t('admin.logsConsole.detail.actor')}: {item.actorName} ({item.actorKind})</Text>
                      <Text size="sm">{t('admin.logsConsole.detail.target')}: {item.targetType} · {item.targetName || item.targetId || '—'}</Text>
                      <Text size="sm">{t('admin.logsConsole.detail.status')}: {item.httpStatus}</Text>
                      {item.errorReason && <Text size="sm" c="red">{item.errorCode}: {item.errorReason}</Text>}
                      {item.affectedCount != null && <Text size="sm">{t('admin.logsConsole.detail.affected')}: {item.affectedCount}</Text>}
                      <Text size="xs" c="dimmed">{t('admin.logsConsole.detail.requestId')}: {item.requestId}</Text>
                    </Stack>
                  </Paper>
                </Accordion.Panel>
              </Accordion.Item>
            ))}
          </Accordion>
          {data && <LogPager page={page} pageSize={data.pageSize} total={data.total} onChange={setPage} />}
          {!loading && data?.items.length === 0 && <Text c="dimmed" ta="center" p="xl">{t('admin.logsConsole.empty')}</Text>}
        </>
      )}
    </Stack>
  )
}
