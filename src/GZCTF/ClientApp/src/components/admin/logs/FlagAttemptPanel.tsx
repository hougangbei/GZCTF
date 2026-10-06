import { Accordion, Alert, Badge, Button, Group, Loader, Paper, Select, Stack, Text, TextInput } from '@mantine/core'
import { mdiAlertCircleOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FlagAttempt, FlagAttemptDetail, LogPage, adminLogsApi } from '@Utils/AdminLogsApi'
import { useLanguage } from '@Utils/I18n'
import { LogPager } from './LogPager'

const PAGE_SIZE = 20

type Props = { refreshKey: number }

export const FlagAttemptPanel: FC<Props> = ({ refreshKey }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const [outcome, setOutcome] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [data, setData] = useState<LogPage<FlagAttempt>>()
  const [details, setDetails] = useState<Record<string, FlagAttemptDetail>>({})
  const [detailErrors, setDetailErrors] = useState<Record<string, boolean>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => setPage(1), [outcome, search, refreshKey])

  useEffect(() => {
    let active = true
    setLoading(true)
    setError(false)
    adminLogsApi.flagAttempts({ page, pageSize: PAGE_SIZE, outcome: outcome || undefined, search: search.trim() || undefined })
      .then((response) => { if (active) setData(response.data) })
      .catch(() => { if (active) setError(true) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, outcome, search, reload, refreshKey])

  const loadDetail = (id: string) => {
    if (details[id]) return
    setDetailErrors((current) => ({ ...current, [id]: false }))
    adminLogsApi.flagAttemptDetail(id)
      .then((response) => setDetails((current) => ({ ...current, [id]: response.data })))
      .catch(() => setDetailErrors((current) => ({ ...current, [id]: true })))
  }

  return (
    <Stack gap="sm">
      <Group grow align="end" wrap="wrap">
        <Select label={t('admin.logsConsole.filters.result')} value={outcome ?? ''}
          onChange={(value) => setOutcome(value || null)} allowDeselect={false}
          data={[{ value: '', label: t('admin.logsConsole.filters.allResults') },
            ...['accepted', 'incorrect', 'rejected', 'error'].map((value) => ({
              value, label: t(`admin.logsConsole.result.${value}`, { defaultValue: value }),
            }))]} />
        <TextInput label={t('admin.logsConsole.filters.search')} value={search} onChange={(event) => setSearch(event.currentTarget.value)} />
      </Group>

      {error ? <Alert color="red" icon={<Icon path={mdiAlertCircleOutline} size={0.9} />} title={t('admin.logsConsole.loadFailed')}>
        <Button mt="xs" size="xs" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => setReload((value) => value + 1)}>
          {t('admin.logsConsole.retry')}
        </Button>
      </Alert> : loading && !data ? <Group justify="center" p="xl"><Loader size="sm" /></Group> : (
        <>
          <Accordion key={`${page}-${outcome}-${search}-${reload}`} variant="separated" radius="md" chevronPosition="left"
            onChange={(id) => { if (id) loadDetail(id) }}>
            {(data?.items ?? []).map((item) => {
              const detail = details[item.id]
              return <Accordion.Item key={item.id} value={item.id}>
                <Accordion.Control>
                  <Group gap="xs" wrap="wrap">
                    <Text size="sm" c="dimmed">{dayjs(item.occurredAtUtc).locale(locale).format('YYYY-MM-DD HH:mm:ss')}</Text>
                    <Text size="sm" fw={600}>{item.userName}</Text>
                    <Text size="sm">{item.challengeName || item.challengeId}</Text>
                    <Badge color={item.outcome === 'accepted' ? 'teal' : item.outcome === 'incorrect' ? 'orange' : 'red'}>
                      {t(`admin.logsConsole.result.${item.outcome}`, { defaultValue: item.outcome })}
                    </Badge>
                  </Group>
                </Accordion.Control>
                <Accordion.Panel>
                  {detailErrors[item.id] ? <Alert color="red" title={t('admin.logsConsole.detailFailed')}>
                    <Button size="xs" mt="xs" onClick={() => loadDetail(item.id)}>{t('admin.logsConsole.retry')}</Button>
                  </Alert> : !detail ? <Group p="sm"><Loader size="xs" /></Group> : (
                    <Paper p="sm" withBorder>
                      <Stack gap={4}>
                        <Text size="sm">{t('admin.logsConsole.detail.submission')}: {detail.submissionId || '—'}</Text>
                        <Text size="sm">{t('admin.logsConsole.detail.rejection')}: {detail.rejectionCode || '—'}</Text>
                        <Text size="sm">{t('admin.logsConsole.detail.submittedFlag')}:</Text>
                        {detail.originalAvailable && detail.submittedFlag != null
                          ? <Text ff="monospace" size="sm" style={{ overflowWrap: 'anywhere' }}>{detail.submittedFlag}</Text>
                          : <Text size="sm" c="dimmed">{t('admin.logsConsole.detail.originalUnavailable')}</Text>}
                      </Stack>
                    </Paper>
                  )}
                </Accordion.Panel>
              </Accordion.Item>
            })}
          </Accordion>
          {data && <LogPager page={page} pageSize={data.pageSize} total={data.total} onChange={setPage} />}
          {!loading && data?.items.length === 0 && <Text c="dimmed" ta="center" p="xl">{t('admin.logsConsole.empty')}</Text>}
        </>
      )}
    </Stack>
  )
}
