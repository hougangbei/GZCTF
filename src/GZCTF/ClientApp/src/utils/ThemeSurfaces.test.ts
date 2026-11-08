import { expect, it } from 'vitest'
import { contrastRatio, resolveThemePalette } from './ThemePalette'
import { themeSurfaces } from './ThemeSurfaces'

it.each(['light', 'dark'] as const)(
  '%s text, placeholder and controls have measured contrast on each layer',
  (scheme) => {
    const colors = themeSurfaces[scheme]
    for (const background of [colors.bg, colors.card, colors.surface]) {
      expect(contrastRatio(colors.ink, background)).toBeGreaterThanOrEqual(7)
      expect(contrastRatio(colors.muted, background)).toBeGreaterThanOrEqual(4.5)
      expect(contrastRatio(colors.controlBorder, background)).toBeGreaterThanOrEqual(3)
    }
    expect(contrastRatio(colors.disabledInk, colors.disabledBg)).toBeGreaterThanOrEqual(4.5)
  }
)

it.each(['#E05822', '#315EAC', '#18804b', '#7c3aed', '#111111'])(
  'custom %s accent is readable on all neutral layers',
  (base) => {
    const palette = resolveThemePalette(base)!
    expect(palette).not.toBeNull()
    for (const scheme of ['light', 'dark'] as const) {
      const shade = scheme === 'light' ? palette.lightShade : palette.darkShade
      for (const background of Object.values(themeSurfaces[scheme]).filter(
        (color) =>
          color === themeSurfaces[scheme].bg ||
          color === themeSurfaces[scheme].card ||
          color === themeSurfaces[scheme].surface
      ))
        expect(contrastRatio(palette.colors[shade], background)).toBeGreaterThanOrEqual(4.5)
    }
  }
)

it.each(['#0161f1', '#012066'])('custom %s remains readable when its soft action is hovered', (base) => {
  const palette = resolveThemePalette(base)!
  for (const scheme of ['light', 'dark'] as const) {
    const shade = scheme === 'light' ? palette.lightShade : palette.darkShade
    const foreground = palette.colors[shade]
    const background = themeSurfaces[scheme].card
    const mixed =
      '#' +
      [1, 3, 5]
        .map((index) =>
          Math.round(
            parseInt(foreground.slice(index, index + 2), 16) * 0.16 +
              parseInt(background.slice(index, index + 2), 16) * 0.84
          )
            .toString(16)
            .padStart(2, '0')
        )
        .join('')
    expect(contrastRatio(foreground, mixed)).toBeGreaterThanOrEqual(4.5)
  }
})
