import { Accordion, Alert, Badge, Button, Group, Loader, Paper, Select, Stack, Text } from '@mantine/core'
import { mdiAlertCircleOutline, mdiRefresh } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LogPage, SystemLog, adminLogsApi } from '@Utils/AdminLogsApi'
import { useLanguage } from '@Utils/I18n'
import { LogPager } from './LogPager'

const PAGE_SIZE = 20

type Props = { refreshKey: number }

export const SystemLogPanel: FC<Props> = ({ refreshKey }) => {
  const { t } = useTranslation()
  const { locale } = useLanguage()
  const [level, setLevel] = useState('Information')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [data, setData] = useState<LogPage<SystemLog>>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => setPage(1), [level, refreshKey])

  useEffect(() => {
    let active = true
    setLoading(true)
    setError(false)
    adminLogsApi.system({ page, pageSize: PAGE_SIZE, level })
      .then((response) => { if (active) setData(response.data) })
      .catch(() => { if (active) setError(true) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, level, reload, refreshKey])

  return (
    <Stack gap="sm">
      <Select maw={260} label={t('admin.logsConsole.filters.level')} value={level}
        onChange={(value) => { if (value) setLevel(value) }}
        data={['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal', 'All'].map((value) => ({
          value, label: t(`admin.logsConsole.levels.${value}`, { defaultValue: value }),
        }))} />
      {error ? <Alert color="red" icon={<Icon path={mdiAlertCircleOutline} size={0.9} />} title={t('admin.logsConsole.loadFailed')}>
        <Button mt="xs" size="xs" leftSection={<Icon path={mdiRefresh} size={0.8} />} onClick={() => setReload((value) => value + 1)}>
          {t('admin.logsConsole.retry')}
        </Button>
      </Alert> : loading && !data ? <Group justify="center" p="xl"><Loader size="sm" /></Group> : (
        <>
          <Accordion key={`${page}-${level}-${reload}`} variant="separated" radius="md" chevronPosition="left">
            {(data?.items ?? []).map((item) => (
              <Accordion.Item key={item.id} value={String(item.id)}>
                <Accordion.Control>
                  <Group gap="xs" wrap="wrap">
                    <Text size="sm" c="dimmed">{dayjs(item.time).locale(locale).format('YYYY-MM-DD HH:mm:ss')}</Text>
                    <Badge color={item.level === 'Error' ? 'red' : item.level === 'Warning' ? 'orange' : 'blue'}>{item.level}</Badge>
                    <Text size="sm" c="dimmed">{item.source || '—'}</Text>
                    <Text size="sm" lineClamp={1} style={{ flex: 1, minWidth: 140 }}>{item.msg || ''}</Text>
                  </Group>
                </Accordion.Control>
                <Accordion.Panel>
                  <Paper p="sm" withBorder>
                    <Stack gap={4}>
                      <Text size="sm" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{item.msg || ''}</Text>
                      {item.source && <Text size="sm" c="dimmed">{t('admin.logsConsole.detail.source')}: {item.source}</Text>}
                      {item.exception && <Text component="pre" size="xs" c="red" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{item.exception}</Text>}
                      {item.ip && <Text size="xs" c="dimmed">{item.ip}</Text>}
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
