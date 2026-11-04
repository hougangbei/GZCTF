import { describe, expect, it } from 'vitest'
import { buildLeaderboardChart } from './LeaderboardChart'

const series = Array.from({ length: 10 }, (_, index) => ({
  userName: index === 0 ? '<img src=x onerror=alert(1)>' : `learner-${index}`,
  points: [{ date: '2026-10-25', score: 100 + index, solvedCount: index + 1 }],
}))

describe('competition scoreboard chart', () => {
  it('keeps all ten series selectable and initially selects only three on a phone', () => {
    const option = buildLeaderboardChart(series, 'score', true, 'zh-CN', false)
    expect(option.series).toHaveLength(10)
    const selected = (option.legend as { selected: Record<string, boolean> }).selected
    expect(Object.values(selected).filter(Boolean)).toHaveLength(3)
    expect(selected['learner-9']).toBe(false)
  })

  it('shows ten desktop series, with plain-text tooltips and no animation for reduced motion', () => {
    const option = buildLeaderboardChart(series, 'solves', false, 'zh-CN', true)
    expect(option.series).toHaveLength(10)
    expect(Object.values((option.legend as { selected: Record<string, boolean> }).selected).every(Boolean)).toBe(true)
    expect(option.tooltip).toMatchObject({ renderMode: 'richText', confine: true })
    expect(option.animation).toBe(false)
    expect(Array.isArray(option.series) ? option.series[0] : option.series).toMatchObject({
      step: 'end',
      data: [['2026-10-25T00:00:00Z', 1]],
    })
  })
})

it('uses the default accent line for the signed-in learner and keeps that learner visible on mobile', () => {
  const option = buildLeaderboardChart(series, 'score', true, 'zh-CN', true, 'learner-7')
  const lines = Array.isArray(option.series) ? option.series : [option.series]
  expect(lines[7]).toMatchObject({ itemStyle: { color: '#5558E8' }, lineStyle: { color: '#5558E8', width: 2.3 } })
  expect((option.legend as { selected: Record<string, boolean> }).selected['learner-7']).toBe(true)
})

it('applies the provided theme to highlighted lines and chart labels', () => {
  const palette = { accent: '#315EAC', surface: '#ffffff', border: '#ddeeff', ink: '#123456', muted: '#456789' }
  const option = buildLeaderboardChart(series, 'score', true, 'en-US', true, 'learner-7', palette)
  const lines = Array.isArray(option.series) ? option.series : [option.series]
  expect(lines[7]).toMatchObject({ itemStyle: { color: palette.accent }, lineStyle: { color: palette.accent } })
  expect(option.tooltip).toMatchObject({
    backgroundColor: palette.surface,
    borderColor: palette.border,
    textStyle: { color: palette.ink },
  })
  expect(option.legend).toMatchObject({ textStyle: { color: palette.muted }, pageIconColor: palette.accent })
})

it('keeps negative imported scores visible while solve counts start at zero', () => {
  const members = [{ userName: 'learner', points: [
    { date: '2026-11-04', score: -150, solvedCount: 0 },
    { date: '2026-11-05', score: 50, solvedCount: 1 },
  ] }]
  expect(buildLeaderboardChart(members, 'score', false, 'zh-CN', true).yAxis).toMatchObject({ min: -150 })
  expect(buildLeaderboardChart(members, 'solves', false, 'zh-CN', true).yAxis).toMatchObject({ min: 0 })
  expect(buildLeaderboardChart(series, 'score', false, 'zh-CN', true).yAxis).toMatchObject({ min: 0 })
})

it('shows years on multi-year timelines and starts with the complete zoom range', () => {
  const members = [{ userName: 'learner', points: [
    { date: '2021-08-25', score: 100, solvedCount: 1 },
    { date: '2024-05-02', score: 250, solvedCount: 2 },
    { date: '2026-11-04', score: 350, solvedCount: 3 },
  ] }]
  const option = buildLeaderboardChart(members, 'score', false, 'zh-CN', true)
  const axis = option.xAxis as { axisLabel: { formatter: (value: number) => string } }
  expect(axis.axisLabel.formatter(Date.parse('2024-05-02T00:00:00Z'))).toContain('2024')
  expect(option.dataZoom).toEqual(expect.arrayContaining([
    expect.objectContaining({ type: 'inside', start: 0, end: 100 }),
    expect.objectContaining({ type: 'slider', start: 0, end: 100 }),
  ]))
  const lines = Array.isArray(option.series) ? option.series : [option.series]
  expect(lines[0]).toMatchObject({ step: 'end', data: [
    ['2021-08-25T00:00:00Z', 100], ['2024-05-02T00:00:00Z', 250], ['2026-11-04T00:00:00Z', 350],
  ] })
})
