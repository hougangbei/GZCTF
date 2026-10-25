import { Button, SegmentedControl, Skeleton, useComputedColorScheme, useMantineTheme } from '@mantine/core'
import { mdiArrowRight, mdiChartLine, mdiReload, mdiTrophyOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import type { EChartsOption, SeriesOption } from 'echarts'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import useSWR from 'swr'
import { fetcher } from '@Api'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { WithNavBar } from '@Components/WithNavbar'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useIsMobile } from '@Utils/ThemeOverride'
import classes from './Leaderboard.module.css'

type Metric = 'score' | 'solves'
type Entry = { rank: number; userName: string; solvedCount: number; score: number }
type Point = { date: string; solvedCount: number; score: number }
type Series = { userName: string; points: Point[] }
type Snapshot = { metric: Metric; entries: Entry[]; series: Series[] }

const LearningLeaderboard = () => {
  const { t, i18n } = useTranslation('skillTrees')
  const [metric, setMetric] = useState<Metric>('score')
  const { data, error, isValidating, mutate } = useSWR<Snapshot>(
    `/api/learning-leaderboard?metric=${metric}`,
    fetcher,
    { refreshInterval: 15000, shouldRetryOnError: false }
  )
  const snapshot = data?.metric === metric ? data : undefined
  const isMobile = useIsMobile(640)
  const theme = useMantineTheme()
  const colorScheme = useComputedColorScheme('light', { getInitialValueInEffect: false })
  const metricLabel = t(metric === 'score' ? 'leaderboard.metricScore' : 'leaderboard.metricSolves')
  const otherLabel = t(metric === 'score' ? 'leaderboard.solved' : 'leaderboard.score')
  const status = snapshot
    ? snapshot.entries.length === 0 ? 'empty' : 'success'
    : error && !isValidating ? 'error' : 'loading'
  usePageTitle(t('leaderboard.title'))

  const visibleSeries = useMemo(() => snapshot?.series.slice(0, isMobile ? 3 : 5) ?? [], [snapshot?.series, isMobile])
  const chart = useMemo<EChartsOption>(() => {
    const shade = colorScheme === 'dark' ? 3 : 6
    const muted = colorScheme === 'dark' ? '#cbd5e1' : '#64748b'
    const border = colorScheme === 'dark' ? '#37465d' : '#e2e8f0'
    return {
      useUTC: true,
      backgroundColor: 'transparent',
      color: [theme.colors[theme.primaryColor][shade], theme.colors.teal[shade], theme.colors.orange[shade],
        theme.colors.grape[shade], theme.colors.blue[shade]],
      tooltip: { trigger: 'axis' },
      legend: { type: 'scroll', bottom: 0, textStyle: { color: muted }, itemWidth: 13, itemHeight: 3 },
      grid: { left: isMobile ? 43 : 54, right: isMobile ? 16 : 28, bottom: isMobile ? 68 : 58, top: 20 },
      xAxis: {
        type: 'time', minInterval: 24 * 60 * 60 * 1000,
        axisLine: { lineStyle: { color: border } }, axisTick: { show: false }, splitLine: { show: false },
        axisLabel: { color: muted, hideOverlap: true,
          formatter: (value: number) => new Date(value).toLocaleDateString(i18n.language, {
            month: 'numeric', day: 'numeric', timeZone: 'UTC',
          }),
        },
      },
      yAxis: {
        type: 'value', min: 0, splitNumber: 4,
        axisLabel: { color: muted }, axisLine: { show: false }, axisTick: { show: false },
        splitLine: { lineStyle: { color: border } },
      },
      series: visibleSeries.map(member => ({
        type: 'line', name: member.userName, step: 'end', showSymbol: false, symbolSize: 7,
        lineStyle: { width: 2.5 }, emphasis: { focus: 'series' },
        data: member.points.map(point => [
          `${point.date}T00:00:00Z`, metric === 'score' ? point.score : point.solvedCount,
        ]),
      }) satisfies SeriesOption),
    }
  }, [visibleSeries, metric, isMobile, colorScheme, theme.colors, theme.primaryColor, i18n.language])

  const retry = () => { void mutate() }

  return (
    <WithNavBar minWidth={0} withFooter>
      <div className={classes.page}>
        <header className={classes.header}>
          <div className={classes.heading}>
            <h1>{t('leaderboard.title')}</h1>
            <p>{t('leaderboard.subtitle')}</p>
          </div>
          <SegmentedControl
            className={classes.metricControl}
            aria-label={t('leaderboard.metric')}
            value={metric}
            onChange={value => setMetric(value as Metric)}
            data={[
              { value: 'score', label: t('leaderboard.metricScore') },
              { value: 'solves', label: t('leaderboard.metricSolves') },
            ]}
          />
        </header>

        {error && snapshot && (
          <div className={classes.stale} role="alert">
            <span>{t('leaderboard.stale')}</span>
            <Button size="compact-sm" variant="subtle" onClick={retry}>{t('leaderboard.retry')}</Button>
          </div>
        )}

        {status === 'loading' && (
          <section className={classes.loading} role="status" aria-busy="true">
            <span className={classes.srOnly}>{t('leaderboard.loading')}</span>
            <Skeleton height={25} width="30%" radius="sm" />
            <Skeleton height={58} radius="md" />
            <Skeleton height={58} radius="md" />
            <Skeleton height={58} radius="md" />
          </section>
        )}

        {status === 'error' && (
          <section className={classes.statePanel} role="alert">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiChartLine} size={1.7} /></span>
            <h2>{t('leaderboard.loadFailed')}</h2>
            <Button leftSection={<Icon path={mdiReload} size={0.8} />} onClick={retry}>
              {t('leaderboard.retry')}
            </Button>
          </section>
        )}

        {status === 'empty' && (
          <section className={classes.statePanel} role="status">
            <span className={classes.stateIcon} aria-hidden="true"><Icon path={mdiTrophyOutline} size={1.7} /></span>
            <h2>{t('leaderboard.emptyTitle')}</h2>
            <p>{t('leaderboard.emptyDescription')}</p>
            <Link className={classes.action} to="/skill-trees">
              {t('leaderboard.exploreSkills')}<Icon path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>
            <span className={classes.stateNote}>{t('leaderboard.emptyNote')}</span>
          </section>
        )}

        {status === 'success' && snapshot && (
          <div className={classes.sections}>
            <section className={classes.ranking} aria-labelledby="ranking-title">
              <div className={classes.sectionHeader}>
                <h2 id="ranking-title">{t('leaderboard.rankingTitle')}</h2>
                <p>{t('leaderboard.rankingHint')}</p>
              </div>
              <table className={classes.table}>
                <caption className={classes.srOnly}>{t('leaderboard.title')} · {metricLabel}</caption>
                <thead><tr>
                  <th scope="col">{t('leaderboard.rank')}</th>
                  <th scope="col">{t('leaderboard.username')}</th>
                  <th scope="col">{metricLabel}</th>
                  <th scope="col">{otherLabel}</th>
                </tr></thead>
                <tbody>{snapshot.entries.map(entry => (
                  <tr key={entry.userName} data-top={entry.rank <= 3 || undefined}>
                    <td className={classes.rank}><span>{String(entry.rank).padStart(2, '0')}</span></td>
                    <td className={classes.name} title={entry.userName}>{entry.userName}</td>
                    <td className={classes.primaryValue}>
                      <strong>{(metric === 'score' ? entry.score : entry.solvedCount).toLocaleString()}</strong>
                      <span className={classes.mobileLabel}>{metricLabel}</span>
                    </td>
                    <td className={classes.secondaryValue}>
                      {(metric === 'score' ? entry.solvedCount : entry.score).toLocaleString()}
                      <span className={classes.mobileLabel}>{otherLabel}</span>
                    </td>
                  </tr>
                ))}</tbody>
              </table>
            </section>

            {visibleSeries.length > 0 && (
              <section className={classes.trend} aria-labelledby="trend-title">
                <div className={classes.sectionHeader}>
                  <h2 id="trend-title">{t('leaderboard.trendTitle')}</h2>
                  <p>{t('leaderboard.trendHint')}</p>
                </div>
                <EchartsContainer
                  option={chart}
                  style={{ height: isMobile ? 260 : 310, width: '100%' }}
                  role="img"
                  aria-label={`${t('leaderboard.trendTitle')} · ${metricLabel}`}
                />
              </section>
            )}
          </div>
        )}
      </div>
    </WithNavBar>
  )
}

export default LearningLeaderboard
