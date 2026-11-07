import type { ECharts } from 'echarts'
import type { LeaderboardSeries, Metric } from './LeaderboardChart'

// Match actual step-end geometry in screen pixels, not the nearest date on the axis.
export const findHoveredCurve = (
  lines: number[][][],
  pointer: number[],
  hidden = new Set<number>(),
  tolerance = 12
): { seriesIndex: number; dataIndex: number } | undefined => {
  let nearest: { seriesIndex: number; dataIndex: number } | undefined
  let distance = tolerance * tolerance
  const consider = (a: number[], b: number[], seriesIndex: number, dataIndex: number) => {
    if (![...a, ...b].every(Number.isFinite)) return
    const dx = b[0] - a[0],
      dy = b[1] - a[1]
    const length = dx * dx + dy * dy
    const fraction = length
      ? Math.max(0, Math.min(1, ((pointer[0] - a[0]) * dx + (pointer[1] - a[1]) * dy) / length))
      : 0
    const squared = (pointer[0] - a[0] - fraction * dx) ** 2 + (pointer[1] - a[1] - fraction * dy) ** 2
    if (squared < distance) {
      distance = squared
      nearest = { seriesIndex, dataIndex }
    }
  }
  lines.forEach((points, seriesIndex) => {
    if (hidden.has(seriesIndex)) return
    if (points.length === 1) consider(points[0], points[0], seriesIndex, 0)
    for (let i = 1; i < points.length; i++) {
      const corner = [points[i][0], points[i - 1][1]]
      consider(points[i - 1], corner, seriesIndex, i - 1)
      consider(corner, points[i], seriesIndex, i)
    }
  })
  return nearest
}

export const bindLeaderboardHover = (chart: ECharts, members: LeaderboardSeries[], metric: Metric) => {
  let projected: number[][][] | undefined
  let activeSeries: number | undefined
  let activePoint: number | undefined
  const clear = () => {
    if (chart.isDisposed()) return
    if (activeSeries !== undefined) chart.dispatchAction({ type: 'downplay', seriesIndex: activeSeries })
    activeSeries = undefined
    activePoint = undefined
    chart.dispatchAction({ type: 'hideTip' })
  }
  const invalidate = () => {
    projected = undefined
    clear()
  }
  const move = (event: { offsetX: number; offsetY: number }) => {
    const pointer = [event.offsetX, event.offsetY]
    if (!chart.containPixel({ gridIndex: 0 }, pointer)) {
      clear()
      return
    }
    projected ??= members.map((member) =>
      member.points.map(
        (point) =>
          chart.convertToPixel({ gridIndex: 0 }, [
            Date.parse(`${point.date}T00:00:00Z`),
            metric === 'score' ? point.score : point.solvedCount,
          ]) as number[]
      )
    )
    const legend = chart.getOption().legend as { selected?: Record<string, boolean> }[]
    const hidden = new Set(
      members.flatMap((member, i) => (legend?.[0]?.selected?.[member.userName] === false ? [i] : []))
    )
    const match = findHoveredCurve(projected, pointer, hidden)
    if (!match) {
      clear()
      return
    }
    if (activeSeries !== match.seriesIndex || activePoint !== match.dataIndex) {
      clear()
      activeSeries = match.seriesIndex
      activePoint = match.dataIndex
      chart.dispatchAction({ type: 'highlight', ...match })
    }
    chart.dispatchAction({ type: 'showTip', ...match, position: pointer })
  }
  chart.getZr().on('mousemove', move)
  chart.getZr().on('globalout', clear)
  chart.on('datazoom', invalidate)
  chart.on('legendselectchanged', invalidate)
  window.addEventListener('resize', invalidate)
  return () => {
    window.removeEventListener('resize', invalidate)
    if (chart.isDisposed()) return
    chart.getZr().off('mousemove', move)
    chart.getZr().off('globalout', clear)
    chart.off('datazoom', invalidate)
    chart.off('legendselectchanged', invalidate)
  }
}
