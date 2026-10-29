import { Button, SegmentedControl, Skeleton, useComputedColorScheme, useMantineTheme } from '@mantine/core'
import { useReducedMotion } from '@mantine/hooks'
import { mdiArrowRight, mdiChartLine, mdiReload, mdiTrophyOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import useSWR from 'swr'
import { WithNavBar } from '@Components/WithNavbar'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { useIsMobile } from '@Utils/ThemeOverride'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUser } from '@Hooks/useUser'
import { fetcher } from '@Api'
import classes from './Leaderboard.module.css'
import { buildLeaderboardChart, type Metric, type LeaderboardSeries } from './LeaderboardChart'

type Entry = { rank: number; userName: string; solvedCount: number; score: number }
type Snapshot = { metric: Metric; entries: Entry[]; series: LeaderboardSeries[] }

const LearningLeaderboard = () => {
  const { t, i18n } = useTranslation('skillTrees')
  const { user, error: userError } = useUser()
  const currentUserName = userError ? undefined : (user?.userName ?? undefined)
  const [metric, setMetric] = useState<Metric>('score')
  const { data, error, isValidating, mutate } = useSWR<Snapshot>(
    `/api/learning-leaderboard?metric=${metric}`,
    fetcher,
    { refreshInterval: 15000, shouldRetryOnError: false }
  )
  const snapshot = data?.metric === metric ? data : undefined
  const isMobile = useIsMobile(640)
  const reducedMotion = useReducedMotion()
  const theme = useMantineTheme()
  const colorScheme = useComputedColorScheme('light')
  const chartPalette = useMemo(() => {
    const shade = typeof theme.primaryShade === 'number' ? theme.primaryShade : theme.primaryShade[colorScheme]
    return {
      accent: theme.colors[theme.primaryColor][shade],
      surface: colorScheme === 'dark' ? theme.colors.dark[6] : theme.white,
      border: colorScheme === 'dark' ? theme.colors.dark[4] : theme.colors.gray[2],
      ink: colorScheme === 'dark' ? theme.colors.dark[0] : theme.colors.gray[8],
      muted: colorScheme === 'dark' ? theme.colors.dark[1] : theme.colors.gray[6],
    }
  }, [theme, colorScheme])
  const metricLabel = t(metric === 'score' ? 'leaderboard.metricScore' : 'leaderboard.metricSolves')
  const otherLabel = t(metric === 'score' ? 'leaderboard.solved' : 'leaderboard.score')
  const status = snapshot
    ? snapshot.entries.length === 0
      ? 'empty'
      : 'success'
    : error && !isValidating
      ? 'error'
      : 'loading'
  usePageTitle(t('leaderboard.title'))

  const visibleSeries = useMemo(() => snapshot?.series.slice(0, 10) ?? [], [snapshot?.series])
  const chart = useMemo(
    () =>
      buildLeaderboardChart(
        visibleSeries,
        metric,
        isMobile,
        i18n.language,
        reducedMotion,
        currentUserName,
        chartPalette
      ),
    [visibleSeries, metric, isMobile, i18n.language, reducedMotion, currentUserName, chartPalette]
  )

  const retry = () => {
    void mutate()
  }

  return (
    <WithNavBar className={classes.shell} minWidth={0} withFooter>
      <div className={classes.page} data-competition-board>
        <header className={classes.header}>
          <div className={classes.heading}>
            <nav className={classes.breadcrumb} aria-label={t('leaderboard.breadcrumbLabel')}>
              <Link to="/skill-trees">{t('leaderboard.learningSpace')}</Link>
              <span aria-hidden="true">/</span>
              <span>{t('leaderboard.title')}</span>
            </nav>
            <h1>{t('leaderboard.title')}</h1>
            <p>{t('leaderboard.subtitle')}</p>
          </div>
          <span className={classes.refreshStatus}>
            <span className={classes.statusDot} aria-hidden="true" />
            {isValidating && snapshot ? t('leaderboard.loading') : t('leaderboard.autoRefresh')}
          </span>
        </header>

        <div className={classes.chartToolbar}>
          {status === 'success' && visibleSeries.length > 0 && (
            <div className={classes.sectionHeader}>
              <h2 id="trend-title">{t('leaderboard.trendTitle')}</h2>
              <p>{t(isMobile ? 'leaderboard.trendHintMobile' : 'leaderboard.trendHintDesktop')}</p>
            </div>
          )}
          <div className={classes.controls}>
            <SegmentedControl
              className={classes.metricControl}
              aria-label={t('leaderboard.metric')}
              value={metric}
              onChange={(value) => setMetric(value as Metric)}
              data={[
                { value: 'score', label: t('leaderboard.metricScore') },
                { value: 'solves', label: t('leaderboard.metricSolves') },
              ]}
            />
          </div>
        </div>

        {error && snapshot && (
          <div className={classes.stale} role="alert">
            <span>{t('leaderboard.stale')}</span>
            <Button size="compact-sm" variant="subtle" color="teal" onClick={retry}>
              {t('leaderboard.retry')}
            </Button>
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
            <span className={classes.stateIcon} aria-hidden="true">
              <Icon path={mdiChartLine} size={1.7} />
            </span>
            <h2>{t('leaderboard.loadFailed')}</h2>
            <Button leftSection={<Icon path={mdiReload} size={0.8} />} onClick={retry}>
              {t('leaderboard.retry')}
            </Button>
          </section>
        )}

        {status === 'empty' && (
          <section className={classes.statePanel} role="status">
            <span className={classes.stateIcon} aria-hidden="true">
              <Icon path={mdiTrophyOutline} size={1.7} />
            </span>
            <h2>{t('leaderboard.emptyTitle')}</h2>
            <p>{t('leaderboard.emptyDescription')}</p>
            <Link className={classes.action} to="/skill-trees">
              {t('leaderboard.exploreSkills')}
              <Icon path={mdiArrowRight} size={0.8} aria-hidden="true" />
            </Link>
            <span className={classes.stateNote}>{t('leaderboard.emptyNote')}</span>
          </section>
        )}

        {status === 'success' && snapshot && (
          <div className={classes.sections}>
            {visibleSeries.length > 0 && (
              <section className={classes.trend} aria-labelledby="trend-title">
                <EchartsContainer
                  key={isMobile ? 'mobile' : 'desktop'}
                  option={chart}
                  style={{ height: isMobile ? 250 : 370, width: '100%' }}
                  role="img"
                  aria-label={`${t('leaderboard.trendTitle')} · ${metricLabel}`}
                />
              </section>
            )}
            <section className={classes.ranking} aria-labelledby="ranking-title">
              <div className={classes.sectionHeader}>
                <h2 id="ranking-title">{t('leaderboard.rankingTitle')}</h2>
                <p>{t('leaderboard.rankingHint')}</p>
              </div>
              <table className={classes.table}>
                <caption className={classes.srOnly}>
                  {t('leaderboard.title')} · {metricLabel}
                </caption>
                <thead>
                  <tr>
                    <th scope="col">{t('leaderboard.rank')}</th>
                    <th scope="col">{t('leaderboard.username')}</th>
                    <th scope="col">{otherLabel}</th>
                    <th scope="col">{metricLabel}</th>
                  </tr>
                </thead>
                <tbody>
                  {snapshot.entries.map((entry) => (
                    <tr
                      key={entry.userName}
                      data-top={entry.rank <= 3 || undefined}
                      data-rank={entry.rank}
                      data-self={entry.userName === currentUserName || undefined}
                      aria-current={entry.userName === currentUserName ? true : undefined}
                    >
                      <td className={classes.rank}>
                        <span>{String(entry.rank).padStart(2, '0')}</span>
                      </td>
                      <td className={classes.name} title={entry.userName}>
                        <span className={classes.nameText}>{entry.userName}</span>
                        {entry.userName === currentUserName && (
                          <span className={classes.selfLabel}>{t('leaderboard.yourPosition')}</span>
                        )}
                      </td>
                      <td className={classes.secondaryValue}>
                        {(metric === 'score' ? entry.solvedCount : entry.score).toLocaleString()}
                        <span className={classes.mobileLabel}>
                          {' '}
                          {t(metric === 'score' ? 'leaderboard.solvesUnit' : 'leaderboard.score')}
                        </span>
                      </td>
                      <td className={classes.primaryValue}>
                        <strong>{(metric === 'score' ? entry.score : entry.solvedCount).toLocaleString()}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className={classes.tableNote}>{t('leaderboard.emptyNote')}</p>
            </section>
          </div>
        )}
      </div>
    </WithNavBar>
  )
}

export default LearningLeaderboard
