import {
  ActionIcon,
  Avatar,
  Badge,
  Code,
  CSSVariablesResolver,
  Loader,
  MantineThemeOverride,
  Menu,
  Modal,
  Popover,
  Switch,
  Tabs,
  Tooltip,
  TooltipFloating,
  useMantineTheme,
} from '@mantine/core'
import { createStyles } from '@mantine/emotion'
import { useLocalStorage, useMediaQuery } from '@mantine/hooks'
import { useMemo } from 'react'
import { useConfig } from '@Hooks/useConfig'
import { resolveThemePalette } from '@Utils/ThemePalette'
import tooltipClasses from '@Styles/Tooltip.module.css'

const CustomTheme: MantineThemeOverride = {
  colors: {
    gray: [
      '#EBEBEB',
      '#CFCFCF',
      '#B3B3B3',
      '#969696',
      '#7A7A7A',
      '#5E5E5E',
      '#414141',
      '#252525',
      '#202020',
      '#141414',
    ],
    brand: [
      '#EEF0FF',
      '#E0E4FF',
      '#C6CAFF',
      '#A5A8FB',
      '#8386F6',
      '#6366F1',
      '#5558E8',
      '#4749D1',
      '#3638A8',
      '#292B7E',
    ],
    alert: [
      '#FFB4B4',
      '#FFA0A0',
      '#FF8c8c',
      '#FF7878',
      '#FF6464',
      '#FE5050',
      '#FE3c3c',
      '#FE2828',
      '#FC1414',
      '#FC0000',
    ],
    light: [
      '#FFFFFF',
      '#F8F8F8',
      '#EFEFEF',
      '#E0E0E0',
      '#DFDFDF',
      '#D0D0D0',
      '#CFCFCF',
      '#C0C0C0',
      '#BFBFBF',
      '#B0B0B0',
    ],
    dark: [
      '#d5d7d7',
      '#acaeae',
      '#8c8f8f',
      '#666969',
      '#4d4f4f',
      '#343535',
      '#2b2c2c',
      '#1d1e1e',
      '#0c0d0d',
      '#010101',
    ],
  },
  primaryColor: 'brand',
  primaryShade: { light: 6, dark: 3 },
  autoContrast: true,
  fontFamily:
    'Roboto, -apple-system, BlinkMacSystemFont, Segoe UI, PingFang SC, Microsoft YaHei, sans-serif',
  fontFamilyMonospace:
    'JetBrains Mono, ui-monospace, SFMono-Regular, Monaco, Consolas, Courier New, monospace, sans-serif',
  headings: {
    fontFamily: 'Roboto, -apple-system, BlinkMacSystemFont, Segoe UI, PingFang SC, Microsoft YaHei, sans-serif',
  },
  breakpoints: {
    xs: '30em',
    sm: '48em',
    md: '64em',
    lg: '74em',
    xl: '90em',
    w18: '1800px',
    w24: '2400px',
    w30: '3000px',
    w36: '3600px',
    w42: '4200px',
    w48: '4800px',
  },
  components: {
    Loader: Loader.extend({
      defaultProps: {
        type: 'bars',
      },
    }),
    Switch: Switch.extend({
      styles: {
        body: {
          alignItems: 'center',
        },
        labelWrapper: {
          display: 'flex',
        },
      },
    }),
    Modal: Modal.extend({
      defaultProps: {
        centered: true,
        styles: {
          title: {
            fontWeight: 'bold',
          },
        },
      },
    }),
    Popover: Popover.extend({
      defaultProps: {
        withinPortal: true,
      },
    }),
    ActionIcon: ActionIcon.extend({
      defaultProps: {
        variant: 'transparent',
      },
    }),
    Badge: Badge.extend({
      defaultProps: {
        variant: 'outline',
      },
    }),
    Tabs: Tabs.extend({
      styles: {
        tab: {
          padding: 'var(--mantine-spacing-xs)',
          fontWeight: 500,
        },
      },
    }),
    Avatar: Avatar.extend({
      defaultProps: {
        color: 'brand',
      },
    }),
    Menu: Menu.extend({
      styles: {
        item: {
          fontWeight: 500,
        },
      },
    }),
    Code: Code.extend({
      styles: {
        root: {
          fontWeight: 500,
        },
      },
    }),
    Tooltip: Tooltip.extend({
      classNames: tooltipClasses,
    }),
    TooltipFloating: TooltipFloating.extend({
      classNames: tooltipClasses,
    }),
  },
}

export const themeCssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {
    '--hbc-primary': 'var(--mantine-primary-color-filled)',
    '--hbc-primary-hover': 'var(--mantine-primary-color-filled-hover)',
    '--hbc-primary-foreground': 'var(--mantine-primary-color-contrast)',
    '--hbc-primary-soft': 'var(--mantine-primary-color-light)',
  },
  light: {
    '--hbc-bg': '#f9fafb',
    '--hbc-card': '#ffffff',
    '--hbc-surface': '#f3f5fa',
    '--hbc-ink': '#1e293b',
    '--hbc-muted': '#64748b',
    '--hbc-border': '#e2e8f0',
  },
  dark: {
    '--hbc-bg': '#111827',
    '--hbc-card': '#1f2937',
    '--hbc-surface': '#273449',
    '--hbc-ink': '#f8fafc',
    '--hbc-muted': '#cbd5e1',
    '--hbc-border': '#37465d',
  },
})

export enum ColorProvider {
  Managed = 'Managed',
  Default = 'Default',
  Custom = 'Custom',
}

export interface CustomColor {
  provider: ColorProvider
  color: string
}

export const useCustomColor = () => {
  const [customColor, setCustomColorInner] = useLocalStorage<CustomColor>({
    key: 'custom-theme',
    defaultValue: { provider: ColorProvider.Managed, color: '' } as CustomColor,
    getInitialValueInEffect: false,
    serialize: (value: CustomColor) => {
      if (value.provider === ColorProvider.Custom && /^#[0-9A-F]{6}$/i.test(value.color)) {
        return value.color
      } else if (value.provider === ColorProvider.Managed) {
        return ''
      } else {
        return 'brand'
      }
    },
    deserialize: (value?: string) => {
      if (typeof value !== 'string') return { provider: ColorProvider.Managed, color: '' }

      if (value === 'brand') {
        return { provider: ColorProvider.Default, color: '' }
      } else if (/^#[0-9A-F]{6}$/i.test(value)) {
        return { provider: ColorProvider.Custom, color: value }
      } else {
        return { provider: ColorProvider.Managed, color: '' }
      }
    },
  })

  const setCustomColor = (color: CustomColor) => {
    // validate custom color, do not save invalid values
    if (color.provider === ColorProvider.Custom && !resolveThemePalette(color.color)) return

    setCustomColorInner(color)
  }

  // color: null for use platform color, 'brand' for default theme
  //        or hex color string for custom color
  return { customColor, setCustomColor }
}

export const useCustomTheme = () => {
  const { config } = useConfig()
  const { customColor } = useCustomColor()

  const resolvedColor = customColor.provider === ColorProvider.Custom
    ? customColor.color
    : customColor.provider === ColorProvider.Managed ? config.customTheme : null

  const theme = useMemo<MantineThemeOverride>(() => {
    const palette = resolveThemePalette(resolvedColor)
    return palette ? {
        ...CustomTheme,
        colors: {
          ...CustomTheme.colors,
          custom: palette.colors,
        },
        components: {
          ...CustomTheme.components,
          Avatar: Avatar.extend({
            defaultProps: {
              color: 'custom',
            },
          }),
        },
        primaryColor: 'custom',
        primaryShade: { light: palette.lightShade, dark: palette.darkShade },
    } : CustomTheme
  }, [resolvedColor])

  return { theme }
}

export const useIsMobile = (limit?: number) => {
  const theme = useMantineTheme()
  const isMobile = useMediaQuery(`(max-width: ${limit ? `${limit}px` : theme.breakpoints.sm})`)
  return isMobile
}

interface UseDisplayInputStylesProps {
  ff?: 'monospace' | 'text'
  fw?: React.CSSProperties['fontWeight']
  lh?: React.CSSProperties['lineHeight']
  cs?: React.CSSProperties['cursor']
}

export const useDisplayInputStyles = createStyles(
  (theme, { fw = 'normal', lh = '1.5rem', ff = 'text', cs = 'auto' }: UseDisplayInputStylesProps) => ({
    wrapper: {
      width: '100%',
    },
    input: {
      fontWeight: fw,
      fontFamily: ff === 'text' ? theme.fontFamily : theme.fontFamilyMonospace,
      height: lh,
      lineHeight: lh,
      cursor: cs,
      userSelect: 'none',
      minHeight: '1rem',
      maxHeight: '2rem',
    },
  })
)
