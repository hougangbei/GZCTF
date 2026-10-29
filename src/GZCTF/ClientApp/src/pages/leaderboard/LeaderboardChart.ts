import type { EChartsOption, SeriesOption } from 'echarts'

export type Metric = 'score' | 'solves'
export type LeaderboardSeries = {
  userName: string
  points: { date: string; solvedCount: number; score: number }[]
}

export const leaderboardColors = [
  '#8a9bac',
  '#b4b8bc',
  '#c3af83',
  '#a59daf',
  '#b4a0a2',
  '#96acad',
  '#a9ac99',
  '#b1a393',
  '#969696',
  '#b49b88',
]

export type LeaderboardChartPalette = { accent: string; surface: string; border: string; ink: string; muted: string }
const defaultPalette: LeaderboardChartPalette = {
  accent: '#5558E8',
  surface: '#ffffff',
  border: '#e2e8f0',
  ink: '#1e293b',
  muted: '#64748b',
}

export const buildLeaderboardChart = (
  members: LeaderboardSeries[],
  metric: Metric,
  mobile: boolean,
  language: string,
  reducedMotion: boolean,
  currentUserName?: string,
  palette: LeaderboardChartPalette = defaultPalette
): EChartsOption => ({
  useUTC: true,
  animation: !reducedMotion,
  animationDuration: 200,
  backgroundColor: 'transparent',
  color: leaderboardColors,
  textStyle: { fontFamily: 'inherit' },
  tooltip: {
    trigger: 'axis',
    renderMode: 'richText',
    confine: true,
    backgroundColor: palette.surface,
    borderColor: palette.border,
    textStyle: { color: palette.ink },
    axisPointer: { type: 'line', lineStyle: { color: palette.muted, type: 'dashed' } },
  },
  legend: {
    type: 'scroll',
    bottom: 0,
    left: 0,
    right: 0,
    textStyle: { color: palette.muted, fontSize: 12 },
    pageTextStyle: { color: palette.muted },
    pageIconColor: palette.accent,
    pageIconInactiveColor: palette.border,
    inactiveColor: palette.muted,
    itemWidth: 18,
    itemHeight: 4,
    itemGap: 20,
    selected: Object.fromEntries(
      members.map((member, index) => [member.userName, !mobile || index < 3 || member.userName === currentUserName])
    ),
  },
  grid: { left: mobile ? 44 : 56, right: mobile ? 14 : 24, bottom: 58, top: 22 },
  xAxis: {
    type: 'time',
    minInterval: 86400000,
    axisLine: { lineStyle: { color: palette.border } },
    axisTick: { show: false },
    splitLine: { show: false },
    axisLabel: {
      color: palette.muted,
      hideOverlap: true,
      margin: 15,
      formatter: (value: number) =>
        new Date(value).toLocaleDateString(language, {
          month: 'numeric',
          day: 'numeric',
          timeZone: 'UTC',
        }),
    },
  },
  yAxis: {
    type: 'value',
    min: 0,
    minInterval: metric === 'solves' ? 1 : undefined,
    splitNumber: 4,
    axisLabel: { color: palette.muted },
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: { lineStyle: { color: palette.border, type: 'solid' } },
  },
  series: members.map(
    (member, index) =>
      ({
        type: 'line',
        name: member.userName,
        step: 'end',
        showSymbol: member.points.length === 1,
        symbol: 'circle',
        symbolSize: 5,
        itemStyle: {
          color:
            member.userName === currentUserName ? palette.accent : leaderboardColors[index % leaderboardColors.length],
        },
        lineStyle: {
          color:
            member.userName === currentUserName ? palette.accent : leaderboardColors[index % leaderboardColors.length],
          width: member.userName === currentUserName ? 2.3 : 1.25,
        },
        emphasis: { focus: 'series' },
        data: member.points.map((point) => [
          `${point.date}T00:00:00Z`,
          metric === 'score' ? point.score : point.solvedCount,
        ]),
      }) satisfies SeriesOption
  ),
})
