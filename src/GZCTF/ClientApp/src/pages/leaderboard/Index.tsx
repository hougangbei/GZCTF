import { Alert, Center, Loader, SegmentedControl, Stack, Table, Text, Title } from '@mantine/core'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import useSWR from 'swr'
import type { EChartsOption, SeriesOption } from 'echarts'
import { fetcher } from '@Api'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'

type Metric = 'score' | 'solves'
type Entry = { rank: number; userName: string; solvedCount: number; score: number }
type Point = { date: string; solvedCount: number; score: number }
type Series = { userName: string; points: Point[] }
type Snapshot = { metric: Metric; entries: Entry[]; series: Series[] }

const LearningLeaderboard = () => {
  const { t } = useTranslation('skillTrees')
  const [metric, setMetric] = useState<Metric>('score')
  const { data, error } = useSWR<Snapshot>(`/api/learning-leaderboard?metric=${metric}`, fetcher, {
    refreshInterval: 15000,
  })
  const metricLabel = t(metric === 'score' ? 'leaderboard.metricScore' : 'leaderboard.metricSolves')
  usePageTitle(t('leaderboard.title'))

  const chart = useMemo<EChartsOption>(() => ({
    useUTC: true,
    tooltip: { trigger: 'axis' },
    legend: { type: 'scroll' },
    grid: { left: 48, right: 24, bottom: 48, top: 52 },
    xAxis: { type: 'time', name: t('leaderboard.date') },
    yAxis: { type: 'value', name: metricLabel, min: 0, minInterval: 1 },
    series: (data?.series ?? []).map((member) => ({
      type: 'line',
      name: member.userName,
      step: 'end',
      showSymbol: false,
      data: member.points.map((point) => [`${point.date}T00:00:00Z`, metric === 'score' ? point.score : point.solvedCount]),
    }) satisfies SeriesOption),
  }), [data, metric, metricLabel, t])

  return (
    <WithNavBar minWidth={0}>
      <Stack gap="lg">
        <Title order={1}>{t('leaderboard.title')}</Title>
        <SegmentedControl
          aria-label={t('leaderboard.metric')}
          value={metric}
          onChange={(value) => setMetric(value as Metric)}
          data={[
            { value: 'score', label: t('leaderboard.metricScore') },
            { value: 'solves', label: t('leaderboard.metricSolves') },
          ]}
        />
        {error ? <Alert color="red">{t('leaderboard.loadFailed')}</Alert>
          : !data ? <Center><Loader /></Center>
            : data.entries.length === 0 ? <Text c="dimmed">{t('leaderboard.empty')}</Text>
              : <>
                <EchartsContainer option={chart} style={{ height: 420, width: '100%' }} />
                <Table striped withTableBorder>
                  <Table.Thead><Table.Tr>
                    <Table.Th>{t('leaderboard.rank')}</Table.Th>
                    <Table.Th>{t('leaderboard.username')}</Table.Th>
                    <Table.Th>{t(metric === 'score' ? 'leaderboard.score' : 'leaderboard.solved')}</Table.Th>
                    <Table.Th>{t(metric === 'score' ? 'leaderboard.solved' : 'leaderboard.score')}</Table.Th>
                  </Table.Tr></Table.Thead>
                  <Table.Tbody>{data.entries.map((entry) => <Table.Tr key={entry.userName}>
                    <Table.Td>{entry.rank}</Table.Td>
                    <Table.Td>{entry.userName}</Table.Td>
                    <Table.Td>{metric === 'score' ? entry.score : entry.solvedCount}</Table.Td>
                    <Table.Td>{metric === 'score' ? entry.solvedCount : entry.score}</Table.Td>
                  </Table.Tr>)}</Table.Tbody>
                </Table>
              </>}
      </Stack>
    </WithNavBar>
  )
}

export default LearningLeaderboard
