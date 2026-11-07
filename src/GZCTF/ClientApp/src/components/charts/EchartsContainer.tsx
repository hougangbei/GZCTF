import { useComputedColorScheme } from '@mantine/core'
import type { EChartsOption } from 'echarts'
import * as echarts from 'echarts'
import { FC, useEffect, useRef } from 'react'

export interface EchartsContainerProps extends React.ComponentPropsWithoutRef<'div'> {
  onReady?: (chart: echarts.ECharts) => void | (() => void)
  option: EChartsOption
  opts?: echarts.EChartsInitOpts
  preserveDataZoom?: boolean
  style?: React.CSSProperties
}

export const EchartsContainer: FC<EchartsContainerProps> = (props) => {
  const chartRef = useRef<HTMLDivElement>(null)
  const chartInstance = useRef<echarts.ECharts | null>(null)
  const zoom = useRef<{ start?: number; end?: number }[]>([])
  const { option, opts, style, preserveDataZoom = false, onReady, ...rest } = props
  const rememberZoom = () => {
    if (!preserveDataZoom || !chartInstance.current) return
    const current = chartInstance.current.getOption().dataZoom as { start?: number; end?: number }[] | undefined
    if (current?.length) zoom.current = current.map(({ start, end }) => ({ start, end }))
  }
  const currentOption = (): EChartsOption => {
    if (!preserveDataZoom || !zoom.current.length || !option.dataZoom) return option
    const ranges = Array.isArray(option.dataZoom) ? option.dataZoom : [option.dataZoom]
    return { ...option, dataZoom: ranges.map((range, index) => ({ ...range, ...zoom.current[index] })) }
  }

  const colorScheme = useComputedColorScheme('light', { getInitialValueInEffect: false })

  useEffect(() => {
    if (chartRef.current && !chartInstance.current) {
      chartInstance.current = echarts.init(chartRef.current, colorScheme === 'dark' ? 'dark' : 'default', opts)
      chartInstance.current.setOption(currentOption())
    }

    return () => {
      if (chartInstance.current) {
        rememberZoom()
        chartInstance.current.dispose()
        chartInstance.current = null
      }
    }
  }, [])

  useEffect(() => {
    if (chartInstance.current) {
      rememberZoom()
      chartInstance.current.dispose()
    }
    chartInstance.current = echarts.init(chartRef.current, colorScheme === 'dark' ? 'dark' : 'default', opts)
    chartInstance.current.setOption(currentOption())
  }, [colorScheme])

  useEffect(() => {
    if (chartInstance.current) {
      rememberZoom()
      chartInstance.current.setOption(currentOption(), true)
    }
  }, [option])

  useEffect(() => {
    if (chartInstance.current) return onReady?.(chartInstance.current)
  }, [option, colorScheme, onReady])

  useEffect(() => {
    const handleResize = () => {
      if (chartInstance.current) {
        chartInstance.current.resize()
      }
    }

    window.addEventListener('resize', handleResize)

    return () => {
      window.removeEventListener('resize', handleResize)
    }
  }, [])

  return <div ref={chartRef} style={style} {...rest} />
}
