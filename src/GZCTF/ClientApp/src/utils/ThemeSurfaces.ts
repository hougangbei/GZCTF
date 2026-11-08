/** Shared neutral layers. Accent and semantic status colors are resolved separately. */
export const themeSurfaces = {
  light: {
    bg: '#f4f5f7',
    card: '#ffffff',
    surface: '#eceef1',
    ink: '#202428',
    muted: '#535c65',
    border: '#c7cdd3',
    controlBorder: '#7b858f',
    disabledBg: '#e3e6ea',
    disabledInk: '#535c65',
  },
  dark: {
    bg: '#141618',
    card: '#202326',
    surface: '#292d31',
    ink: '#f2f3f4',
    muted: '#b3bbc3',
    border: '#454c54',
    controlBorder: '#78838e',
    disabledBg: '#30353a',
    disabledInk: '#b3bbc3',
  },
} as const
