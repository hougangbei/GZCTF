import { generateColors } from '@mantine/colors-generator'
import type { MantineColorShade } from '@mantine/core'
import { themeSurfaces } from './ThemeSurfaces'

const colorPattern = /^#[0-9a-f]{6}$/i

const luminance = (hex: string) => {
  const channels = [1, 3, 5].map((index) => Number.parseInt(hex.slice(index, index + 2), 16) / 255)
  return channels
    .map((channel) => (channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4))
    .reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index], 0)
}

export const contrastRatio = (first: string, second: string) => {
  const values = [luminance(first), luminance(second)].sort((a, b) => b - a)
  return (values[0] + 0.05) / (values[1] + 0.05)
}

const accessibleShade = (colors: readonly string[], backgrounds: readonly string[], preferred: number) =>
  colors
    .map((color, index) => ({
      index,
      contrast: Math.min(
        ...backgrounds.map((background) => contrastRatio(color, background)),
        contrastRatio(
          color,
          '#' +
            [1, 3, 5]
              .map((index) =>
                Math.round(
                  Number.parseInt(color.slice(index, index + 2), 16) * 0.16 +
                    Number.parseInt(backgrounds[1].slice(index, index + 2), 16) * 0.84
                )
                  .toString(16)
                  .padStart(2, '0')
              )
              .join('')
        )
      ),
    }))
    .filter((item) => item.contrast >= 4.55)
    .sort((a, b) => Math.abs(a.index - preferred) - Math.abs(b.index - preferred))[0]?.index as
    MantineColorShade | undefined

export const resolveThemePalette = (color: string | null | undefined) => {
  if (!color || !colorPattern.test(color)) return null
  if (contrastRatio(color, '#ffffff') < 3) return null
  const colors: [string, string, string, string, string, string, string, string, string, string, ...string[]] = [
    ...generateColors(color),
  ]
  // Some generated palettes have no shade dark enough on inset light surfaces.
  // Extend their darkest stop rather than rejecting an otherwise valid personal color.
  const lightLayers = [themeSurfaces.light.bg, themeSurfaces.light.card, themeSurfaces.light.surface]
  if (accessibleShade(colors, lightLayers, 6) === undefined) {
    const darkest = colors[9]
    for (let step = 1; step <= 20; step++) {
      const factor = 1 - step / 20
      const candidate =
        '#' +
        [1, 3, 5]
          .map((index) =>
            Math.round(Number.parseInt(darkest.slice(index, index + 2), 16) * factor)
              .toString(16)
              .padStart(2, '0')
          )
          .join('')
      colors[9] = candidate
      if (accessibleShade(colors, lightLayers, 6) !== undefined) break
    }
  }
  const lightShade = accessibleShade(
    colors,
    [themeSurfaces.light.bg, themeSurfaces.light.card, themeSurfaces.light.surface],
    6
  )
  const darkShade = accessibleShade(
    colors,
    [themeSurfaces.dark.bg, themeSurfaces.dark.card, themeSurfaces.dark.surface],
    3
  )
  if (lightShade === undefined || darkShade === undefined) return null
  return { colors, lightShade, darkShade }
}
