import { expect, it } from 'vitest'
import { findHoveredCurve } from './LeaderboardHover'

it('selects the curve under the pointer instead of the first or bottom series', () => {
  const lines = [
    [
      [0, 100],
      [100, 80],
    ],
    [
      [0, 20],
      [70, 10],
    ],
  ]
  expect(findHoveredCurve(lines, [40, 21])).toEqual({ seriesIndex: 1, dataIndex: 0 })
  expect(findHoveredCurve(lines, [40, 99])).toEqual({ seriesIndex: 0, dataIndex: 0 })
})
it('matches step-end horizontal and vertical segments, without inventing interpolated values', () => {
  const lines = [
    [
      [0, 100],
      [100, 20],
      [200, 10],
    ],
  ]
  expect(findHoveredCurve(lines, [50, 100])).toEqual({ seriesIndex: 0, dataIndex: 0 })
  expect(findHoveredCurve(lines, [100, 60])).toEqual({ seriesIndex: 0, dataIndex: 1 })
  expect(findHoveredCurve(lines, [150, 20])).toEqual({ seriesIndex: 0, dataIndex: 1 })
  expect(findHoveredCurve(lines, [50, 60])).toBeUndefined()
})
it('ignores deselected lines and handles isolated points and empty data', () => {
  expect(findHoveredCurve([[], [[20, 30]]], [21, 31])).toEqual({ seriesIndex: 1, dataIndex: 0 })
  expect(findHoveredCurve([[[20, 30]]], [21, 31], new Set([0]))).toBeUndefined()
  expect(findHoveredCurve([], [0, 0])).toBeUndefined()
})
