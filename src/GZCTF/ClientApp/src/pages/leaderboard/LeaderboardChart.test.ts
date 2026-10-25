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
    expect((Array.isArray(option.series) ? option.series[0] : option.series)).toMatchObject({ step: 'end', data: [['2026-10-25T00:00:00Z', 1]] })
  })
})

it('uses a teal line for the signed-in learner and keeps that learner visible on mobile', () => {
  const option = buildLeaderboardChart(series, 'score', true, 'zh-CN', true, 'learner-7')
  const lines = Array.isArray(option.series) ? option.series : [option.series]
  expect(lines[7]).toMatchObject({ itemStyle: { color: '#4eb7a1' }, lineStyle: { color: '#4eb7a1', width: 2.3 } })
  expect((option.legend as { selected: Record<string, boolean> }).selected['learner-7']).toBe(true)
})
