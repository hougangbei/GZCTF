import { generateColors } from '@mantine/colors-generator'
import type { MantineColorShade } from '@mantine/core'

const colorPattern = /^#[0-9a-f]{6}$/i

const luminance = (hex: string) => {
  const channels = [1, 3, 5].map(index => Number.parseInt(hex.slice(index, index + 2), 16) / 255)
  return channels.map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
    .reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index], 0)
}

export const contrastRatio = (first: string, second: string) => {
  const values = [luminance(first), luminance(second)].sort((a, b) => b - a)
  return (values[0] + 0.05) / (values[1] + 0.05)
}

const accessibleShade = (colors: readonly string[], background: string, preferred: number) =>
  colors.map((color, index) => ({ index, contrast: contrastRatio(color, background) }))
    .filter(item => item.contrast >= 4.5)
    .sort((a, b) => Math.abs(a.index - preferred) - Math.abs(b.index - preferred))[0]?.index as MantineColorShade | undefined

export const resolveThemePalette = (color: string | null | undefined) => {
  if (!color || !colorPattern.test(color)) return null
  if (contrastRatio(color, '#ffffff') < 3) return null
  const colors = generateColors(color)
  const lightShade = accessibleShade(colors, '#f9fafb', 6)
  const darkShade = accessibleShade(colors, '#1f2937', 3)
  if (lightShade === undefined || darkShade === undefined) return null
  return { colors, lightShade, darkShade }
}
