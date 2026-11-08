import chroma from 'chroma-js'
import type { EChartsOption, SeriesOption } from 'echarts'

export type Metric = 'score' | 'solves'
export type LeaderboardSeries = {
  userName: string
  points: { date: string; solvedCount: number; score: number }[]
}

// Separate palettes keep all ten learners distinguishable on either theme.
export const leaderboardColors = [
  '#2563eb',
  '#b45309',
  '#047857',
  '#be185d',
  '#6d28d9',
  '#0e7490',
  '#b91c1c',
  '#4d7c0f',
  '#a21caf',
  '#475569',
]
const darkLeaderboardColors = [
  '#60a5fa',
  '#fbbf24',
  '#34d399',
  '#f472b6',
  '#a78bfa',
  '#22d3ee',
  '#fb7185',
  '#a3e635',
  '#e879f9',
  '#cbd5e1',
]

// Custom theme accents can be very pale; preserve their hue while improving contrast.
const contrastColor = (color: string, surface: string): string => {
  if (chroma.contrast(color, surface) >= 3) return color
  const target = chroma(surface).luminance() > 0.4 ? '#000000' : '#ffffff'
  for (let amount = 0; amount <= 100; amount += 5) {
    const candidate = chroma.mix(color, target, amount / 100, 'rgb').hex()
    if (chroma.contrast(candidate, surface) >= 3) return candidate
  }
  return target
}

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
): EChartsOption => {
  const colors = (chroma(palette.surface).luminance() > 0.4 ? leaderboardColors : darkLeaderboardColors).map((color) =>
    contrastColor(color, palette.surface)
  )
  const accent = contrastColor(palette.accent, palette.surface)
  return {
    useUTC: true,
    animation: !reducedMotion,
    animationDuration: 200,
    backgroundColor: 'transparent',
    color: colors,
    textStyle: { fontFamily: 'inherit' },
    tooltip: {
      trigger: 'item',
      triggerOn: 'none',
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
    grid: { left: mobile ? 44 : 56, right: mobile ? 14 : 24, bottom: 96, top: 22 },
    dataZoom: [
      { type: 'inside', start: 0, end: 100, xAxisIndex: 0, filterMode: 'none', zoomOnMouseWheel: 'ctrl' },
      {
        type: 'slider',
        start: 0,
        end: 100,
        xAxisIndex: 0,
        filterMode: 'none',
        bottom: 32,
        height: 18,
        showDetail: false,
        showDataShadow: false,
        borderColor: palette.border,
        fillerColor: palette.border,
        handleStyle: { color: palette.accent },
        textStyle: { color: palette.muted },
      },
    ],
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
            ...(new Set(members.flatMap((member) => member.points.map((point) => point.date.slice(0, 4)))).size > 1
              ? { year: 'numeric' as const }
              : {}),
            month: 'numeric',
            day: 'numeric',
            timeZone: 'UTC',
          }),
      },
    },
    yAxis: {
      type: 'value',
      min:
        metric === 'score'
          ? members.reduce(
              (minimum, member) => member.points.reduce((value, point) => Math.min(value, point.score), minimum),
              0
            )
          : 0,
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
            color: member.userName === currentUserName ? accent : colors[index % colors.length],
          },
          lineStyle: {
            color: member.userName === currentUserName ? accent : colors[index % colors.length],
            width: member.userName === currentUserName ? 3 : 2,
          },
          emphasis: { focus: 'series' },
          data: member.points.map((point) => [
            `${point.date}T00:00:00Z`,
            metric === 'score' ? point.score : point.solvedCount,
          ]),
        }) satisfies SeriesOption
    ),
  }
}
