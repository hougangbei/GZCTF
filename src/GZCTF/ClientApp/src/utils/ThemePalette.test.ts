import { describe, expect, it } from 'vitest'
import { contrastRatio, resolveThemePalette } from './ThemePalette'

describe('theme palette', () => {
  it('selects readable accents for both color schemes', () => {
    const palette = resolveThemePalette('#E05822')
    expect(palette).not.toBeNull()
    expect(contrastRatio(palette!.colors[palette!.lightShade], '#ffffff')).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(palette!.colors[palette!.lightShade], '#f9fafb')).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(palette!.colors[palette!.darkShade], '#1f2937')).toBeGreaterThanOrEqual(4.5)
  })

  it('rejects invalid and unreadable accents', () => {
    expect(resolveThemePalette('red')).toBeNull()
    expect(resolveThemePalette('#ffffff')).toBeNull()
    expect(resolveThemePalette('#ffff00')).toBeNull()
  })
})
